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

    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // No catalog price in this model, so accept client unitPrice but totalAmount server-side.
        var order = new Order
        {
            CustomerId = request.CustomerId,
            TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
            Status = OrderStatus.Pending,
        };
        foreach (var item in request.Items)
            order.Items.Add(new OrderItem { ProductId = item.ProductId, Quantity = item.Quantity, UnitPrice = item.UnitPrice });

        var reserved = new List<OrderItem>();   // exactly what got reserved → exact compensation

        try
        {
            foreach (var item in order.Items)
            {
                await _inventory.ReserveAsync(item.ProductId, item.Quantity, ct);
                reserved.Add(item);
            }

            var payment = await _paymentApi.ProcessPaymentAsync(
                new ProcessPaymentRequest { OrderId = order.Id.ToString(), Amount = order.TotalAmount }, ct);

            if (payment.Status != PaymentStatus.Completed)      // payment FAILED
            {
                await ReleaseAsync(reserved, ct);
                order.Status = OrderStatus.Cancelled;
                await SaveAsync(order, ct);
                throw new PaymentProcessingException("Payment processing failed; order cancelled and inventory released.");
            }

            order.Status = OrderStatus.Confirmed;
        }
        catch (InsufficientInventoryException)
        {
            await ReleaseAsync(reserved, ct);
            order.Status = OrderStatus.Cancelled;
            await SaveAsync(order, ct);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or PaymentServiceUnavailableException)   // payment UNREACHABLE
        {
            await ReleaseAsync(reserved, ct);
            order.Status = OrderStatus.Cancelled;
            await SaveAsync(order, ct);
            throw new PaymentServiceUnavailableException("Payment service unreachable; order cancelled and inventory released.");
        }

        await SaveAsync(order, ct);
        return Map(order);
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