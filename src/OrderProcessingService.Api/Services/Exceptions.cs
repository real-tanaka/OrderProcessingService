namespace OrderProcessingService.Api.Services;

public sealed class ProductNotFoundException : Exception
{
    public string ProductId { get; }
    public ProductNotFoundException(string productId) : base($"Product '{productId}' was not found.") => ProductId = productId;
}

public sealed class InsufficientInventoryException : Exception
{
    public string ProductId { get; } public int Requested { get; } public int Available { get; }
    public InsufficientInventoryException(string productId, int requested, int available)
        : base($"Insufficient inventory for '{productId}': requested {requested}, available {available}.")
    { ProductId = productId; Requested = requested; Available = available; }
}

public sealed class PaymentProcessingException : Exception
{
    public PaymentProcessingException(string message) : base(message) { }
}

public sealed class IdempotencyKeyReuseException : Exception
{
    public IdempotencyKeyReuseException() : base("Idempotency-Key was already used with a different request body.") { }
}

public sealed class RequestInProgressException : Exception
{
    public RequestInProgressException() : base("A request with this Idempotency-Key is still in progress.") { }
}

public sealed class PaymentServiceUnavailableException : Exception
{
    public PaymentServiceUnavailableException(string message) : base(message) { }
}