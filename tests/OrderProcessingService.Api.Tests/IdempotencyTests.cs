using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Controllers;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Tests;

public class IdempotencyTests
{
    private static DbContextOptions<OrderDbContext> NewStore()
        => new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static void Seed(DbContextOptions<OrderDbContext> store)
    {
        using var db = new OrderDbContext(store);
        db.InventoryItems.AddRange(
            new InventoryItem { ProductId = "PROD-001", AvailableQuantity = 100 },
            new InventoryItem { ProductId = "PROD-002", AvailableQuantity = 0 });
        db.SaveChanges();
    }

    // Each call gets its own context + service over the same store, like separate HTTP requests.
    private static OrderService NewService(DbContextOptions<OrderDbContext> store, IPaymentApiClient pay)
    {
        var db = new OrderDbContext(store);
        return new OrderService(db, new InventoryService(db), pay);
    }

    private static CreateOrderRequest Request(string customer, params (string productId, int qty, decimal price)[] items) => new()
    {
        CustomerId = customer,
        Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.productId, Quantity = i.qty, UnitPrice = i.price }).ToList()
    };

    private static async Task<InventoryItem> Stock(DbContextOptions<OrderDbContext> store, string productId)
    {
        await using var db = new OrderDbContext(store);
        return await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId);
    }

    private static async Task<int> OrderCount(DbContextOptions<OrderDbContext> store)
    {
        await using var db = new OrderDbContext(store);
        return await db.Orders.CountAsync();
    }

    [Fact]
    public async Task SameKeyAndBody_RunsSagaOnce_AndReplaysSameOrder()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);
        var req = Request("c1", ("PROD-001", 2, 30.00m));

        var first = await NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None);
        var second = await NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None);

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Order.Id, second.Order.Id);
        Assert.Equal(first.Order with { Items = [] }, second.Order with { Items = [] });
        Assert.Equal(first.Order.Items, second.Order.Items);
        Assert.Equal(1, pay.CallCount);
        Assert.Equal(first.Order.Id.ToString(), pay.LastRequest!.OrderId);   // record's OrderId became Order.Id
        var stock = await Stock(store, "PROD-001");
        Assert.Equal(98, stock.AvailableQuantity);
        Assert.Equal(2, stock.ReservedQuantity);
        Assert.Equal(1, await OrderCount(store));
    }

    [Fact]
    public async Task SameKey_DifferentBody_ThrowsKeyReuse()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);
        await NewService(store, pay).CreateOrderAsync(Request("c1", ("PROD-001", 2, 30.00m)), "key-1", CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyReuseException>(() =>
            NewService(store, pay).CreateOrderAsync(Request("c1", ("PROD-001", 3, 30.00m)), "key-1", CancellationToken.None));
        Assert.Equal(1, pay.CallCount);
    }

    [Fact]
    public async Task ReplayedInsufficientStock_ThrowsSameType_WithoutTouchingPaymentOrStock()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);
        var req = Request("c1", ("PROD-001", 2, 30.00m), ("PROD-002", 1, 10.00m));

        await Assert.ThrowsAsync<InsufficientInventoryException>(() =>
            NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None));
        var ordersBefore = await OrderCount(store);
        await Assert.ThrowsAsync<InsufficientInventoryException>(() =>
            NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None));

        Assert.Equal(0, pay.CallCount);
        Assert.Equal(ordersBefore, await OrderCount(store));
        var stock = await Stock(store, "PROD-001");
        Assert.Equal(100, stock.AvailableQuantity);
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Fact]
    public async Task ReplayedPaymentDeclined_ThrowsSameType_AndDoesNotChargeAgain()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Failed);
        var req = Request("c1", ("PROD-001", 2, 30.00m));

        await Assert.ThrowsAsync<PaymentProcessingException>(() =>
            NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None));
        await Assert.ThrowsAsync<PaymentProcessingException>(() =>
            NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None));

        Assert.Equal(1, pay.CallCount);
        var stock = await Stock(store, "PROD-001");
        Assert.Equal(100, stock.AvailableQuantity);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(1, await OrderCount(store));
    }

    [Fact]
    public async Task ReplayedUnknownProduct_And_PaymentUnreachable_ThrowSameType()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);
        var unknown = Request("c1", ("PROD-404", 1, 10.00m));
        await Assert.ThrowsAsync<ProductNotFoundException>(() => NewService(store, pay).CreateOrderAsync(unknown, "k-404", CancellationToken.None));
        await Assert.ThrowsAsync<ProductNotFoundException>(() => NewService(store, pay).CreateOrderAsync(unknown, "k-404", CancellationToken.None));

        pay.ThrowHttpException = true;
        var req = Request("c1", ("PROD-001", 1, 10.00m));
        await Assert.ThrowsAsync<PaymentServiceUnavailableException>(() => NewService(store, pay).CreateOrderAsync(req, "k-503", CancellationToken.None));
        Assert.Equal(1, pay.CallCount);
        await Assert.ThrowsAsync<PaymentServiceUnavailableException>(() => NewService(store, pay).CreateOrderAsync(req, "k-503", CancellationToken.None));
        Assert.Equal(1, pay.CallCount);
    }

    [Fact]
    public async Task UnexpectedFailure_RemovesRecord_SoClientCanRetry()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed) { ExceptionToThrow = new InvalidOperationException("boom") };
        var req = Request("c1", ("PROD-001", 2, 30.00m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None));
        pay.ExceptionToThrow = null;
        var retry = await NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None);

        Assert.False(retry.Replayed);
        Assert.Equal(OrderStatus.Confirmed, retry.Order.Status);
    }

    [Fact]
    public async Task DifferentCustomers_SameKey_AreIndependent()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);

        var a = await NewService(store, pay).CreateOrderAsync(Request("alice", ("PROD-001", 1, 10.00m)), "same", CancellationToken.None);
        var b = await NewService(store, pay).CreateOrderAsync(Request("bob", ("PROD-001", 1, 10.00m)), "same", CancellationToken.None);

        Assert.False(a.Replayed);
        Assert.False(b.Replayed);
        Assert.NotEqual(a.Order.Id, b.Order.Id);
        Assert.Equal(2, pay.CallCount);
    }

    [Fact]
    public void Fingerprint_IgnoresItemOrder_ButNotContent()
    {
        var ab = Request("c1", ("PROD-001", 1, 10.00m), ("PROD-002", 2, 5.00m));
        var ba = Request("c1", ("PROD-002", 2, 5.0m), ("PROD-001", 1, 10m));
        Assert.Equal(IdempotencyKeys.Fingerprint(ab), IdempotencyKeys.Fingerprint(ba));
        Assert.NotEqual(IdempotencyKeys.Fingerprint(ab), IdempotencyKeys.Fingerprint(Request("c1", ("PROD-001", 1, 10.00m), ("PROD-002", 2, 5.01m))));
        Assert.NotEqual(IdempotencyKeys.Fingerprint(ab), IdempotencyKeys.Fingerprint(Request("c2", ("PROD-001", 1, 10.00m), ("PROD-002", 2, 5.00m))));
    }

    [Fact]
    public async Task ReorderedItems_WithSameKey_ReplaysInsteadOfConflicting()
    {
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);
        // PROD-002 has no stock, so the first outcome is a stored InsufficientInventory failure.
        await Assert.ThrowsAsync<InsufficientInventoryException>(() => NewService(store, pay)
            .CreateOrderAsync(Request("c1", ("PROD-001", 1, 10m), ("PROD-002", 1, 5m)), "k", CancellationToken.None));
        // Same items in another order: replayed failure, not IdempotencyKeyReuseException.
        await Assert.ThrowsAsync<InsufficientInventoryException>(() => NewService(store, pay)
            .CreateOrderAsync(Request("c1", ("PROD-002", 1, 5m), ("PROD-001", 1, 10m)), "k", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsMissingOrBlank(string? key)
        => Assert.Throws<ArgumentException>(() => IdempotencyKeys.Validate(key));

    [Fact]
    public void Validate_RejectsOver200Chars_AcceptsExactly200()
    {
        IdempotencyKeys.Validate(new string('a', 200));
        Assert.Throws<ArgumentException>(() => IdempotencyKeys.Validate(new string('a', 201)));
    }

    // Controller-level: a missing header throws ArgumentException, which ApiExceptionHandler maps to 400
    // (the handler itself is internal and is not exercised without a WebApplicationFactory).
    private sealed class FakeOrderService : IOrderService
    {
        public bool Replayed { get; init; }
        public int Calls { get; private set; }
        public Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken ct)
        {
            Calls++;
            var order = new OrderResponse(Guid.NewGuid(), request.CustomerId, [], 0m, OrderStatus.Confirmed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return Task.FromResult(new CreateOrderResult(order, Replayed));
        }
        public Task<OrderResponse?> GetOrderAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<OrderListResponse> GetOrdersAsync(int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<OrderResponse?> UpdateOrderStatusAsync(Guid id, OrderStatus status, CancellationToken ct) => throw new NotSupportedException();
    }

    private static OrdersController NewController(FakeOrderService service) => new(service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task Controller_MissingHeader_ThrowsArgumentException_AndDoesNotCallService()
    {
        var service = new FakeOrderService();
        var controller = NewController(service);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.Create(Request("c1", ("PROD-001", 1, 10m)), null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.Create(Request("c1", ("PROD-001", 1, 10m)), "  ", CancellationToken.None));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Controller_Replay_Returns201_WithReplayedHeader()
    {
        var controller = NewController(new FakeOrderService { Replayed = true });
        var result = await controller.Create(Request("c1", ("PROD-001", 1, 10m)), "k", CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replayed"].ToString());

        var fresh = NewController(new FakeOrderService());
        await fresh.Create(Request("c1", ("PROD-001", 1, 10m)), "k", CancellationToken.None);
        Assert.False(fresh.Response.Headers.ContainsKey("Idempotent-Replayed"));
    }

    // Holds the winning saga inside the payment call until released, so losers deterministically see InProgress.
    private sealed class GatedPaymentApiClient : IPaymentApiClient
    {
        private int _calls;
        public int CallCount => _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Release.Task;
            return new PaymentResponse(Guid.NewGuid(), request.OrderId, request.Amount, PaymentStatus.Completed, DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task ParallelCallsSameKey_RunExactlyOneSaga()
    {
        const int n = 10;
        var store = NewStore(); Seed(store);
        var pay = new GatedPaymentApiClient();
        var req = Request("c1", ("PROD-001", 2, 30.00m));

        var tasks = Enumerable.Range(0, n)
            .Select(_ => Task.Run(() => NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None)))
            .ToList();

        // The winner is parked in the payment call; the other n-1 must all finish (as RequestInProgress) first.
        await pay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (tasks.Count(t => t.IsCompleted) < n - 1 && DateTime.UtcNow < deadline) await Task.Delay(5);
        pay.Release.SetResult();
        await Task.WhenAll(tasks.Select(t => Record.ExceptionAsync(() => t)));

        var succeeded = tasks.Where(t => t.IsCompletedSuccessfully).ToList();
        var failed = tasks.Where(t => t.IsFaulted).ToList();
        Assert.Equal(1, succeeded.Count);
        Assert.False(succeeded[0].Result.Replayed);
        Assert.Equal(n - 1, failed.Count);
        Assert.All(failed, t => Assert.IsType<RequestInProgressException>(t.Exception!.InnerException));
        Assert.Equal(1, pay.CallCount);
        Assert.Equal(1, await OrderCount(store));
        var stock = await Stock(store, "PROD-001");
        Assert.Equal(98, stock.AvailableQuantity);
        Assert.Equal(2, stock.ReservedQuantity);
    }

    [Fact]
    public async Task ParallelCallsSameKey_Ungated_NeverRunSecondSaga()
    {
        const int n = 10;
        var store = NewStore(); Seed(store);
        var pay = new OrderServiceTests.FakePaymentApiClient(PaymentStatus.Completed);
        var req = Request("c1", ("PROD-001", 2, 30.00m));

        var results = await Task.WhenAll(Enumerable.Range(0, n).Select(_ => Task.Run(async () =>
        {
            try { return (CreateOrderResult?)await NewService(store, pay).CreateOrderAsync(req, "key-1", CancellationToken.None); }
            catch (RequestInProgressException) { return null; }
        })));

        Assert.Equal(1, pay.CallCount);
        Assert.Equal(1, await OrderCount(store));
        Assert.Single(results, r => r is { Replayed: false });
        Assert.Single(results.Where(r => r is not null).Select(r => r!.Order.Id).Distinct());
    }
}
