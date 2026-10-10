# BookingService (sap-atitos)
> **MVP-02: Event Search and Purchase (v1.0)**

Backend service responsible for ticket purchasing, idempotency handling, and unique ticket issuance for the event platform.

---

## Requirements
* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* [Docker](https://docs.docker.com/get-docker/) with Docker Compose (PostgreSQL and Redis for local development)

---

## Solution Structure (Clean Architecture & DDD)

The repository follows Domain-Driven Design and Clean Architecture principles:

```text
booking-service/
├── .github/workflows/             # CI/CD and release pipelines (validation.yml, release.yml)
├── .githooks/                     # Git hooks enforcing Conventional Commits (commit-msg)
├── BookingService.Api/            # Minimal APIs, endpoints, middleware, HTTP models
├── BookingService.Application/    # Use cases, application orchestration, DTOs, validations
├── BookingService.Domain/         # Core business entities (Ticket, Event), value objects, domain rules
├── BookingService.Infrastructure/ # EF Core + PostgreSQL persistence, migrations (incl. seed data), Redis seat locks/cache, in-memory fallback
├── BookingService.Application.Tests/ # xUnit unit tests (+ PostgreSQL/Redis integration tests when configured)
├── docker-compose.yml             # Local PostgreSQL and Redis dependencies
├── scripts/                       # Lifecycle & release automation scripts (bump, changelog)
├── .agents/                       # Agent workflows, specifications (Notion), and MCP integrations
├── AGENTS.md                      # Global architecture rules & agent workflow protocol
└── BookingService.slnx            # Solution file
```

---

## How to Run Locally

1. **Configure Git hooks (Conventional Commits):**
   ```bash
   git config core.hooksPath .githooks
   ```

2. **Restore dependencies and build:**
   ```bash
   dotnet build
   ```

3. **Run the API:**
   ```bash
   docker compose up -d   # PostgreSQL on localhost:5433, Redis on localhost:6381 (used by appsettings.Development.json)
   dotnet run --project BookingService.Api
   ```
   In Development the API applies pending EF Core migrations on startup. The `SeedDefaultEvent` migration
   seeds a dummy event (`11111111-1111-1111-1111-111111111111`, "Rock Fest 2026") with 50 available seats (`A-1`..`A-50`).
   To apply migrations manually instead: `dotnet tool restore && dotnet ef database update --project BookingService.Infrastructure --startup-project BookingService.Api`.
   Without `ConnectionStrings:DefaultConnection` it falls back to the in-memory store (data is lost on restart).
   Without `ConnectionStrings:Redis` seat reservations are kept in memory (not shared between instances) and nothing is cached.

   **Purchase flow:** a seat must be reserved before it is bought.
   * `POST /events/{eventId}/tickets/{ticketId}/reserve` locks the seat for the buyer's email for 10 minutes
     (Redis key `booking:seat-lock:{ticketId}`, atomic Lua scripts). The same email renews it; another email gets 409.
   * `POST /events/{eventId}/tickets/{ticketId}/purchase` (with `X-Idempotency-Key`) requires that reservation (403 otherwise).
     Used keys are answered from Redis for 10 minutes before touching the database; after the sale the lock is released.
   * `GET /events/{eventId}/tickets` is cached in Redis for 30 s (invalidated on each sale) and hides reserved seats;
     `GET .../availability` reports `Reserved` for a locked seat.

4. **Explore the API:**
   * Swagger UI will be available at: `http://localhost:<port>/swagger` (in Development mode).
   * Health check endpoint: `GET /health` (returns `{ "status": "ok" }`).
   * Root status endpoint: `GET /`.

5. **Run the tests:**
   ```bash
   dotnet test
   ```
   PostgreSQL integration tests are skipped unless `ConnectionStrings__DefaultConnection` is set
   (e.g. `Host=localhost;Port=5433;Database=bookingservice_db;Username=postgres;Password=postgres`),
   and Redis integration tests unless `ConnectionStrings__Redis` is set (e.g. `localhost:6381`).

6. **Lint (code quality & formatting):**
   * Verify formatting (lint check):
     ```bash
     dotnet format --verify-no-changes
     ```
   * Automatically fix formatting:
     ```bash
     dotnet format
     ```

7. **Generate OpenAPI Specification (Contract):**
   * Restore local tools:
     ```bash
     dotnet tool restore
     ```
   * Generate `openapi.json` from the compiled assembly without requiring a running server or browser:
     ```bash
     dotnet swagger tofile --output openapi.json BookingService.Api/bin/Debug/net10.0/BookingService.Api.dll v1
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

---

## Release & Versioning Scripts

The repository includes automation scripts in `scripts/` to manage Conventional Commit changelogs and automated semantic version releases:

### Prerequisites
* Bash environment (Git Bash, WSL, Linux, or macOS). Both scripts require LF line endings.
* [GitHub CLI (`gh`)](https://cli.github.com) authenticated (`gh auth login`).
* .NET SDK installed (for automated preflight verification with `dotnet test`).

### 1. Changelog Generation (`scripts/changelog.sh`)
Generates Markdown release notes from Conventional Commits since the previous stable release tag:

```bash
# Preview release notes for upcoming release against HEAD
bash scripts/changelog.sh HEAD

# Generate release notes for an existing tag
bash scripts/changelog.sh v1.0.0
```

### 2. Version Bump & Release Tagging (`scripts/bump.sh`)
Calculates the next semantic version, runs preflight validations, creates an annotated git tag, and pushes it to `origin` (triggering the release workflow):

```bash
# Interactive mode (checks status, displays version choices, prompts confirmation)
bash scripts/bump.sh

# Direct bump with auto-confirmation
bash scripts/bump.sh --patch -y
bash scripts/bump.sh --minor -y
bash scripts/bump.sh --major -y

# Pre-release tag
bash scripts/bump.sh --alpha
bash scripts/bump.sh --beta

# Dry run (inspect next version calculation without modifying or pushing tags)
bash scripts/bump.sh --dry-run
```

#### Preflight Checks Performed by `bump.sh`:
1. Current branch is `main`.
2. Working tree is clean (no modified, staged, or untracked files).
3. Local `main` branch is up to date with `origin/main`.
4. CI workflow (`validation.yml`) passed on GitHub for current commit (`HEAD`).
5. Unit test suite passes locally (`dotnet test`).

---

## CI/CD & Automated Pipelines

The repository features automated GitHub Actions workflows to guarantee code quality and automate releases:

### 1. PR & Validation Pipeline (`.github/workflows/validation.yml`)
* **Trigger:** Pull Requests and pushes to `main` and `dev`.
* **Jobs:**
  * **Code Quality & Linting:** Enforces C# styling via `dotnet format --verify-no-changes`.
  * **Tests:** Restores dependencies and runs unit tests via `dotnet test`.

### 2. Release Pipeline (`.github/workflows/release.yml`)
* **Trigger:** Pushing a version tag matching `v*` (e.g., `v1.0.0`, `v1.1.0`, `v0.1.0-alpha`), typically initiated via `scripts/bump.sh`.
* **Permissions:** `contents: write` (grants permission to publish releases and upload asset files).
* **Pipeline Steps:**
  1. **Full History Checkout:** Clones the repository with `fetch-depth: 0` so `git describe` and `scripts/changelog.sh` have access to the complete history of tags and commits.
  2. **SDK Setup & Tooling Restore:** Configures .NET 10.0.x SDK and runs `dotnet restore` and `dotnet tool restore` (installing local CLI tools such as Swashbuckle CLI).
  3. **Build:** Compiles the solution with `dotnet build`.
  4. **OpenAPI Contract Generation:** Extracts the versioned API specification directly from the compiled assembly without needing a running server:
     ```bash
     dotnet swagger tofile --output openapi.json BookingService.Api/bin/Debug/net10.0/BookingService.Api.dll v1
     ```
  5. **Changelog Generation:** Runs `bash scripts/changelog.sh "${{ github.ref_name }}" > release-notes.md` to parse Conventional Commits since the previous release.
  6. **GitHub Release Publication:** Uses the GitHub CLI (`gh release create`) authenticated with `GH_TOKEN: ${{ github.token }}` to publish the release titled with the tag name, embedding the release notes in the body, and attaching `openapi.json` as an asset.
