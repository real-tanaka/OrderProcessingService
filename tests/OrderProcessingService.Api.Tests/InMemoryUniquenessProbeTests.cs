using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Tests;

// Guards the assumption the idempotency "DB uniqueness is the lock" design relies on in tests.
public class InMemoryUniquenessProbeTests
{
    [Fact]
    public async Task InMemory_EnforcesCompositePk_AcrossContextsSharingADatabase()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var a = new OrderDbContext(options);
        await using var b = new OrderDbContext(options);
        a.IdempotencyRecords.Add(new IdempotencyRecord { CustomerId = "c", Key = "k", RequestHash = "h" });
        await a.SaveChangesAsync();
        b.IdempotencyRecords.Add(new IdempotencyRecord { CustomerId = "c", Key = "k", RequestHash = "h" });

        // Observed: ArgumentException ("An item with the same key has already been added"), not DbUpdateException.
        var ex = await Record.ExceptionAsync(() => b.SaveChangesAsync());
        Assert.NotNull(ex);
    }
}
