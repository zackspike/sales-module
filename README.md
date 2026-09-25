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
├── scripts/                       # Lifecycle & release automation scripts (bump, changelog)
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

5. **Generate OpenAPI Specification (Contract):**
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