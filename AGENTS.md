# Booking Service - Agent Instructions

## Project

BookingService is a .NET application following Domain-Driven Design
and Clean Architecture principles.

The solution is divided into four projects:

- `BookingService.Api`
- `BookingService.Application`
- `BookingService.Domain`
- `BookingService.Infrastructure`

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

### General Rules

All code contributions must adhere to the rules defined in `.editorconfig` and pass `dotnet format --verify-no-changes`.

Do not create abstractions without a concrete architectural reason.

Do not move business logic into Infrastructure or API.

Do not introduce a pattern merely because it is commonly used.

Prefer the simplest implementation that preserves the architecture.

Do not perform unrelated refactoring.

Do not modify files outside the requested scope unless explicitly required.

Before changing architecture, inspect the existing solution and project
references.

### Source of Architectural Knowledge

Additional project knowledge may be available in:

.agents/knowledge/notion.md

When the Notion source is configured, use it as project-specific
documentation and architectural context.

Repository code and explicit user instructions take precedence over
general recommendations from the Notion documentation.

### Workflow Protocol

When executing tasks or user requests, agents must follow the structured lifecycle tracked in `.agents/current/`:

1. **Analysis & Planning Phase**:
   - Save the incoming task description to `.agents/current/request.md`.
   - Inspect domain requirements, architecture, and `.agents/knowledge/notion.md`.
   - Formulate the minimal implementation plan in `.agents/current/plan.md` and task instructions in `.agents/current/editor-task.md`.
   - Do not modify source code during this phase.

2. **Execution Phase**:
   - Implement strictly the approved scope defined in `.agents/current/editor-task.md`.
   - Touch only the files explicitly designated as writable targets.
   - Keep business logic in `BookingService.Domain` and application logic in `BookingService.Application`.
   - Document changes in `.agents/current/execution-report.md`.

3. **Audit & Verification Phase**:
   - Verify architectural boundaries and dependency directions.
   - Run verification commands (`dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`) when applicable.
   - Document validation status and findings in `.agents/current/auditor-report.md`.

Detailed role guidelines are defined in `.agents/agents/analyst/agent.md`, `.agents/agents/editor/agent.md`, and `.agents/agents/auditor/agent.md`.