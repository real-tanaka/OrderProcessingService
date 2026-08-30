using System.ComponentModel.DataAnnotations;
using OrderProcessingService.Api.Models;

namespace OrderProcessingService.Api.DTOs;

/// <summary>Payload for creating a new order.</summary>
public sealed record CreateOrderRequest
{
    [Required, MaxLength(100)]
    public required string CustomerId { get; init; }

    [Required, MinLength(1)]
    public required List<CreateOrderItemRequest> Items { get; init; }
}

/// <summary>A single product line within a create-order request.</summary>
public sealed record CreateOrderItemRequest
{
    [Required, MaxLength(50)]
    public required string ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public required int Quantity { get; init; }

    [Range(0.01, 999999.99)]
    public required decimal UnitPrice { get; init; }
}

/// <summary>Payload for updating an order's status.</summary>
public sealed record UpdateOrderStatusRequest
{
    public required OrderStatus Status { get; init; }
}

/// <summary>A product line as returned by the API, with its computed line total.</summary>
public sealed record OrderItemResponse(string ProductId, int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>An order as returned by the API.</summary>
public sealed record OrderResponse(
    Guid Id, string CustomerId, IReadOnlyList<OrderItemResponse> Items,
    decimal TotalAmount, OrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A paginated set of orders.</summary>
public sealed record OrderListResponse(IReadOnlyList<OrderResponse> Items, int TotalCount, int Page, int PageSize);