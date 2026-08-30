using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Middleware;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ProductNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            InsufficientInventoryException => (StatusCodes.Status409Conflict, "Conflict"),
            PaymentProcessingException => (StatusCodes.Status409Conflict, "Conflict"),
            PaymentServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            _ => (0, (string?)null)
        };

        if (statusCode == 0) return false;

        logger.LogWarning(exception, "Handled API exception: {Title}", title);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode, Title = title, Detail = title, Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}