# BookingService (sap-atitos)
> **MVP-02: Event Search and Purchase (v1.0)**

Backend service responsible for ticket purchasing, idempotency handling, and unique ticket issuance for the event platform.

---

## Requirements
* [.NET 8.0+ SDK](https://dotnet.microsoft.com/download) (Compatible with .NET 8, 9 and 10)
* [Docker](https://www.docker.com/) with Docker Compose (PostgreSQL database and integration tests)

---

## Solution Structure (Clean Architecture & DDD)

The repository follows Domain-Driven Design and Clean Architecture principles:

```text
booking-service/
├── .github/workflows/             # CI/CD and release pipelines (validation.yml, release.yml)
├── .githooks/                     # Git hooks enforcing Conventional Commits (commit-msg)
├── BookingService.Api/            # Minimal APIs, endpoints, middleware, HTTP models
├── BookingService.Application/    # Use cases, application orchestration, DTOs, validations
├── BookingService.Domain/         # Core business entities (Event, Zone, Seat, User, Ticket), domain rules
├── BookingService.Infrastructure/ # PostgreSQL persistence (EF Core DbContext, repositories, unit of work)
├── BookingService.Application.Tests/    # Unit tests (domain, validators, handlers with fakes)
├── BookingService.Infrastructure.Tests/ # Integration tests against PostgreSQL (Testcontainers) and the HTTP API
├── database/init/                 # SQL schema and seed scripts (source of truth of the database structure)
├── docker-compose.yml             # Local PostgreSQL database
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

2. **Start the PostgreSQL database:**
   ```bash
   docker compose up -d
   ```
   * Listens on `localhost:5434` (override with `BOOKING_DB_PORT`); credentials default to the development values in `docker-compose.yml` and can be overridden with `BOOKING_DB_NAME`, `BOOKING_DB_USER` and `BOOKING_DB_PASSWORD`.
   * On the first start it runs `database/init/001_create_booking_schema.sql` (tables `events`, `event_zones`, `zone_seats`, `users`, `tickets`) and `002_seed_default_event.sql` (event `11111111-1111-1111-1111-111111111111` with 50 seats `A-1`..`A-50`).
   * To recreate the database from the scripts: `docker compose down -v && docker compose up -d`.
   * The API reads the connection string `ConnectionStrings:BookingDatabase` (set in `appsettings.Development.json`; use the `ConnectionStrings__BookingDatabase` environment variable elsewhere).

3. **Restore dependencies and build:**
   ```bash
   dotnet build
   ```

4. **Run the API:**
   ```bash
   dotnet run --project BookingService.Api
   ```

5. **Explore the API:**
   * Swagger UI will be available at: `http://localhost:<port>/swagger` (in Development mode).
   * Health check endpoint: `GET /health` (returns `{ "status": "ok" }`).
   * Root status endpoint: `GET /`.
   * Available seats: `GET /events/{eventId}/tickets/available`.
   * Seat availability: `GET /events/{eventId}/tickets/{ticketId}/availability`.
   * Seat purchase: `POST /events/{eventId}/tickets/{ticketId}/purchase` with body `{ "fullName", "email" }` and header `X-Idempotency-Key` (201 created, 200 idempotent replay, 400, 404, 409 seat sold or key reused).

6. **Run the tests** (Docker must be running; integration tests start their own PostgreSQL container):
   ```bash
   dotnet test
   ```

7. **Code Quality & Formatting:**
   * Verify formatting (lint check):
     ```bash
     dotnet format --verify-no-changes
     ```
   * Automatically fix formatting:
     ```bash
     dotnet format
     ```

8. **Generate OpenAPI Specification (Contract):**
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