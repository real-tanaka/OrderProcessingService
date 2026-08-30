# Order Processing Service

A small ASP.NET Core API that creates orders by checking stock, taking payment, then confirming in that order. The interesting part is what happens when any of those steps goes wrong.

Built with .NET 10, EF Core (in-memory), and xUnit.

## Running it

dotnet run --project src/OrderProcessingService.Api

It starts on `http://localhost:5086` (matching the launch settings, and the payment base URL). Swagger is at `/swagger`. Five products get seeded into the in-memory inventory on startup; `PROD-004` has zero stock on purpose, so you can trigger the "not enough stock" path without touching the data.

## How an order gets created

Order creation is a saga because it touches three things (inventory, payment, the order itself) that a single database transaction won't cover.

1. Build the order, status `pending`.
2. Reserve stock for each line. If one line doesn't have enough, everything reserved so far gets put back, and the order is marked `cancelled`.
3. Charge the payment. This goes over HTTP to the payment endpoint, so the two are separate service boundaries.
4. If the card goes through, the order becomes `confirmed`. If it fails, the reserved stock is released and the order is `cancelled`. If the payment service can't be reached, same thing — but that comes back as a `503`, not a `409`.

I track what actually got reserved rather than just "reserve it all" because of the case where the third line fails after the first two succeeded — you release only the two you actually took. Releasing too much hands back stock that was never reserved for anyone.

## Endpoints

| Method | Route | What it does |
|---|---|---|
| POST | `/api/orders` | create an order (runs the saga) |
| GET | `/api/orders/{id}` | get a single order |
| GET | `/api/orders?page=&pageSize=` | list orders, paged |
| PUT | `/api/orders/{id}/status` | change the status |
| GET | `/api/inventory/{productId}` | check availability |
| POST | `/api/inventory/{productId}/reserve` | reserve stock |
| POST | `/api/inventory/{productId}/release` | release stock |
| POST | `/api/payments/process` | charge a payment |
| GET | `/api/payments/{transactionId}` | payment status |

Errors come back as `400` for bad input, `404` for a missing product, `409` for something conflicting with the current state (no stock, or a payment that processed but failed), `503` if the payment service is unreachable, and `201` + a `Location` header on create.

I keep `409` and `503` separate because a payment that was attempted and declined is a different problem from a payment service that just can't be reached — one is on the customer, the other is on infrastructure.

## Tests

dotnet test



## What I'd change for production

- The in-memory provider has no real locking, so two orders could oversell the same product at the same time. In a real service I'd put a concurrency token on the inventory row. I left it out here to keep the demo on one concern.  
- Payment is mocked — any amount over 5000 "declines." Real integration would call a PSP.  
- The total is recomputed server-side, but the unit price still comes from the client, because this model has no product catalog to look a price up from.
