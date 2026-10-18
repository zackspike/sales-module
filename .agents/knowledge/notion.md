# Project Knowledge & Technical Implementation Plan (MVP-02, EventSeat model)

## Source of Truth & Synchronization
* **Online Source (Notion):** [sap-atitos](https://app.notion.com/p/sap-atitos-3de7bd21751a8053a5f9e961829231cb?source=copy_link)
* **Page ID:** `3de7bd21-751a-8053-a5f9-e961829231cb`
* **Tasks Database ID:** `3de7bd21-751a-8013-9ca7-d994b2527813` (`collection://3de7bd21-751a-8014-be08-000b8740852b`)
* **MCP Integration:** Live synchronization configured in `.agents/plugins/notion/mcp_config.json`.
* **Offline-First:** This file acts as the local cached source of truth. All agents must follow these specifications directly without requiring active internet access or external API tokens.
* **Code as reference:** The business model and use cases described here mirror `BookingService.Domain` and `BookingService.Application`. If this file and those projects disagree, the code wins and this file must be updated.

---

## 1. Project Overview & Scope (MVP-02)

**Goal:** Event Search and Purchase (v1.0)
* An unauthenticated fan searches for an event by name, views event details, picks an available seat, reserves it and completes the purchase of that seat.
* **Out of scope:** Authentication/Login, real payment gateway, external third-party systems, sections, and pricing calculations.
* **Seat choice:** The fan chooses the seat from the event's available seats; the backend never assigns one.

### Participating Services
* **Error200 (Front):** Fan-facing UI (search input, event details, purchase form, ticket display).
* **Error200 (SearchService):** Event search backend (`GET /events?name={name}`, `GET /events/{id}`).
* **Sap-atitos (BookingService):** Seat reservation and ticket purchase backend (**this repository**).
* **EventService / VenueService:** Owners of the event data (`Event`) and of the physical venue seats (`VenueSeat`). BookingService only reads them through `IEventCatalog` and `IVenueSeatCatalog`.

---

## 2. Technical Stack & Architectural Directives

* **Framework:** .NET 10 / C# Minimal APIs.
* **Architecture:** Domain-Driven Design (DDD) & Clean Architecture (see `AGENTS.md` for dependency rules):
  * `BookingService.Domain`: Entities `EventSeat` and `Ticket`, shared models `Event`, `VenueSeat`, `SeatZone`, factory `EventInventoryFactory`, policy `EventSeatReservationPolicy`, `TicketCodeGenerator`, domain exceptions (`DomainException`, `EventSeatAlreadySoldException`).
  * `BookingService.Application`: Use cases in `EventSeats/` (commands `ReserveEventSeat*`, `PurchaseEventSeat*`; queries `GetAvailableEventSeats*`, `GetEventSeatAvailability*`), validation (`TicketHolderValidator`), read models (`AvailableEventSeat`, `EventSeatAvailability`, `ReservedEventSeat`, `IssuedTicket`) and abstractions (`IEventSeatRepository`, `IEventCatalog`, `IVenueSeatCatalog`, `IEventSeatLockStore`, `IIdempotencyStore`, `IAvailableEventSeatsCache`).
  * `BookingService.Infrastructure`: Implementations of the Application abstractions (in-memory and PostgreSQL repositories, in-memory catalogs, Redis or in-memory seat locks, Redis idempotency store and available-seats cache).
  * `BookingService.Api`: Minimal API endpoints, middleware (`GlobalExceptionMiddleware`), dependency injection composition (`Program.cs`).
* **Documentation & Testing:**
  * Minimal APIs with OpenAPI / Swagger UI support (`/swagger`).
  * Integration testing via `.http` file (`BookingService.Api.http`).
  * Unit tests with xUnit, one project per layer: `BookingService.Domain.Tests`, `BookingService.Application.Tests`, `BookingService.Infrastructure.Tests`.

---

## 3. Domain Model (`BookingService.Domain`)

### `EventSeat` (aggregate, `Domain/EventSeats`)
The sellable inventory unit: one physical venue seat for one event.
* `Id`, `EventId`, `VenueSeatId` (physical seat, owned by VenueService), `ZoneId` (zone of the seat for the event, owned by VenueService), `Status` (`EventSeatStatus`).
* Built only through `EventSeat.CreateAvailable(eventId, venueSeatId, zoneId)`; starts as `Available`.
* `Sell(holderName, holderEmail, soldAtUtc)` marks it `Sold` and issues its `Ticket`. **A seat can only be sold once:** selling a `Sold` seat throws `EventSeatAlreadySoldException` (`EVENT_SEAT_ALREADY_SOLD`).

### `EventSeatStatus`
* `Available` — not sold yet. `Sold` — sold and its ticket issued.
* Temporary reservations (seat locks) are **not** part of this status; they live in `IEventSeatLockStore`.

### `Ticket` (`Domain/Tickets`)
The ticket issued when an event seat is sold. It only exists for sold seats.
* `Id`, `TicketCode`, `EventSeatId`, `HolderName`, `HolderEmail`, `SoldAtUtc`.
* Created only by `EventSeat.Sell` (internal `Ticket.Issue`).
* Limits: `MaxHolderFullNameLength = 64`, `MaxHolderEmailLength = 254`.

### `TicketCodeGenerator`
* Format `"TK-{GUID:N}"` (e.g., `TK-4f32a76fbf424b9195980da672807f87`).

### `EventInventoryFactory`
* `GenerateEventSeats(eventId, venueSeats)` creates one available `EventSeat` per `VenueSeat` (copying its `ZoneId`).

### `EventSeatReservationPolicy`
* `Duration = 10 minutes`: how long a seat stays reserved for the fan who reserved it.

### Shared models (contracts agreed with other services)
* `Event` (SP-03 / ALIGN-01, `Domain/Events`): `Id`, `Name`, `ArtistId`, `ArtistName`, `VenueId`, `VenueName`, `EventDateTime`, `EventSalesStartDateTime`, `EventSalesEndDateTime`.
  * The sales window (`EventSalesStartDateTime` / `EventSalesEndDateTime`) is **not enforced by any use case yet**.
* `VenueSeat` (ALIGN-02, `Domain/Venues`): `Id`, `ZoneId`, `SeatNumber` (e.g., `"A-1"`).
* `SeatZone` (`Domain/EventSeats`): `Id`, `EventId`, `Price`. Not used by any use case (pricing is out of scope).

---

## 4. Use Cases (`BookingService.Application`)

Every use case returns a result with a status; the HTTP code listed for each status is the one the Api must map it to.

### Common rules
* **Holder validation (`TicketHolderValidator`, SP-05 / VAL-02):** used by reserve and purchase.
  * `fullName`: required; at most 64 characters after trimming.
  * `email`: required; valid address (display-name forms like `Jane <jane@example.com>` are rejected); at most 254 characters after trimming.
  * Errors are returned as a dictionary keyed by JSON field name (`fullName`, `email`).
* **Buyer identity in seat locks (`EventSeatLockHolder`):** the email, trimmed and lower-cased, so reserve and purchase produce the same identifier.
* **Seat number:** always resolved from `IVenueSeatCatalog` through `EventSeat.VenueSeatId`. A seat whose venue seat is missing from the catalog is an inconsistency (`InvalidOperationException`) and is never reserved or sold.
* **Event existence:** checked with `IEventCatalog.Exists` (404 if unknown).

### SP-04 / APP-01 — List available event seats (`GetAvailableEventSeatsHandler`)
* Input: `GetAvailableEventSeatsQuery(EventId)`.
* Output: `AvailableEventSeat(EventSeatId, SeatNumber)` list sorted by seat number, or `null` if the event does not exist (404).
* Seats `Available` in the repository, served from `IAvailableEventSeatsCache` when possible (a cache hit implies the event exists); seats currently reserved by a fan (seat lock) are filtered out at read time.

### SP-04 / APP-02 — Event seat availability (`GetEventSeatAvailabilityHandler`)
* Input: `GetEventSeatAvailabilityQuery(EventId, EventSeatId)`.
* Output: `EventSeatAvailability(EventSeatId, SeatNumber, Status)`, or `null` if the event or the seat does not exist (404).
* `Status`: `Sold` (stored status), `Reserved` (available but locked by a fan) or `Available`.

### Reserve an event seat (`ReserveEventSeatHandler`)
First step of the purchase: the fan selects a seat and clicks "comprar".
* Input: `ReserveEventSeatCommand(EventId, EventSeatId, FullName, Email)`.
* Locks the seat for the buyer during `EventSeatReservationPolicy.Duration`. If the same buyer already holds it, the lock is renewed. After locking, the seat is re-read so a seat sold in between is never left reserved.
* Output: `ReservedEventSeat(EventSeatId, EventId, SeatNumber, ExpiresAtUtc)`.

| Status | HTTP |
| :--- | :---: |
| `Reserved` (new or renewed) | 200 |
| `Invalid` | 400 |
| `EventNotFound`, `EventSeatNotFound` | 404 |
| `AlreadySold`, `ReservedByAnotherBuyer` | 409 |

### SP-05 — Purchase an event seat (`PurchaseEventSeatHandler`)
* Input: `PurchaseEventSeatCommand(EventId, EventSeatId, FullName, Email, IdempotencyKey)`.
* Flow:
  1. Validate holder data (400) and require a non-empty idempotency key (400).
  2. Check the event exists (404).
  3. If the key is in `IIdempotencyStore`, replay that purchase (or 409 if it was for another seat).
  4. Load the seat (404). If it is already sold, replay it when this key bought it, otherwise 409.
  5. Require the buyer to hold the seat lock taken by the reserve step (403).
  6. Sell atomically and idempotently through `IEventSeatRepository.SellOnce` (the "sold only once" rule is `EventSeat.Sell`).
  7. After the sale is committed: remember the key in `IIdempotencyStore` for `IdempotencyKeyRetention` (10 minutes), release the seat lock and invalidate the available-seats cache. Failures here are only logged.
* Output: `IssuedTicket(EventSeatId, EventId, SeatNumber, HolderName, HolderEmail, TicketCode, SoldAtUtc)`.

| Status | HTTP |
| :--- | :---: |
| `Purchased` | 201 |
| `Replayed` | 200 |
| `Invalid` | 400 |
| `ReservationRequired` | 403 |
| `EventNotFound`, `EventSeatNotFound` | 404 |
| `AlreadySold`, `IdempotencyKeyConflict` | 409 |

### SP-06 — Purchase idempotency
* **Key:** `IdempotencyKey` (`Guid`, non-empty), sent by the client as the `X-Idempotency-Key` header.
* Exactly one seat is purchased per key. Replaying an already-processed key returns the previously issued ticket (`Replayed`) without issuing a second one; using it for a different seat is `IdempotencyKeyConflict`.
* `IIdempotencyStore` is a fast, expiring lookup; the durable guarantee is `IEventSeatRepository`, which keeps the key of every sale (`GetSaleByIdempotencyKey` returns the sale only if that key bought that seat).

### SP-07 & SP-08 — Unique ticket codes
* The purchase result returns the generated `TicketCode` (SP-07).
* `TicketCodeGenerator` uses a random GUID (122 bits of entropy), so no two tickets share a code across all events, including concurrent purchases by the same person for the same event (SP-08).

---

## 5. Contracts Required from Infrastructure (Application abstractions)

* **`IEventSeatRepository`:** `GetByEvent`, `GetById(eventId, eventSeatId)` (only if the seat belongs to the event), `GetSaleByIdempotencyKey(key, eventId, eventSeatId)`, and `SellOnce(key, eventId, eventSeatId, sell)`. `SellOnce` performs key lookup, seat lookup, sale and storage of the seat, its ticket and the key **atomically**; if the key was already used it returns that sale with `Replayed = true` (even for another seat); if `sell` throws `EventSeatAlreadySoldException`, nothing is stored and the exception propagates.
* **`IEventCatalog`:** `Exists(eventId)`. Read-only data from EventService.
* **`IVenueSeatCatalog`:** `GetById`, `GetByIds`. Read-only data from VenueService; missing seats are omitted.
* **`IEventSeatLockStore`:** distributed, expiring, atomic locks `eventSeatId → holderIdentifier` (`TryAcquire` → `Acquired` / `Renewed` / `HeldByAnother`, `GetHolderIdentifier`, `GetLockedEventSeatIds`, `Release` only by the holder).
* **`IIdempotencyStore`:** `GetIdempotencyRecord(key)` and `Remember(key, IdempotencyRecord(EventId, EventSeatId), retention)`; called only after the sale is committed.
* **`IAvailableEventSeatsCache`:** `Get`, `Set` (only for existing events), `Invalidate` (after a sale). Short-lived; reservations and purchases always re-check the repository.

---

## 6. Cross-Cutting — Global Exception Handling & Setup

* **Middleware:** Centralized global exception handler in `BookingService.Api.Middleware.GlobalExceptionMiddleware`.
* **Standard Error Responses:** Clean, consistent JSON error payloads (`statusCode`, `message`, `details`, `traceId`, `timestampUtc`).
  * `BadHttpRequestException` -> `400 Bad Request` (e.g., a malformed `X-Idempotency-Key` GUID).
  * `KeyNotFoundException` -> `404 Not Found`
  * `ArgumentException` / `InvalidOperationException` -> `400 Bad Request`
  * Unhandled exceptions -> `500 Internal Server Error`
* **CORS:** Enabled for local frontend development (`AllowAnyOrigin`, `AllowAnyHeader`, `AllowAnyMethod`).

---

## 7. Migration Status (Ticket → EventSeat refactor)

The refactor advances one layer at a time: Domain → Application → Infrastructure → Api.
* **Domain and Application:** migrated to the EventSeat model (this document).
* **Infrastructure:** being migrated (PR #33).
* **Api:** not migrated yet. The current HTTP routes keep the MVP-02 names, where the route's `ticketId` is the event seat id:
  * `GET /events/{eventId}/tickets` — available seats.
  * `GET /events/{eventId}/tickets/{ticketId}/availability` — seat availability.
  * `POST /events/{eventId}/tickets/{ticketId}/reserve` — reserve.
  * `POST /events/{eventId}/tickets/{ticketId}/purchase` — purchase (`X-Idempotency-Key` header).
  * The HTTP request/response shape (e.g., MVP-02 `purchasedAtUtc` vs. `IssuedTicket.SoldAtUtc`) is defined when the Api is migrated.

Task statuses are tracked in the Notion tasks database, not in this file.

---

## 8. Precedence Rules

1. **Explicit user instructions** in conversation take highest precedence.
2. **Architecture guidelines** in `AGENTS.md` take precedence over implementation details.
3. **`BookingService.Domain` and `BookingService.Application`** define the business model and use cases; this document mirrors them.
4. **Specifications in this document** define the required functional boundaries for MVP-02.
5. Agents must never invent business rules or entity contracts not defined here or in the Domain/Application code.
