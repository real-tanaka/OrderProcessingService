using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CustomerId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasMany(e => e.Items)
                  .WithOne()
                  .HasForeignKey("OrderId")
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(e => e.ProductId);
            entity.Property(e => e.ProductId).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId);
            entity.Property(e => e.OrderId).HasMaxLength(50);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}