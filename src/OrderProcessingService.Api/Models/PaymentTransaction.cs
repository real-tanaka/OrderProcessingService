namespace OrderProcessingService.Api.Models;

public class PaymentTransaction
{
    public Guid TransactionId { get; set; } = Guid.NewGuid();
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTimeOffset? ProcessedAt { get; set; }
}