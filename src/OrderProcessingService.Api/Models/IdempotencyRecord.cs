namespace OrderProcessingService.Api.Models;

public enum IdempotencyState { InProgress, Completed }

/// <summary>
/// One row per (CustomerId, Key). The composite primary key is the lock: whoever inserts first runs the saga.
/// </summary>
public class IdempotencyRecord
{
    public string CustomerId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public IdempotencyState State { get; set; } = IdempotencyState.InProgress;
    public Guid OrderId { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? FailureKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
