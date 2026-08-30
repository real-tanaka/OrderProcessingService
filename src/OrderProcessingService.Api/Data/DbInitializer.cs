using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Data;

public static class DbInitializer
{
    public static void Initialize(OrderDbContext db)
    {
        db.Database.EnsureCreated();
        if (db.InventoryItems.Any()) return;

        db.InventoryItems.AddRange(
            new InventoryItem { ProductId = "PROD-001", AvailableQuantity = 100 },
            new InventoryItem { ProductId = "PROD-002", AvailableQuantity = 50 },
            new InventoryItem { ProductId = "PROD-003", AvailableQuantity = 25 },
            new InventoryItem { ProductId = "PROD-004", AvailableQuantity = 0 },  
            new InventoryItem { ProductId = "PROD-005", AvailableQuantity = 75 });

        db.SaveChanges();
    }
}