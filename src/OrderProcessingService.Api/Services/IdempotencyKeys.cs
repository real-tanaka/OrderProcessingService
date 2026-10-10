using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderProcessingService.Api.DTOs;

namespace OrderProcessingService.Api.Services;

public static class IdempotencyKeys
{
    public const string HeaderName = "Idempotency-Key";
    public const int MaxLength = 200;

    /// <summary>Throws <see cref="ArgumentException"/> (mapped to 400) for a missing, blank or over-long key.</summary>
    public static void Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"{HeaderName} header is required.");
        if (key.Length > MaxLength)
            throw new ArgumentException($"{HeaderName} must be at most {MaxLength} characters.");
    }

    /// <summary>SHA-256 (hex) over customer + items sorted by product, so item order and decimal scale don't matter.</summary>
    public static string Fingerprint(CreateOrderRequest request)
    {
        var items = request.Items
            .OrderBy(i => i.ProductId, StringComparer.Ordinal)
            .ThenBy(i => i.Quantity)
            .ThenBy(i => i.UnitPrice)
            .Select(i => new object[] { i.ProductId, i.Quantity, i.UnitPrice.ToString("0.############################", CultureInfo.InvariantCulture) });
        var canonical = JsonSerializer.Serialize(new object[] { request.CustomerId, items });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
