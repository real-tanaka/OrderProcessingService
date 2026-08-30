using System.ComponentModel.DataAnnotations;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.DTOs;

public sealed record ProcessPaymentRequest
{
    [MaxLength(50)]
    public required string OrderId { get; init; }

    [Range(0.01, 999999.99)]
    public required decimal Amount { get; init; }
}

public sealed record PaymentResponse(Guid TransactionId, string OrderId, decimal Amount, PaymentStatus Status, DateTimeOffset? ProcessedAt);