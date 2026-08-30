using Microsoft.AspNetCore.Mvc;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    public PaymentsController(IPaymentService paymentService) => _paymentService = paymentService;

    [HttpPost("process")]
    public async Task<ActionResult<PaymentResponse>> Process(ProcessPaymentRequest request, CancellationToken ct)
    {
        var payment = await _paymentService.ProcessPaymentAsync(request, ct);
        return Ok(payment);
    }

    [HttpGet("{transactionId:guid}")]
    public async Task<ActionResult<PaymentResponse>> GetById(Guid transactionId, CancellationToken ct)
    {
        var payment = await _paymentService.GetPaymentAsync(transactionId, ct);
        return payment is null ? NotFound() : Ok(payment);
    }
}