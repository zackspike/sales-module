# Project Knowledge & Technical Implementation Plan (MVP-02)

## Source of Truth & Synchronization
* **Online Source (Notion):** [sap-atitos](https://app.notion.com/p/sap-atitos-3de7bd21751a8053a5f9e961829231cb?source=copy_link)
* **Page ID:** `3de7bd21-751a-8053-a5f9-e961829231cb`
* **MCP Integration:** Optional live synchronization configured in `.agents/plugins/notion/mcp_config.json`.
* **Offline-First:** This file acts as the local cached source of truth. All agents must follow these specifications directly without requiring active internet access or external API tokens.

---

## 1. Project Overview & Scope (MVP-02)

**Goal:** Event Search and Purchase (v1.0)
* An unauthenticated fan searches for an event by name, views event details, and completes a ticket purchase.
* **Out of scope:** Authentication/Login, real payment gateway, external third-party systems.

### Participating Services
* **Error200 (Front):** Fan-facing UI (search input, event details, purchase form, ticket display).
* **Error200 (SearchService):** Event search backend (`GET /events?name={name}`, `GET /events/{id}`).
* **Sap-atitos (BookingService):** Ticket purchase backend (**this repository**).

---

## 2. Technical Stack & Architectural Directives

* **Framework:** .NET 8.0+ / C# Minimal APIs.
* **Architecture:** Domain-Driven Design (DDD) & Clean Architecture:
  * `BookingService.Domain`: Core entities (`Ticket`, `Event`), value objects, domain exceptions, repository interfaces.
  * `BookingService.Application`: Use cases, commands, queries, DTOs, business validations.
  * `BookingService.Infrastructure`: In-memory storage implementations (`InMemoryBookingStore`).
  * `BookingService.Api`: Minimal API endpoints, middleware, dependency injection composition.
* **Data Strategy:** **In-Memory Concurrent Collections (No Database)**:
  * Use thread-safe collections (`ConcurrentDictionary<TKey, TValue>`) for storing tickets, idempotency keys, and mock events.
* **Documentation & Testing:**
  * Minimal APIs with OpenAPI / Swagger support.
  * Integration testing via `.http` files (e.g. `BookingService.Api.http`).

---

## 3. Detailed Functional Requirements (Sap-atitos / BookingService)

### Requirement: SP-05 — Ticket Purchase Endpoint

* **Endpoint:** `POST /events/{event-id}/tickets`
* **Authentication:** None (public endpoint).
* **Route Parameter:** `event-id` (identifier of the target event).
* **Request Payload (JSON):**
  ```json
  {
    "fullName": "Jane Doe",
    "email": "jane.doe@example.com"
  }
  ```
* **Validations & Error Responses:**
  * **400 Bad Request:** If `fullName` or `email` is missing, empty, or if `email` is not a valid email format. The response body must indicate which field(s) failed validation.
  * **404 Not Found:** If `event-id` does not match any known event in the store.
* **Success Response (201 Created or 200 OK on idempotent replay):**
  ```json
  {
    "ticketCode": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "eventId": "event-123",
    "fullName": "Jane Doe",
    "email": "jane.doe@example.com",
    "createdAt": "2026-09-20T18:00:00Z"
  }
  ```

---

### Requirement: SP-06 — Ticket Creation Idempotency

* **Header:** `X-Idempotency-Key` (mandatory for safe replay).
* **Format:** Must be a valid `GUID` (e.g., `UUIDv4`).
  * If the header is provided but is not a valid GUID format, reject with **400 Bad Request**.
* **Behavior:**
  * Exactly one ticket is created per unique idempotency key.
  * If a request with an already-processed idempotency key is received, return the previously issued ticket (**200 OK**) without creating a second ticket.
  * Protects against network retries or concurrent double-submissions.

---

### Requirements: SP-07 & SP-08 — Unique Ticket Codes

* **Return Code (`SP-07`):** The purchase response must return the generated `ticketCode`.
* **Format:** The code uses the format `"TK-{GUID:N}"` (e.g., `TK-4f32a76fbf424b9195980da672807f87`). The standard GUID string shown in the SP-05 example response is illustrative.
* **Global Uniqueness Guarantee (`SP-08`):**
  * The ticket code generation mechanism (UUID / GUID) must guarantee that no two tickets share a code across all events in the system.
  * This rule holds even if the same person purchases multiple tickets for the exact same event.

---

### Requirement: Cross-Cutting — Global Exception Handling & Setup

* **Middleware:** Centralized global exception handler in `BookingService.Api` / common middleware.
* **Standard Error Responses:** Clean, consistent JSON error payloads (`title`, `status`, `detail`, `errors`).
* **CORS:** Enabled for local frontend development.

---

## 4. Work Breakdown & Kanban Mapping

| Task ID | Task Title | Layer / Domain | Key Deliverable |
| :--- | :--- | :--- | :--- |
| `SETUP-BS-T1` | Initial Repository & Git | Cross-Cutting | Git setup, branching, `.gitignore`. |
| `SETUP-BS-T2` | Minimal APIs & Swagger | Cross-Cutting / Api | Web host, Swagger, CORS, `.http` test file. |
| `SETUP-BS-T3` | Global Exception Middleware | Cross-Cutting / Api | Centralized error handling returning clean JSON. |
| `MOCK-01` | Entity Modeling | Domain | `Ticket` and `Event` domain models. |
| `MOCK-02` | `InMemoryBookingStore` | Infrastructure | Thread-safe in-memory store with seeded events. |
| `VAL-01` | Request & Response DTOs | Application | Strongly-typed contracts for input/output. |
| `VAL-02` | Purchase Validations | Application | Required fields, email format (`400`), event existence (`404`). |
| `VAL-03` | Unique Ticket Code Generator | Domain | As example: Collision-free GUID format code generator. |
| `API-01` | `POST /events/{id}/tickets` | Api / Application | Endpoint handler orchestrating purchase. |
| `API-02` | Idempotency Handler | Api / Application | `X-Idempotency-Key` validation and replay cache. |

---

## 5. Precedence Rules

1. **Explicit user instructions** in conversation take highest precedence.
2. **Architecture guidelines** in `AGENTS.md` take precedence over implementation details.
3. **Specifications in this document** define the required functional boundaries for MVP-02.
4. Agents must never invent business rules or entity contracts not defined here.