using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.Models;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Tests;

public class InventoryServiceTests
{
    private static (OrderDbContext db, InventoryService service) Build()
    {
        var db = new OrderDbContext(new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.InventoryItems.AddRange(
            new InventoryItem { ProductId = "PROD-001", AvailableQuantity = 100 },
            new InventoryItem { ProductId = "PROD-002", AvailableQuantity = 0 });
        db.SaveChanges();
        return (db, new InventoryService(db));
    }

    [Fact]
    public async Task Reserve_MovesQuantityFromAvailableToReserved()
    {
        var (_, service) = Build();
        var result = await service.ReserveAsync("PROD-001", 30, CancellationToken.None);
        Assert.Equal(70, result.AvailableQuantity);
        Assert.Equal(30, result.ReservedQuantity);
    }

    [Fact]
    public async Task Release_MovesQuantityBack()
    {
        var (_, service) = Build();
        await service.ReserveAsync("PROD-001", 30, CancellationToken.None);
        var result = await service.ReleaseAsync("PROD-001", 30, CancellationToken.None);
        Assert.Equal(100, result.AvailableQuantity);
        Assert.Equal(0, result.ReservedQuantity);
    }

    [Fact]
    public async Task Reserve_InsufficientStock_Throws()
    {
        var (_, service) = Build();
        await Assert.ThrowsAsync<InsufficientInventoryException>(
            () => service.ReserveAsync("PROD-002", 1, CancellationToken.None));
    }

    [Fact]
    public async Task Reserve_UnknownProduct_Throws()
    {
        var (_, service) = Build();
        await Assert.ThrowsAsync<ProductNotFoundException>(
            () => service.ReserveAsync("NOPE", 1, CancellationToken.None));
    }
}