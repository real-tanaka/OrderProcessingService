using Microsoft.AspNetCore.Mvc;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    public OrdersController(IOrderService orderService) => _orderService = orderService;

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await _orderService.CreateOrderAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken ct)
    {
        var order = await _orderService.GetOrderAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet]
    public async Task<ActionResult<OrderListResponse>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var orders = await _orderService.GetOrdersAsync(page, pageSize, ct);
        return Ok(orders);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var order = await _orderService.UpdateOrderStatusAsync(id, request.Status, ct);
        return order is null ? NotFound() : Ok(order);
    }
}