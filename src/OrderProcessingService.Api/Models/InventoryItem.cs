namespace OrderProcessingService.Api.Models;

public class InventoryItem
{
    public string ProductId { get; set; } = string.Empty;
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
}