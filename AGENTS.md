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

**Order creation is a saga** (`Services/OrderService.CreateOrderAsync`): reserve stock per line → charge payment → mark `Confirmed`. On any failure it compensates by releasing *only* the lines actually reserved (tracked in the local `reserved` list, never the full order), then saves the order as `Cancelled` and rethrows. This happens for *any* exception during reserve/payment (not just the expected ones), and compensation uses `CancellationToken.None` so a cancelled request still releases stock. Preserve that exact-compensation behavior when changing the flow.

**The payment service is the same app, called over HTTP.** `PaymentApiClient` (typed `HttpClient`, base URL from `Payment:BaseUrl` in `appsettings.json`, which must match the launch-settings port) posts to this API's own `/api/payments/process`. Consequences:
- Client and server JSON must agree: enums are camelCase strings (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` is configured both in `Program.cs` and in `PaymentApiClient`). Change one, change both.
- Non-2xx from the payment endpoint → `PaymentServiceUnavailableException`; a 2xx with a non-`Completed` status → `PaymentProcessingException`. The mock payment service declines amounts over 5000.

**Error mapping is centralized** in `Middleware/ApiExceptionHandler` (`IExceptionHandler` + ProblemDetails): `ProductNotFoundException`→404, `InsufficientInventoryException` and `PaymentProcessingException`→409, `PaymentServiceUnavailableException`→503, `ArgumentException`→400. Services throw domain exceptions (`Services/Exceptions.cs`); controllers generally don't catch. 409 (payment declined) vs 503 (payment unreachable) is a deliberate distinction.

## Details worth knowing before building on this

- **Domain model:** `Order` (1..n `OrderItem`, FK shadow property `OrderId`), `InventoryItem` (key `ProductId`; `AvailableQuantity` + `ReservedQuantity`), `PaymentTransaction`. Enums (`OrderStatus`: Pending/Confirmed/Cancelled/Shipped, `PaymentStatus`: Pending/Completed/Failed) are stored as strings and serialized camelCase.
- **Order persistence:** the order is only added to the DbContext and saved at the end of the saga (confirmed or cancelled); nothing is persisted mid-flow. `Order.Id` is generated at construction and sent to the payment service as `OrderId`.
- **Payment idempotency:** `PaymentService.ProcessPaymentAsync` returns the existing transaction if one exists for the same `OrderId`.
- **Validation** is DataAnnotations on DTOs in `DTOs/` (`[ApiController]` returns 400 automatically). `ApiExceptionHandler` returns generic ProblemDetails (title only, not the exception message).
- **Conventions:** services are registered scoped in `Program.cs` behind `I*Service` interfaces; every async method takes a `CancellationToken`; DTOs are records.
- **Known gaps in the saga/services** (don't assume they are handled): `UpdateOrderStatusAsync` accepts any status with no transition rules and does not release stock; `InventoryService.ReleaseAsync` does not cap quantity at what was reserved; a failure saving after a successful charge is not refunded.
- **Environment:** the .NET SDK was not installed in the cloud session where this was written, so the build/tests had not been run there; check `dotnet --version` before relying on them.

## Working agreements

- `PROGRESS.md` tracks features and the backlog. Update it when you start, finish, or discover work (move items between sections, add a dated entry to the log).
- Keep this file current when architecture or conventions change.

Known simplifications (see README): no concurrency control on inventory (overselling possible), unit price is client-supplied, total is computed server-side.

## Tests

Tests construct services directly over a per-test in-memory `OrderDbContext` (`Guid` database name) and use hand-written fakes (e.g. `FakePaymentApiClient` in `OrderServiceTests`) rather than a mocking library or `WebApplicationFactory`.

Note: the DbContext file is named `Data/OderDbContext.cs` (typo) but the class is `OrderDbContext`.
