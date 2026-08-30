using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly OrderDbContext _db;

    public InventoryService(OrderDbContext db) => _db = db;

    public async Task<InventoryResponse?> GetInventoryAsync(string productId, CancellationToken ct)
    {
        var item = await _db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.ProductId == productId, ct);
        return item is null ? null : Map(item);
    }

    public async Task<InventoryResponse> ReserveAsync(string productId, int quantity, CancellationToken ct)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductId == productId, ct);
        if (item is null) throw new ProductNotFoundException(productId);
        if (item.AvailableQuantity < quantity)
            throw new InsufficientInventoryException(productId, quantity, item.AvailableQuantity);
        item.AvailableQuantity -= quantity;
        item.ReservedQuantity += quantity;
        await _db.SaveChangesAsync(ct);
        return Map(item);
    }

    public async Task<InventoryResponse> ReleaseAsync(string productId, int quantity, CancellationToken ct)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductId == productId, ct);
        if (item is null) throw new ProductNotFoundException(productId);
        item.ReservedQuantity = Math.Max(0, item.ReservedQuantity - quantity);
        item.AvailableQuantity += quantity;
        await _db.SaveChangesAsync(ct);
        return Map(item);
    }

    private static InventoryResponse Map(InventoryItem i) => new(i.ProductId, i.AvailableQuantity, i.ReservedQuantity);
}