using OrderProcessingService.Api.DTOs;

namespace OrderProcessingService.Api.Services;
public interface IInventoryService
{
    Task<InventoryResponse?> GetInventoryAsync(string productId, CancellationToken ct);
    Task<InventoryResponse> ReserveAsync(string productId, int quantity, CancellationToken ct);
    Task<InventoryResponse> ReleaseAsync(string productId, int quantity, CancellationToken ct);
}
