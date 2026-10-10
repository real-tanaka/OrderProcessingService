using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.Services;

public interface IOrderService
{
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken ct);
    Task<OrderResponse?> GetOrderAsync(Guid id, CancellationToken ct);
    Task<OrderListResponse> GetOrdersAsync(int page, int pageSize, CancellationToken ct);
    Task<OrderResponse?> UpdateOrderStatusAsync(Guid id, OrderStatus status, CancellationToken ct);
}
