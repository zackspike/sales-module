# Project Knowledge & Technical Implementation Plan (MVP-02)

## Source of Truth & Synchronization
* **Online Source (Notion):** [sap-atitos](https://app.notion.com/p/sap-atitos-3de7bd21751a8053a5f9e961829231cb?source=copy_link)
* **Page ID:** `3de7bd21-751a-8053-a5f9-e961829231cb`
* **Tasks Database ID:** `3de7bd21-751a-8013-9ca7-d994b2527813` (`collection://3de7bd21-751a-8014-be08-000b8740852b`)
* **MCP Integration:** Live synchronization configured in `.agents/plugins/notion/mcp_config.json`.
* **Offline-First:** This file acts as the local cached source of truth. All agents must follow these specifications directly without requiring active internet access or external API tokens.

---

## 1. Project Overview & Scope (MVP-02)

**Goal:** Event Search and Purchase (v1.0)
* An unauthenticated fan searches for an event by name, views event details, and completes a ticket purchase.
* **Out of scope:** Authentication/Login, real payment gateway, external third-party systems, seat selection, sections, and pricing calculations.

### Participating Services
* **Error200 (Front):** Fan-facing UI (search input, event details, purchase form, ticket display).
* **Error200 (SearchService):** Event search backend (`GET /events?name={name}`, `GET /events/{id}`).
* **Sap-atitos (BookingService):** Ticket purchase backend (**this repository**).

---

## 2. Technical Stack & Architectural Directives

* **Framework:** .NET 10 / C# Minimal APIs.
* **Architecture:** Domain-Driven Design (DDD) & Clean Architecture:
  * `BookingService.Domain`: Core entities (`Ticket`, `Event`), code generator (`TicketCodeGenerator`), domain rules.
  * `BookingService.Application`: Use cases, commands (`PurchaseTicketCommand`), DTOs, validations (`TicketPurchaseValidator`), abstractions (`ITicketRepository`, `IEventCatalog`).
  * `BookingService.Infrastructure`: In-memory storage implementations (`InMemoryTicketRepository`, `InMemoryEventCatalog`).
  * `BookingService.Api`: Minimal API endpoints, middleware (`GlobalExceptionMiddleware`), dependency injection composition (`Program.cs`).
* **Data Strategy:** **In-Memory Concurrent Collections (No Database)**:
  * Thread-safe memory storage for tickets (`InMemoryTicketRepository` with concurrency `Lock`).
  * Thread-safe mock event catalog (`InMemoryEventCatalog`) seeded with known test event ID: `11111111-1111-1111-1111-111111111111`.
* **Documentation & Testing:**
  * Minimal APIs with OpenAPI / Swagger UI support (`/swagger`).
  * Integration testing via `.http` file (`BookingService.Api.http`).
  * Unit and concurrency testing with xUnit (`BookingService.Application.Tests`).

---

## 3. Detailed Functional Requirements (Sap-atitos / BookingService)

### Requirement: SP-05 — Ticket Purchase Endpoint

* **Endpoint:** `POST /events/{eventId:guid}/tickets`
* **Authentication:** None (public endpoint).
* **Route Parameter:** `eventId` (GUID identifier of the target event).
* **Request Payload (JSON):**
  ```json
  {
    "fullName": "Jane Doe",
    "email": "jane.doe@example.com"
  }
  ```
* **Validations & Error Responses:**
  * **400 Bad Request:** If `fullName` or `email` is missing, empty, or if `email` is not a valid email format (RFC 5321). The response body returns an `errors` dictionary indicating failing field(s).
  * **404 Not Found:** If `eventId` does not match any known event in `IEventCatalog`.
* **Success Response (201 Created on new issuance, or 200 OK on idempotent replay):**
  ```json
  {
    "ticketId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "ticketCode": "TK-4f32a76fbf424b9195980da672807f87",
    "eventId": "11111111-1111-1111-1111-111111111111",
    "fullName": "Jane Doe",
    "email": "jane.doe@example.com",
    "createdAt": "2026-09-20T18:00:00Z"
  }
  ```
  *(Note: Field `createdAt` is explicitly serialized as camelCase via `[property: JsonPropertyName("createdAt")]` in `TicketDto`)*.

---

### Requirement: SP-06 — Ticket Creation Idempotency

* **Header:** `X-Idempotency-Key` (safe replay mechanism).
* **Format:** Must be a valid `GUID` (e.g., UUIDv4).
  * If the header is provided but is not a valid GUID format, ASP.NET Core raises `BadHttpRequestException`, intercepted by `GlobalExceptionMiddleware` to return **400 Bad Request**.
* **Behavior:**
  * Exactly one ticket is created per unique idempotency key.
  * Replaying a request with an already-processed key returns the previously issued ticket (**200 OK**) without creating a second ticket.
  * Atomically enforced in `InMemoryTicketRepository` under synchronized `Lock`.

---

### Requirements: SP-07 & SP-08 — Unique Ticket Codes

* **Return Code (`SP-07`):** The purchase response must return the generated `ticketCode`.
* **Format:** The code uses the format `"TK-{GUID:N}"` (e.g., `TK-4f32a76fbf424b9195980da672807f87`). The pure GUID string in legacy JSON examples is illustrative.
* **Global Uniqueness Guarantee (`SP-08`):**
  * The ticket code generation mechanism in `TicketCodeGenerator` guarantees 122 bits of entropy so that no two tickets share a code across all events in the system.
  * Holds even if the same person purchases multiple tickets for the exact same event under parallel/concurrent traffic.

---

### Requirement: Cross-Cutting — Global Exception Handling & Setup

* **Middleware:** Centralized global exception handler in `BookingService.Api.Common.GlobalExceptionMiddleware`.
* **Standard Error Responses:** Clean, consistent JSON error payloads (`statusCode`, `message`, `details`, `traceId`, `timestampUtc`).
  * `BadHttpRequestException` -> `400 Bad Request`
  * `KeyNotFoundException` -> `404 Not Found`
  * `ArgumentException` / `InvalidOperationException` -> `400 Bad Request`
  * Unhandled exceptions -> `500 Internal Server Error`
* **CORS:** Enabled for local frontend development (`AllowAnyOrigin`, `AllowAnyHeader`, `AllowAnyMethod`).

---

## 4. Work Breakdown & Kanban Mapping

| Task ID | Task Title | Layer | Estado Actual | Entregable Clave |
| :--- | :--- | :--- | :---: | :--- |
| `SETUP-BS-T1` | Initial Repository & Git | Cross-Cutting | **Listo** | Repo en GitHub, branch strategy (`main`/`dev`), `.gitignore`. |
| `SETUP-BS-T2` | Minimal APIs & Swagger | Api | **Listo** | Host web, Swagger UI funcional, CORS y `.http` test file. |
| `SETUP-BS-T3` | Global Exception Middleware | Api | **Listo** | Centralized error handling retornando JSON y mapeo de HTTP 400. |
| `MOCK-01` | Entity Modeling | Domain | **Listo** | Modelos de dominio `Ticket.cs` y `Event.cs`. |
| `MOCK-02` | `InMemoryBookingStore` | Infrastructure | **Listo** | `InMemoryTicketRepository` thread-safe e `InMemoryEventCatalog` con eventos precargados. |
| `VAL-01` | Request & Response DTOs | Application / Api | **Listo** | `PurchaseTicketCommand` y `TicketDto` con serialización `"createdAt"`. |
| `VAL-02` | Purchase Validations | Application | **Listo** | `TicketPurchaseValidator` con suite xUnit (400 required/email, 404 event). |
| `VAL-03` | Unique Ticket Code Generator | Domain | **Listo** | `TicketCodeGenerator` ("TK-{GUID:N}") con tests de concurrencia masiva. |
| `API-01` | `POST /events/{id}/tickets` | Api | **Listo / Integrado** | Endpoint mapeado en `BookingEndpoints.cs` con resolución por DI. |
| `API-02` | Idempotency Handler | Api / Infra | **Listo / Integrado** | `X-Idempotency-Key` en endpoint, middleware HTTP 400 y cache en repo. |

---

## 5. Precedence Rules

1. **Explicit user instructions** in conversation take highest precedence.
2. **Architecture guidelines** in `AGENTS.md` take precedence over implementation details.
3. **Specifications in this document** define the required functional boundaries for MVP-02.
4. Agents must never invent business rules or entity contracts not defined here.