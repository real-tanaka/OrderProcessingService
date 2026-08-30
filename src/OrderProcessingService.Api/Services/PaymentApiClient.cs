using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderProcessingService.Api.DTOs;

namespace OrderProcessingService.Api.Services;

public interface IPaymentApiClient
{
    Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct);
}

public sealed class PaymentApiClient : IPaymentApiClient
{
    private readonly HttpClient _http;

    // CRITICAL: the client MUST agree with the server's wire format.
    // The server emits enums as camelCase strings; the client must parse them the same way.
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public PaymentApiClient(HttpClient http) => _http = http;

    public async Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/payments/process", request, _json, ct);
        if (!response.IsSuccessStatusCode)
            throw new PaymentServiceUnavailableException($"Payment service returned {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>(_json, ct))!;
    }
}