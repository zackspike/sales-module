# Booking Service - Agent Instructions

## Project

BookingService is a .NET application following Domain-Driven Design
and Clean Architecture principles.

The solution is divided into four projects plus one xUnit test project per tested layer:

- `BookingService.Api`
- `BookingService.Application`
- `BookingService.Domain`
- `BookingService.Infrastructure` (EF Core + PostgreSQL, migrations in `Migrations/`; Redis seat locks, idempotency and cache)
- `BookingService.Domain.Tests` (references only Domain)
- `BookingService.Application.Tests` (references only Application)
- `BookingService.Infrastructure.Tests` (references Infrastructure; includes PostgreSQL/Redis integration tests)

Other relevant paths:

- `docker-compose.yml`: local PostgreSQL (host port 5433) and Redis (host port 6381).
- `scripts/`: `bump.sh` (version tag) and `changelog.sh` (release notes).
- `.github/workflows/`: `validation.yml` (PR lint + tests) and `release.yml` (tag release).

## Commands

Requires the .NET 10 SDK and Docker.

| Purpose | Command |
|---|---|
| Restore local tools | `dotnet tool restore` |
| Build | `dotnet build` |
| Lint | `dotnet format --verify-no-changes` |
| Test | `dotnet test` |
| Start PostgreSQL and Redis | `docker compose up -d` |
| Apply migrations (incl. seed data) | `dotnet ef database update --project BookingService.Infrastructure --startup-project BookingService.Api` |
| Add a migration | `dotnet ef migrations add <Name> --project BookingService.Infrastructure --startup-project BookingService.Api` |
| Run API | `dotnet run --project BookingService.Api` |

PostgreSQL integration tests are skipped unless `ConnectionStrings__DefaultConnection` is set,
and Redis integration tests unless `ConnectionStrings__Redis` is set (e.g. `localhost:6381`).
Seed data lives in the `SeedDefaultEvent` migration, not in application code.

## Architecture

The dependency direction must remain:

```text
BookingService.Api
        |
        v
BookingService.Application
        |
        v
BookingService.Domain

BookingService.Api
        |
        v
BookingService.Infrastructure
        |
        v
BookingService.Domain
```

BookingService.Domain must not depend on any other project.

BookingService.Application may depend only on BookingService.Domain.

BookingService.Infrastructure may depend on BookingService.Application
and BookingService.Domain.

BookingService.Api may depend on BookingService.Application
and BookingService.Infrastructure.

Do not introduce dependencies that violate this direction.

### Domain Layer

The Domain project contains business concepts and rules.

It may contain:

Entities
Value Objects
Aggregates
Domain Events
Domain Exceptions
Domain Services
Repository abstractions when required by the domain

The Domain layer must not contain:

HTTP concerns
ASP.NET Core controllers/endpoints
Entity Framework implementations
Database-specific code
Configuration binding
Infrastructure implementations
External API clients

The Domain layer must remain independent of frameworks whenever practical.

### Application Layer

The Application project contains use cases and application orchestration.

It may contain:

Commands
Queries
Handlers
Application services
DTOs
Application interfaces
Validation
Behaviors

Application code must depend on abstractions rather than infrastructure
implementations.

Business rules that belong to the domain must not be duplicated in
Application.

### Infrastructure Layer

Infrastructure contains technical implementations.

It may contain:

Entity Framework Core
DbContext
Repository implementations
Database configurations
External service clients
Message bus implementations
Persistence
Infrastructure dependency injection

Infrastructure must implement abstractions defined by Domain or Application.

### API Layer

The API project is responsible for HTTP concerns.

It may contain:

Endpoints
HTTP request/response models when appropriate
Middleware
Authentication/authorization configuration
Dependency injection composition
API-specific configuration

The API must not contain business rules.

Standard endpoints include `GET /health` returning `{ "status": "ok" }` for health check and liveness/readiness probes.

### OpenAPI Contract Generation

