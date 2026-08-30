using OrderProcessingService.Api.DTOs;

namespace OrderProcessingService.Api.Services;

public interface IPaymentService
{
    Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct);
    Task<PaymentResponse?> GetPaymentAsync(Guid transactionId, CancellationToken ct);
}