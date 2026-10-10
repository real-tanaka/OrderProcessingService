# PROGRESS.md

Running log of features and planned work. Update it as part of each change: move items between sections and add a log entry. Architecture and conventions live in `AGENTS.md`.

## Implemented

- Order creation saga (reserve stock → charge payment → confirm) with exact compensation — `Services/OrderService.cs`
- Orders API: create, get by id, paged list, update status — `Controllers/OrdersController.cs`
- Inventory API: get, reserve, release; seeded in-memory data (`PROD-004` has zero stock) — `Controllers/InventoryController.cs`, `Data/DbInitializer.cs`
- Mock payment service (declines amounts > 5000, idempotent per `OrderId`) called over HTTP via `PaymentApiClient`
- Centralized error mapping to ProblemDetails (400/404/409/503) — `Middleware/ApiExceptionHandler.cs`
- Saga compensates on any exception (no leaked stock), including request cancellation
- Unit tests: `InventoryServiceTests`, `OrderServiceTests` (happy path, payment declined, partial reserve, insufficient stock, payment unreachable), `PaymentApiClientTests`

## In progress

_Nothing yet._

## Backlog

Candidates, roughly by value (from known gaps; confirm priorities with the user):

- [ ] Order status transition rules (e.g. only Confirmed → Shipped); cancelling a confirmed order should release stock
- [ ] Cap `ReleaseAsync` at the reserved quantity
- [ ] Concurrency control on inventory (concurrency token / real database) to prevent overselling
- [ ] Product catalog so unit price is not client-supplied
- [ ] Refund/void when persisting the order fails after a successful charge
- [ ] Integration tests with `WebApplicationFactory` (controllers, status codes, ProblemDetails)
- [ ] Real persistence (replace the in-memory provider) and migrations
- [ ] Rename `Data/OderDbContext.cs` → `OrderDbContext.cs`

## Log

- 2026-10-10 — Added `AGENTS.md`/`CLAUDE.md` and this file after a codebase review. No code changes. .NET SDK was unavailable in the session, so tests were not run.
- 2026-10-10 — Fixed leaked stock: `OrderService.CreateOrderAsync` now compensates on any exception via `CancelAsync`; added tests for unknown product on a later line and an unexpected payment exception. Not run locally (no .NET SDK in the session; install is blocked by the proxy) — verify with `dotnet test`.
