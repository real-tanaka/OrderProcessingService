using Microsoft.AspNetCore.Mvc;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    public InventoryController(IInventoryService inventoryService) => _inventoryService = inventoryService;

    [HttpGet("{productId}")]
    public async Task<ActionResult<InventoryResponse>> Get(string productId, CancellationToken ct)
    {
        var inventory = await _inventoryService.GetInventoryAsync(productId, ct);
        return inventory is null ? NotFound() : Ok(inventory);
    }

    [HttpPost("{productId}/reserve")]
    public async Task<ActionResult<InventoryResponse>> Reserve(string productId, ReserveInventoryRequest request, CancellationToken ct)
    {
        var inventory = await _inventoryService.ReserveAsync(productId, request.Quantity, ct);
        return Ok(inventory);
    }

    [HttpPost("{productId}/release")]
    public async Task<ActionResult<InventoryResponse>> Release(string productId, ReleaseInventoryRequest request, CancellationToken ct)
    {
        var inventory = await _inventoryService.ReleaseAsync(productId, request.Quantity, ct);
        return Ok(inventory);
    }
}