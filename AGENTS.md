# AGENTS.md

Guidance for AI coding agents working in this repository.

## Commands

.NET 10 solution (`OrderProcessingService.slnx`): one API project in `src/`, one xUnit project in `tests/`.

- Run: `dotnet run --project src/OrderProcessingService.Api` — listens on `http://localhost:5086`; Swagger at `/swagger` (Development only).
- Test all: `dotnet test`
- Single test: `dotnet test --filter "FullyQualifiedName~OrderServiceTests.<TestName>"`
- Manual requests: `src/OrderProcessingService.Api/OrderProcessingService.Api.http`

No linter or formatter is configured.

## Architecture

ASP.NET Core controllers → services → EF Core `OrderDbContext` (in-memory provider, seeded by `DbInitializer` at startup; `PROD-004` intentionally has zero stock).

**Order creation is a saga** (`Services/OrderService.CreateOrderAsync`): reserve stock per line → charge payment → mark `Confirmed`. On any failure it compensates by releasing *only* the lines actually reserved (tracked in the local `reserved` list, never the full order), then saves the order as `Cancelled` and rethrows. Preserve that exact-compensation behavior when changing the flow.

**The payment service is the same app, called over HTTP.** `PaymentApiClient` (typed `HttpClient`, base URL from `Payment:BaseUrl` in `appsettings.json`, which must match the launch-settings port) posts to this API's own `/api/payments/process`. Consequences:
- Client and server JSON must agree: enums are camelCase strings (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` is configured both in `Program.cs` and in `PaymentApiClient`). Change one, change both.
- Non-2xx from the payment endpoint → `PaymentServiceUnavailableException`; a 2xx with a non-`Completed` status → `PaymentProcessingException`. The mock payment service declines amounts over 5000.

**Error mapping is centralized** in `Middleware/ApiExceptionHandler` (`IExceptionHandler` + ProblemDetails): `ProductNotFoundException`→404, `InsufficientInventoryException` and `PaymentProcessingException`→409, `PaymentServiceUnavailableException`→503, `ArgumentException`→400. Services throw domain exceptions (`Services/Exceptions.cs`); controllers generally don't catch. 409 (payment declined) vs 503 (payment unreachable) is a deliberate distinction.

Known simplifications (see README): no concurrency control on inventory (overselling possible), unit price is client-supplied, total is computed server-side.

## Tests

Tests construct services directly over a per-test in-memory `OrderDbContext` (`Guid` database name) and use hand-written fakes (e.g. `FakePaymentApiClient` in `OrderServiceTests`) rather than a mocking library or `WebApplicationFactory`.

Note: the DbContext file is named `Data/OderDbContext.cs` (typo) but the class is `OrderDbContext`.
