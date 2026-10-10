using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Tests;

public class OrderServiceTests
{
    // Hand-written fake for the HTTP payment boundary: force a result or a failure without a real service.
    private sealed class FakePaymentApiClient : IPaymentApiClient
    {
        private readonly PaymentStatus _status;
        public bool ThrowHttpException { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public ProcessPaymentRequest? LastRequest { get; private set; }
        public FakePaymentApiClient(PaymentStatus status) => _status = status;

        public Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct)
        {
            LastRequest = request;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            if (ThrowHttpException) throw new HttpRequestException("payment service unreachable");
            return Task.FromResult(new PaymentResponse(Guid.NewGuid(), request.OrderId, request.Amount, _status, DateTimeOffset.UtcNow));
        }
    }

    private static (OrderDbContext db, InventoryService inventory, FakePaymentApiClient payment) Build()
    {
        var db = new OrderDbContext(new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.InventoryItems.AddRange(
            new InventoryItem { ProductId = "PROD-001", AvailableQuantity = 100 },
            new InventoryItem { ProductId = "PROD-002", AvailableQuantity = 0 });
        db.SaveChanges();
        return (db, new InventoryService(db), new FakePaymentApiClient(PaymentStatus.Completed));
    }

    private static OrderService CreateService(OrderDbContext db, IInventoryService inv, IPaymentApiClient pay)
        => new(db, inv, pay);

    private static CreateOrderRequest Request(params (string productId, int qty, decimal price)[] items) => new()
    {
        CustomerId = "customer-1",
        Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.productId, Quantity = i.qty, UnitPrice = i.price }).ToList()
    };

    [Fact]
    public async Task HappyPath_ConfirmsOrder_ReservesInventory_SendsCorrectAmount()
    {
        var (db, inventory, payment) = Build();
        var service = CreateService(db, inventory, payment);
        var result = await service.CreateOrderAsync(Request(("PROD-001", 2, 30.00m)), CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, result.Status);
        Assert.Equal(60.00m, result.TotalAmount);
        Assert.Equal(60.00m, payment.LastRequest!.Amount);
        var item = await db.InventoryItems.FindAsync("PROD-001");
        Assert.Equal(98, item!.AvailableQuantity);
        Assert.Equal(2, item.ReservedQuantity);
    }

    [Fact]
    public async Task PaymentFails_ReleasesInventory_And_CancelsOrder()
    {
        var (db, inventory, _) = Build();
        var failing = new FakePaymentApiClient(PaymentStatus.Failed);
        var service = CreateService(db, inventory, failing);

        await Assert.ThrowsAsync<PaymentProcessingException>(
            () => service.CreateOrderAsync(Request(("PROD-001", 2, 30.00m)), CancellationToken.None));

        var item = await db.InventoryItems.FindAsync("PROD-001");
        Assert.Equal(100, item!.AvailableQuantity);   // released back
        Assert.Equal(0, item.ReservedQuantity);
        var order = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task InsufficientStock_Throws_And_ReservesNothing()
    {
        var (db, inventory, payment) = Build();
        var service = CreateService(db, inventory, payment);

        await Assert.ThrowsAsync<InsufficientInventoryException>(
            () => service.CreateOrderAsync(Request(("PROD-002", 1, 10.00m)), CancellationToken.None));

        var item = await db.InventoryItems.FindAsync("PROD-002");
        Assert.Equal(0, item!.AvailableQuantity);
        var order = await db.Orders.SingleAsync();      // rejected attempt is persisted as cancelled
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task PartialReserve_ReleasesOnlyTheReservedLines()
    {
        var (db, inventory, payment) = Build();
        var service = CreateService(db, inventory, payment);

        await Assert.ThrowsAsync<InsufficientInventoryException>(
            () => service.CreateOrderAsync(Request(("PROD-001", 2, 30.00m), ("PROD-002", 1, 10.00m)), CancellationToken.None));

        var first = await db.InventoryItems.FindAsync("PROD-001");
        Assert.Equal(100, first!.AvailableQuantity);   // first line released back
        Assert.Equal(0, first.ReservedQuantity);
    }

    [Fact]
    public async Task PaymentUnreachable_CancelsOrder_And_ReleasesInventory()
    {
        var (db, inventory, payment) = Build();
        payment.ThrowHttpException = true;
        var service = CreateService(db, inventory, payment);

        await Assert.ThrowsAsync<PaymentServiceUnavailableException>(
            () => service.CreateOrderAsync(Request(("PROD-001", 1, 10.00m)), CancellationToken.None));

        var item = await db.InventoryItems.FindAsync("PROD-001");
        Assert.Equal(100, item!.AvailableQuantity);
        var order = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task UnknownProductOnLaterLine_ReleasesEarlierLines_And_CancelsOrder()
    {
        var (db, inventory, payment) = Build();
        var service = CreateService(db, inventory, payment);

        await Assert.ThrowsAsync<ProductNotFoundException>(
            () => service.CreateOrderAsync(Request(("PROD-001", 2, 30.00m), ("PROD-404", 1, 10.00m)), CancellationToken.None));

        var first = await db.InventoryItems.FindAsync("PROD-001");
        Assert.Equal(100, first!.AvailableQuantity);
        Assert.Equal(0, first.ReservedQuantity);
        var order = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task UnexpectedPaymentException_ReleasesInventory_And_Rethrows()
    {
        var (db, inventory, payment) = Build();
        payment.ExceptionToThrow = new InvalidOperationException("boom");
        var service = CreateService(db, inventory, payment);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateOrderAsync(Request(("PROD-001", 2, 30.00m)), CancellationToken.None));

        var item = await db.InventoryItems.FindAsync("PROD-001");
        Assert.Equal(100, item!.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        var order = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
