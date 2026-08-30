namespace OrderProcessingService.Api.Models;

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}