using System.ComponentModel.DataAnnotations;

namespace OrderProcessingService.Api.DTOs;

public sealed record ReserveInventoryRequest
{
    [Range(1, int.MaxValue)]
    public required int Quantity { get; init; }
}

public sealed record ReleaseInventoryRequest
{
    [Range(1, int.MaxValue)]
    public required int Quantity { get; init; }
}

public sealed record InventoryResponse(string ProductId, int AvailableQuantity, int ReservedQuantity);