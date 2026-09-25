# BookingService (sap-atitos)
> **MVP-02: Event Search and Purchase (v1.0)**

Backend service responsible for ticket purchasing, idempotency handling, and unique ticket issuance for the event platform.

---

## Requirements
* [.NET 8.0+ SDK](https://dotnet.microsoft.com/download) (Compatible with .NET 8, 9 and 10)

---

## Solution Structure (Clean Architecture & DDD)

The repository follows Domain-Driven Design and Clean Architecture principles:

```text
booking-service/
├── BookingService.Api/            # Minimal APIs, endpoints, middleware, HTTP models
├── BookingService.Application/    # Use cases, application orchestration, DTOs, validations
├── BookingService.Domain/         # Core business entities (Ticket, Event), value objects, domain rules
├── BookingService.Infrastructure/ # In-memory store implementation (ConcurrentDictionary), persistence
├── .agents/                       # Agent workflows, specifications (Notion), and MCP integrations
├── AGENTS.md                      # Global architecture rules & agent workflow protocol
└── BookingService.slnx            # Solution file
```

---

## How to Run Locally

1. **Restore dependencies and build:**
   ```bash
   dotnet build
   ```

2. **Run the API:**
   ```bash
   dotnet run --project BookingService.Api
   ```

3. **Explore the API:**
   * Swagger UI will be available at: `http://localhost:<port>/swagger` (in Development mode).
   * Health check endpoint: `GET /health` (returns `{ "status": "ok" }`).
   * Root status endpoint: `GET /`.

4. **Code Quality & Formatting:**
   * Verify formatting (lint check):
     ```bash
     dotnet format --verify-no-changes
     ```
   * Automatically fix formatting:
     ```bash
     dotnet format
     ```

---

## Branch Strategy

We follow a structured branching model based on `main` and `dev` branches:

```
[main]  <── (PR de release / validado 100%) ── [dev]
                                                 │
                                                 ├──> [feature/TASK-ID] ──> (PR a dev)
```

### Branches

* **`main`**:
  * Represents production-ready, stable code.
  * Only contains code that has been 100% verified, tested, and approved in `dev`.
  * Direct commits to `main` are restricted.

* **`dev`**:
  * Integration branch where all completed features arrive and are tested before reaching `main`.
  * All work passes through `dev`.

* **`feature/<nombre-o-id-tarea>`**:
  * Working branches for individual features or pair programming tasks (e.g., `feature/VAL-02-validations`, `feature/MOCK-01-entities`).
  * Always branched off **`dev`**.

### Development Workflow

1. **Update and branch off `dev`:**
   ```bash
   git checkout dev
   git pull origin dev
   git checkout -b feature/<TASK-ID>-<descripcion-corta>
   ```

2. **Implement and verify locally:**
   ```bash
   dotnet build
   dotnet test
   dotnet format --verify-no-changes
   ```

3. **Open a Pull Request:**
   * Target branch: **`dev`** (never directly to `main`).
   * Include a description of changes and requirements satisfied.

4. **Merge to `main`:**
   * Once all features of the sprint/MVP are verified in `dev`, a Pull Request from `dev` to `main` is created for final release.