The OpenAPI specification (`openapi.json`) must be generated directly from the compiled assembly without requiring a running web server or browser:

```bash
dotnet tool restore
dotnet swagger tofile --output openapi.json BookingService.Api/bin/Debug/net10.0/BookingService.Api.dll v1
```

*(Note: Prior compilation via `dotnet build` is required to produce `BookingService.Api/bin/Debug/net10.0/BookingService.Api.dll`).*

### General Rules

All code contributions must adhere to the rules defined in `.editorconfig` and pass `dotnet format --verify-no-changes`.

All commit messages must strictly follow the [Conventional Commits](https://www.conventionalcommits.org/) format (`feat:`, `fix:`, `chore:`, etc.), enforced locally via `.githooks/commit-msg`.

Do not create abstractions without a concrete architectural reason.

Do not move business logic into Infrastructure or API.

Do not introduce a pattern merely because it is commonly used.

Prefer the simplest implementation that preserves the architecture.

Do not perform unrelated refactoring.

Do not modify files outside the requested scope unless explicitly required.

Before changing architecture, inspect the existing solution and project
references.

### Design Decisions

Decisions the code alone does not explain. Record rules and their reasons here,
not descriptions of the current code (class lists, routes, migration progress):
`BookingService.Domain` and `BookingService.Application` are the source of truth
for the business model and use cases.

- **Scope:** an unauthenticated fan picks an available seat of an event, reserves it
  and purchases it. Out of scope: authentication, real payment gateway, sections
  and pricing calculations. Event search belongs to SearchService, not this service.
- **Seat choice:** the fan always chooses the seat; the backend never assigns one.
- **Data ownership:** `Event` belongs to EventService and `VenueSeat`/`SeatZone` to
  VenueService. BookingService only reads them through `IEventCatalog` and
  `IVenueSeatCatalog`. In-memory catalogs are local test mocks, not an integration.
- **Layer-by-layer refactor:** changes advance Domain → Application → Infrastructure
  → Api. The Api keeps the MVP-02 routes (`ticketId` in the route is the event seat
  id) until it is migrated; do not migrate it as a side effect of other work.
- **Open — EventService/VenueService integration:** PostgreSQL keeps a local `events`
  table (FK from `event_seats.EventId`) but no `VenueSeat` table (`VenueSeatId` and
  `ZoneId` are plain ids). This asymmetry is not a decision. Options:
  - Local replica of both, synchronized from the owning services: reserve/purchase
    do not depend on them being up, and FKs/joins are possible. `VenueSeat` is on
    the hot paths, so it benefits most.
  - Remote lookup of both (possibly cached): drop the `events` table and its FK; no
    duplicated data, but every request depends on those services.

  Decide before regenerating the initial migration: either option changes the schema.

### Workflow Protocol

When executing tasks or user requests, agents must follow the structured lifecycle tracked in `.agents/current/`:

1. **Analysis & Planning Phase**:
   - Save the incoming task description to `.agents/current/request.md`.
   - Inspect domain requirements, architecture, and the Design Decisions above.
   - Formulate the minimal implementation plan in `.agents/current/plan.md` and task instructions in `.agents/current/editor-task.md`.
   - Do not modify source code during this phase.

2. **Execution Phase**:
   - Implement strictly the approved scope defined in `.agents/current/editor-task.md`.
   - Touch only the files explicitly designated as writable targets.
   - Keep business logic in `BookingService.Domain` and application logic in `BookingService.Application`.
   - Document changes in `.agents/current/execution-report.md`.

3. **Audit & Verification Phase**:
   - Verify architectural boundaries and dependency directions.
   - Run verification commands (`dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, and OpenAPI generation) when applicable.
   - Document validation status and findings in `.agents/current/auditor-report.md`.

Detailed role guidelines are defined in `.agents/agents/analyst/agent.md`, `.agents/agents/editor/agent.md`, and `.agents/agents/auditor/agent.md`.