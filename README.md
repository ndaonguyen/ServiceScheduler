# AppointmentScheduler

A backend .NET service for scheduling vehicle service appointments across dealerships,
technicians, and service bays. **Modular monolith** with **Clean Architecture** and **vertical-slice
CQRS** over a lightweight in-process mediator (no MediatR), JWT + cookie auth with RBAC, EF Core +
PostgreSQL, OpenTelemetry observability, and health checks. No frontend — the API is the product;
`/openapi/v1.json` is the client contract.

## What it does

The domain surface is deliberately small and focused on one core use case: **booking a service
appointment**. A single authenticated endpoint —

```
POST /api/appointments   { vehicleId, dealershipId, serviceTypeId, requestedStart }
```

— validates the request, resolves the service duration, the dealership's bays, vehicle ownership,
and the qualified technicians, assigns the first free bay + technician for the window (retrying once
on a concurrent-booking conflict), persists a confirmed appointment, and returns the assignment.
Everything else (auth, profile, health, OpenAPI) is supporting infrastructure.

## Architecture

One deployable process, split into feature **modules**, each a class-library project with
Clean-Architecture layers as folders inside it. Module boundaries are **compiler-enforced** by the
`ProjectReference` graph and backed by a NetArchTest suite — a module references only its own
`Contracts`, the shared `BuildingBlocks`, and other modules' `Contracts` (never their
implementation).

| Module | Role |
|--------|------|
| **Booking** | The full vertical slice — Domain + Application (CQRS) + Infrastructure + the `POST /api/appointments` endpoint. |
| **Fleet** | Supporting read side — dealerships, service bays, vehicles; exposes `IServiceBayLookup`, `IVehicleOwnershipQuery`. |
| **Catalog** | Supporting read side — service types; exposes `IServiceTypeLookup`. |
| **Workforce** | Supporting read side — technicians + qualifications; exposes `IQualifiedTechnicianLookup`. |

