using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Services;

public sealed class OrderService : IOrderService
{
    private readonly OrderDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IPaymentApiClient _paymentApi;

    public OrderService(OrderDbContext db, IInventoryService inventory, IPaymentApiClient paymentApi)
    {
        _db = db; _inventory = inventory; _paymentApi = paymentApi;
    }

    private static readonly JsonSerializerOptions BodyJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string FailureProductNotFound = "ProductNotFound";
    private const string FailureInsufficientInventory = "InsufficientInventory";
    private const string FailurePaymentProcessing = "PaymentProcessing";
    private const string FailurePaymentUnavailable = "PaymentServiceUnavailable";

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken ct)
    {
        IdempotencyKeys.Validate(idempotencyKey);
        var hash = IdempotencyKeys.Fingerprint(request);

        // The primary key (CustomerId, Key) is the lock: insert first, branch on duplicate. Never check-then-insert.
        var record = new IdempotencyRecord
        {
            CustomerId = request.CustomerId, Key = idempotencyKey, RequestHash = hash,
            State = IdempotencyState.InProgress, OrderId = Guid.NewGuid(),
        };
        _db.IdempotencyRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Duplicate-key errors differ by provider (DbUpdateException, or other types from InMemory): detach our
            // failed insert and confirm by loading the winner's row. No row => not a duplicate, so rethrow.
            _db.Entry(record).State = EntityState.Detached;
            var existing = await _db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.CustomerId == request.CustomerId && r.Key == idempotencyKey, ct);
            if (existing is null) throw;
            return Replay(existing, hash);
        }

        // Known gap: a crash after this point (before the record is completed or removed) leaves it InProgress
        // forever, so retries get 409. Fixing needs persist-Pending-first + a sweeper (see PROGRESS.md backlog).
        try
        {
            var response = await RunSagaAsync(request, record.OrderId, ct);
            record.State = IdempotencyState.Completed;
            record.StatusCode = StatusCodes.Status201Created;
            record.ResponseBody = JsonSerializer.Serialize(response, BodyJson);
            await _db.SaveChangesAsync(CancellationToken.None);
            return new CreateOrderResult(response, false);
        }
        catch (Exception ex) when (FailureKindOf(ex) is not null)
        {
            record.State = IdempotencyState.Completed;
            record.FailureKind = FailureKindOf(ex);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            // Unexpected failure: free the key so the client may retry.
            _db.IdempotencyRecords.Remove(record);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static CreateOrderResult Replay(IdempotencyRecord existing, string hash)
    {
        if (existing.RequestHash != hash) throw new IdempotencyKeyReuseException();
        if (existing.State == IdempotencyState.InProgress) throw new RequestInProgressException();

        if (existing.FailureKind is null)
            return new CreateOrderResult(JsonSerializer.Deserialize<OrderResponse>(existing.ResponseBody!, BodyJson)!, true);

        // Rethrow the domain exception so ApiExceptionHandler stays the only place that maps status codes.
        throw existing.FailureKind switch
        {
            FailureProductNotFound => new ProductNotFoundException("(replayed)"),
            FailureInsufficientInventory => new InsufficientInventoryException("(replayed)", 0, 0),
            FailurePaymentProcessing => new PaymentProcessingException("Replayed: payment processing failed."),
            FailurePaymentUnavailable => new PaymentServiceUnavailableException("Replayed: payment service unreachable."),
            _ => new InvalidOperationException($"Unknown stored failure kind '{existing.FailureKind}'."),
        };
    }

    private static string? FailureKindOf(Exception ex) => ex switch
    {
        ProductNotFoundException => FailureProductNotFound,
        InsufficientInventoryException => FailureInsufficientInventory,
        PaymentProcessingException => FailurePaymentProcessing,
        PaymentServiceUnavailableException => FailurePaymentUnavailable,
        _ => null,
    };

    private async Task<OrderResponse> RunSagaAsync(CreateOrderRequest request, Guid orderId, CancellationToken ct)
    {
        // No catalog price in this model, so accept client unitPrice but totalAmount server-side.
        var order = new Order
        {
            Id = orderId,
            CustomerId = request.CustomerId,
            TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
            Status = OrderStatus.Pending,
        };
        foreach (var item in request.Items)
            order.Items.Add(new OrderItem { ProductId = item.ProductId, Quantity = item.Quantity, UnitPrice = item.UnitPrice });

        var reserved = new List<OrderItem>();   // exactly what got reserved → exact compensation

        PaymentResponse payment;
        try
        {
            foreach (var item in order.Items)
            {
                await _inventory.ReserveAsync(item.ProductId, item.Quantity, ct);
                reserved.Add(item);
            }

            payment = await _paymentApi.ProcessPaymentAsync(
                new ProcessPaymentRequest { OrderId = order.Id.ToString(), Amount = order.TotalAmount }, ct);
        }
        catch (Exception ex)
        {
            // Any failure (expected or not) must undo exactly what was reserved, or stock leaks.
            await CancelAsync(order, reserved);
            if (ex is HttpRequestException or PaymentServiceUnavailableException)   // payment UNREACHABLE
                throw new PaymentServiceUnavailableException("Payment service unreachable; order cancelled and inventory released.");
            throw;
        }

        if (payment.Status != PaymentStatus.Completed)      // payment FAILED
        {
            await CancelAsync(order, reserved);
            throw new PaymentProcessingException("Payment processing failed; order cancelled and inventory released.");
        }

        order.Status = OrderStatus.Confirmed;
        await SaveAsync(order, ct);
        return Map(order);
    }

    // Compensation ignores the request's token: if the caller cancelled, we must still undo the reservations.
    private async Task CancelAsync(Order order, IEnumerable<OrderItem> reserved)
    {
        await ReleaseAsync(reserved, CancellationToken.None);
        order.Status = OrderStatus.Cancelled;
        await SaveAsync(order, CancellationToken.None);
    }

    private async Task ReleaseAsync(IEnumerable<OrderItem> items, CancellationToken ct)
    {
        foreach (var item in items)
            await _inventory.ReleaseAsync(item.ProductId, item.Quantity, ct);
    }

    private async Task SaveAsync(Order order, CancellationToken ct)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
    }

    private static OrderResponse Map(Order o) =>
        new(o.Id, o.CustomerId,
            o.Items.Select(i => new OrderItemResponse(i.ProductId, i.Quantity, i.UnitPrice, i.Quantity * i.UnitPrice)).ToList(),
            o.TotalAmount, o.Status, o.CreatedAt, o.UpdatedAt);
    public async Task<OrderResponse?> GetOrderAsync(Guid id, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? null : Map(order);
    }

    public async Task<OrderListResponse> GetOrdersAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Orders.Include(o => o.Items).AsNoTracking();
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new OrderListResponse(orders.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<OrderResponse?> UpdateOrderStatusAsync(Guid id, OrderStatus status, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;
        order.Status = status;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(order);
    }
}