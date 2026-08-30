namespace OrderProcessingService.Api.Models;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = string.Empty;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}