The `RequestAppointment` handler in Booking fans out to the other three modules **only through their
`Contracts` query ports** — the canonical cross-module read pattern. See
[`CLAUDE.md`](CLAUDE.md) and the [ADRs](docs/adrs/) for the full design (modular monolith:
[ADR-0001](docs/adrs/0001-modular-monolith.md); project-per-module:
[ADR-0006](docs/adrs/0006-project-per-module-physical-structure.md)).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/) (pinned by `global.json`, prerelease allowed)
- [Docker](https://www.docker.com/) (for local PostgreSQL via `docker-compose.yml`)

## Build

```bash
dotnet restore && dotnet tool restore
dotnet build -c Release
```

## Run

```bash
docker compose up -d                                    # start local Postgres (postgres:17 on :5432)
dotnet run --project src/Host/AppointmentScheduler.Api  # the only executable
```

In **Development** the API auto-applies EF migrations and seeds roles + an optional dev admin on
startup (`DbInitializer`). The API listens on `http://localhost:5080` by default. `/` redirects to
`/openapi/v1.json` — use that as the contract for any client or `curl` harness.

Quick smoke test of the full auth + booking flow:

```bash
BASE=http://localhost:5080

curl -c cookies.txt -X POST "$BASE/api/auth/register" -H "Content-Type: application/json" \
  -d '{"email":"me@x.com","password":"Passw0rd!$"}'

curl -c cookies.txt -X POST "$BASE/api/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"me@x.com","password":"Passw0rd!$"}'

curl -b cookies.txt "$BASE/api/profile/me"              # 200; current user + roles

curl -b cookies.txt -X POST "$BASE/api/appointments" -H "Content-Type: application/json" \
  -d '{"vehicleId":"...","dealershipId":"...","serviceTypeId":"...","requestedStart":"2026-08-01T09:00:00Z"}'
```

## Test

```bash
dotnet test -c Release
```

The suite has three projects, run together by the command above:

| Project | Kind | What it validates |
|---------|------|-------------------|
| `tests/AppointmentScheduler.Application.Tests` | **Core business logic** (xUnit + AwesomeAssertions) | The `RequestAppointment` handler in isolation: happy path, `end = start + duration`, every validation error (unknown service type / dealership / vehicle, vehicle-not-owned, past start), no-qualified-technician and no-bay-available shortages, and the boundary/overlap rules (adjacent appointments don't conflict, 1-second overlap does). |
| `tests/AppointmentScheduler.Api.Tests` | **Integration** over `WebApplicationFactory` | The HTTP pipeline end-to-end: booking endpoint, the real cookie/JWT auth flow (`AuthEndpointsTests`), profile, and health checks. |
| `tests/AppointmentScheduler.ArchitectureTests` | **Architecture** (NetArchTest) | The module boundaries and aggregate rules that the compiler can't fully express — a module never depends on another module's Domain/Infrastructure, Domain takes no persistence/web dependency. |

The **core business logic** lives in `RequestAppointmentTests` (17 cases), each mapped to a numbered
acceptance/business rule (e.g. `AT-08 / BR-01`) so a failing test names the rule it broke.

To run one project only:

```bash
dotnet test tests/AppointmentScheduler.Application.Tests -c Release
```

## Project layout

| Path | Purpose |
|------|---------|
| `src/Host/AppointmentScheduler.Api/` | The only executable — `Program.cs`, endpoint groups, security wiring, `DbInitializer` |
| `src/BuildingBlocks/AppointmentScheduler.BuildingBlocks/` | Mediator + cross-cutting ports (`ICurrentUser`) |
| `src/BuildingBlocks/AppointmentScheduler.BuildingBlocks.Persistence/` | Shared `AppDbContext`, Identity, refresh tokens, `Migrations/` |
| `src/Modules/<Module>/` | Feature module (`<Module>` + `<Module>.Contracts` projects) |
| `tests/AppointmentScheduler.Application.Tests/` | Core business-logic handler tests |
| `tests/AppointmentScheduler.Api.Tests/` | Integration tests over `WebApplicationFactory` |
| `tests/AppointmentScheduler.ArchitectureTests/` | Module-boundary + aggregate-rule tests |
| `AppointmentScheduler.sln` | Solution file |
| `docker-compose.yml` | Local dev PostgreSQL (+ Aspire dashboard for telemetry) |
| `docs/` | ADRs, design specs (`authentication.md`), PRDs, and per-issue plans |
| `CLAUDE.md` | Detailed architecture notes and conventions |

## Database migrations

Schema is owned by EF Core migrations in
`src/BuildingBlocks/AppointmentScheduler.BuildingBlocks.Persistence/Migrations/`. EF column mappings
(snake_case) define the schema — there is no hand-written SQL to keep in sync.

```bash
# add a migration after changing entities/DbContext
dotnet ef migrations add Describe_change \
  --project src/BuildingBlocks/AppointmentScheduler.BuildingBlocks.Persistence \
  --startup-project src/Host/AppointmentScheduler.Api

# apply pending migrations manually (Development does this automatically on startup)
dotnet ef database update \
  --project src/BuildingBlocks/AppointmentScheduler.BuildingBlocks.Persistence \
  --startup-project src/Host/AppointmentScheduler.Api
```

Override the connection with the `AppDb__ConnectionString` env var. In production, migrations run as a
deliberate deploy step (`.github/workflows/deploy.yaml`), never on startup.

## Authentication

JWT access (15 min) and refresh (7-day) tokens, both transported as `httpOnly`, `Secure`,
`SameSite=Strict` cookies — never exposed to JavaScript (XSS-safe) and CSRF-safe without a separate
token. ASP.NET Core Identity is the user store. See
[`docs/authentication.md`](docs/authentication.md) for the full design.

## Observability

OpenTelemetry (traces + metrics) is wired in `Program.cs` and exported over OTLP — point
`OTEL_EXPORTER_OTLP_*` at a collector, or use the Aspire dashboard from `docker-compose.yml`
(`http://localhost:4317`). Health checks: `/health/live` (liveness), `/health/ready` (readiness,
checks the database), `/health` (liveness alias).

## AI Collaboration Narrative

This service was built in close collaboration with an AI coding assistant (Claude Code). The AI was a
force multiplier, not an autopilot: I set the architecture and the acceptance criteria, drove the work
in small verifiable slices, and treated every AI output as a proposal to be reviewed rather than a
result to be accepted.

### High-level strategy for guiding the AI

- **Constrain first, generate second.** Before writing features I established the guardrails the AI
  had to work inside — a modular-monolith architecture, Clean-Architecture layering, and explicit
  module boundaries — and captured them in [`CLAUDE.md`](CLAUDE.md) and a set of
  [ADRs](docs/adrs/). Because those decisions live in the repo, every AI session starts already
  knowing the rules, so its output lands consistent with what came before instead of drifting.
- **Spec-driven, one slice at a time.** Work flowed from a PRD → a per-issue plan → implementation,
  each as its own vertical slice (e.g. availability computation, the DB-level double-booking
  constraint). Keeping each unit of work small and independently testable made the AI's output easy
  to reason about and easy to reject when it was wrong.
- **Make the rules machine-checkable.** Rather than relying on the AI (or myself) to *remember* the
  boundaries, I encoded them as `ProjectReference` graphs and NetArchTest architecture tests. A
  boundary violation becomes a failed build, not a review comment — the fastest possible feedback.
- **Traceable acceptance criteria.** Each business rule and validation carries a stable code
  (`AT-08`, `BR-01`, PRD §8/§10). The AI was asked to reference those codes in comments and test
  names, so intent stays anchored to the spec and a failing test names exactly which rule broke.

### Verifying and refining the AI's output

- **Tests are the contract, not the prose.** The core logic (`RequestAppointment`) is pinned by 17
  handler tests, each mapped to a numbered acceptance criterion. I refined the AI's implementation
  by first agreeing the test list, then holding the code to it — red/green, not "looks right."
- **Adversarial reading of every diff.** I reviewed each change looking specifically for the things
  AI gets subtly wrong: off-by-one boundary conditions (an appointment ending exactly at the
  requested start must *not* conflict), race conditions on insert, and trusting client-supplied
  values (duration and owner are resolved server-side, never taken from the request body).
- **Verify against reality, not the model's claims.** When the AI described the codebase, I checked
  the actual source — which is how this very README was corrected: it had drifted to describe an
  older `source/`-per-layer layout that no longer matched the real `src/` project-per-module
  structure.
- **Docs kept honest.** Architectural decisions were written down as ADRs at the moment they were
  made, and discrepancies between docs and code were treated as bugs to fix, not noise to ignore.

### Ensuring final quality

- **Multi-layered automated gates.** Unit tests (business logic), integration tests (the real HTTP +
  cookie/JWT pipeline), and architecture tests (module boundaries) all run in CI on every push/PR,
  alongside build + coverage. Quality is enforced by the pipeline, not by good intentions.
- **Defense in depth for the critical invariant.** Double-booking is prevented at *two* levels — the
  application checks availability and retries, and a PostgreSQL exclusion constraint rejects any
  overlap that slips through a race. The AI proposed the application logic; I insisted the database
  be the ultimate source of truth.
- **The human owns the boundaries.** The AI accelerated implementation, but architecture decisions,
  the acceptance criteria, and the review of every merge were mine. The result is code I understand
  end-to-end and can defend line by line.

## More documentation

See [`CLAUDE.md`](CLAUDE.md) for detailed architecture notes and conventions, and [`docs/`](docs/)
for ADRs, design specs, and per-issue plans/PRDs.
