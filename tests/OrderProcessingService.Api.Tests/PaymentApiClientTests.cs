using System.Net;
using OrderProcessingService.Api.DTOs;
using OrderProcessingService.Api.Models;
using OrderProcessingService.Api.Services;

namespace OrderProcessingService.Api.Tests;

public class PaymentApiClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_respond(request));
    }

    private static PaymentApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => new(new HttpClient(new FakeHandler(respond)) { BaseAddress = new Uri("http://localhost/") });

    [Fact]
    public async Task ParsesEnumSerializedAsString_WhenResponseIsOk()
    {
        var body = "{\"transactionId\":\"11111111-1111-1111-1111-111111111111\",\"orderId\":\"order-1\",\"amount\":60.0,\"status\":\"completed\",\"processedAt\":\"2026-01-01T00:00:00Z\"}";
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });

        var result = await client.ProcessPaymentAsync(new ProcessPaymentRequest { OrderId = "order-1", Amount = 60 }, CancellationToken.None);

        Assert.Equal(PaymentStatus.Completed, result.Status);
        Assert.Equal("order-1", result.OrderId);
    }

    [Fact]
    public async Task NonSuccessResponse_ThrowsUnavailable()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<PaymentServiceUnavailableException>(
            () => client.ProcessPaymentAsync(new ProcessPaymentRequest { OrderId = "order-1", Amount = 60 }, CancellationToken.None));
    }
}