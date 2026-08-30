using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly OrderDbContext _db;
    public PaymentService(OrderDbContext db) => _db = db;

    public async Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct)
    {
        var existing = await _db.PaymentTransactions.FirstOrDefaultAsync(p => p.OrderId == request.OrderId, ct);
        if (existing is not null) return Map(existing);

        var txn = new PaymentTransaction
        {
            OrderId = request.OrderId,
            Amount = request.Amount,
            Status = PaymentStatus.Pending,
        };
        _db.PaymentTransactions.Add(txn);
        txn.Status = request.Amount > 5000 ? PaymentStatus.Failed : PaymentStatus.Completed;
        txn.ProcessedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(txn);
    }

    public async Task<PaymentResponse?> GetPaymentAsync(Guid transactionId, CancellationToken ct)
    {
        var txn = await _db.PaymentTransactions.AsNoTracking().FirstOrDefaultAsync(p => p.TransactionId == transactionId, ct);
        return txn is null ? null : Map(txn);
    }

    private static PaymentResponse Map(PaymentTransaction t) => new(t.TransactionId, t.OrderId, t.Amount, t.Status, t.ProcessedAt);
}