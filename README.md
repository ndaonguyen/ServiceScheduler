# AppointmentScheduler

A backend .NET service for scheduling vehicle service appointments across dealerships,
technicians, and service bays. **Modular monolith** with **Clean Architecture** and **vertical-slice
CQRS** over a lightweight in-process mediator (no MediatR), JWT + cookie auth with RBAC, EF Core +
PostgreSQL, OpenTelemetry observability, and health checks. No frontend — the API is the product;
`/openapi/v1.json` is the client contract.

## What it does

The domain surface is deliberately small and focused on one core use case: the **lifecycle of a
service appointment** — book it, reschedule it, cancel it. Three authenticated endpoints:

```
POST   /api/appointments                  { vehicleId, dealershipId, serviceTypeId, requestedStart }
POST   /api/appointments/{id}/reschedule  { newStart }
DELETE /api/appointments/{id}
```

**Booking** validates the request, resolves the service duration, the dealership's bays, vehicle
ownership, and the qualified technicians, assigns the first free bay + technician for the window
(retrying once on a concurrent-booking conflict), persists a confirmed appointment, and returns the
assignment. **Reschedule** re-derives the duration and re-runs availability for the new window —
possibly reassigning bay/technician, or rejecting with `409` if nothing is free — and never treats
the appointment's own current slot as a conflict. **Cancel** is a soft state transition (status →
`Cancelled`, not a row delete), which frees the slot for future bookings. Everything else (auth,
profile, health, OpenAPI) is supporting infrastructure.

## Architecture

One deployable process, split into feature **modules**, each a class-library project with
Clean-Architecture layers as folders inside it. Module boundaries are **compiler-enforced** by the
`ProjectReference` graph and backed by a NetArchTest suite — a module references only its own
`Contracts`, the shared `BuildingBlocks`, and other modules' `Contracts` (never their
implementation).

| Module | Role |
|--------|------|
| **Booking** | The full vertical slice — Domain + Application (CQRS) + Infrastructure + the `/api/appointments` endpoints (book, reschedule, cancel). |
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

Quick smoke test of the full auth + booking flow. This logs in as the **seeded dev customer**
(`customer@example.com`, seeded by `DbInitializer` and the owner of the seeded vehicles) and books
against the seeded IDs, so the booking actually succeeds end-to-end. (For a brand-new account use
`/api/auth/register` instead — but a fresh user owns no vehicles, so the booking call would 403 until
you give it one.)

```bash
BASE=http://localhost:5080

# Seeded dev customer — owns the Corolla/Civic/F-150 below
curl -c cookies.txt -X POST "$BASE/api/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"customer@example.com","password":"Passw0rd!$"}'

curl -b cookies.txt "$BASE/api/profile/me"              # 200; current user + roles

# Seeded IDs: Corolla + Springfield Downtown + Oil change (45 min). 201 Created.
curl -b cookies.txt -X POST "$BASE/api/appointments" -H "Content-Type: application/json" \
  -d '{"vehicleId":"5b0a0000-0000-0000-0000-000000000001","dealershipId":"0e1c0000-0000-0000-0000-000000000001","serviceTypeId":"8f210000-0000-0000-0000-000000000001","requestedStart":"2026-08-01T09:00:00Z"}'
```

The full set of seeded IDs (service types, dealerships, vehicles) lives in `DbInitializer`, and the
same values are pre-filled in the Postman environment below.

## Test

```bash
dotnet test -c Release
```

The suite has three projects, run together by the command above:

| Project | Kind | What it validates |
|---------|------|-------------------|
| `tests/AppointmentScheduler.Application.Tests` | **Core business logic** (xUnit + AwesomeAssertions) | The Booking handlers in isolation — `RequestAppointment` (happy path, `end = start + duration`, every validation error, no-qualified-technician / no-bay-available shortages, and the boundary/overlap rules), plus the lifecycle handlers `RescheduleAppointment` (duration re-derived, availability re-run, self-slot not a conflict, past/cancelled guards) and `CancelAppointment` (soft-cancel, not-found / already-cancelled / ownership guards). |
| `tests/AppointmentScheduler.Api.Tests` | **Integration** over `WebApplicationFactory` | The HTTP pipeline end-to-end: booking endpoint, the real cookie/JWT auth flow (`AuthEndpointsTests`), profile, and health checks. |
| `tests/AppointmentScheduler.ArchitectureTests` | **Architecture** (NetArchTest) | The module boundaries and aggregate rules that the compiler can't fully express — a module never depends on another module's Domain/Infrastructure, Domain takes no persistence/web dependency. |

The **core booking logic** lives in `RequestAppointmentTests` (17 cases), each mapped to a numbered
acceptance/business rule (e.g. `AT-08 / BR-01`) so a failing test names the rule it broke; the
appointment lifecycle is covered by `RescheduleAppointmentTests` and `CancelAppointmentTests`
alongside it.

To run one project only:

```bash
dotnet test tests/AppointmentScheduler.Application.Tests -c Release
```

## Manual API testing (Postman)

A Postman collection — [`ServiceScheduler Dev.postman_collection.json`](ServiceScheduler%20Dev.postman_collection.json) —
covers the booking slice end-to-end: the cookie/JWT auth flow, happy-path bookings (random and
fixed slots), reschedule/cancel lifecycle, the Issue #4 validation branches, and the conflict-handling
rules (half-open intervals, the DB `EXCLUDE` constraint, graceful retry on a race). Each request's
`description` explains the rule it exercises, and its test scripts assert the status code and body.

It's a companion to the `curl` smoke test above — use it for interactive, repeatable testing.

**Setup**

1. Start the stack and seed the dev database: `docker compose up -d` then
   `dotnet run --project src/Host/AppointmentScheduler.Api` (Development auto-migrates and seeds).
2. In **Postman desktop** (the web client needs the desktop agent to reach `localhost`), choose
   **Import** and select the collection file, plus the **`ServiceScheduler Dev` environment** file —
   the collection is driven entirely by environment variables (`baseUrl`, the dev credentials, and
   the seed GUIDs), so it does nothing without that environment.
3. Select **ServiceScheduler Dev** in the environment dropdown (top-right). Confirm `baseUrl` matches
   where the API is listening (`http://localhost:5080` by default).

**Run**

- **Auth is cookie-based**, so run **`Auth / Login as customer` first** — Postman's cookie jar stores
  the `httpOnly` access/refresh cookies for `baseUrl` and replays them on every later request, exactly
  like a browser. (`Login as admin` switches identity for the ownership-guard test.)
- Send individual requests, or use the **Collection Runner** on a whole folder — the requests are
  ordered so captured ids (`lastAppointmentId`) and pre-request scripts (unique future times per run)
  let you re-run without resetting the database.

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
| `docker-compose.yml` | Local dev PostgreSQL + Grafana LGTM observability stack |
| `ServiceScheduler Dev.postman_collection.json` | Postman smoke-test collection (see [Manual API testing](#manual-api-testing-postman)) |
| `ServiceScheduler Dev.postman_environment.json` | Postman environment — `baseUrl`, dev credentials, seed GUIDs (import alongside the collection) |
| `observability/` | Grafana dashboards-as-code (JSON) + provisioning config |
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

OpenTelemetry (traces + metrics + logs) is wired in `Program.cs` and exported over OTLP — logs are
stamped with the active trace/span id so they correlate with traces. The exporter honors the standard
`OTEL_EXPORTER_OTLP_*` env vars; point them at your own collector, or use the bundled stack.

`docker compose up -d` starts a **Grafana LGTM** backend (`grafana/otel-lgtm` — Loki + Grafana +
Tempo + Mimir with an OpenTelemetry Collector in front) alongside Postgres. It receives the API's
OTLP traces/metrics/logs on `4317` (gRPC) / `4318` (HTTP) — matching the API's default exporter
endpoint — and pre-provisions Grafana datasources so everything is queryable at
**`http://localhost:3000`** (login `admin`/`admin`).

**Dashboards-as-code.** Dashboard JSON under [`observability/dashboards/`](observability/dashboards/)
is committed to git and auto-loaded on startup (provisioning config in
[`observability/provisioning/`](observability/provisioning/)), so `docker compose down -v && up -d`
recreates every dashboard from source. Provisioned dashboards are read-only in the UI — the repo is
the source of truth. Shipped out of the box:

- **AppointmentScheduler.Api — Golden Signals**: request rate, 5xx error rate, and p95 latency by
  route; active requests; runtime (GC pause rate, working set); and DB signals (connection pool, p95
  query duration and query rate by operation).

The LGTM stack is **local-dev only** — no auth on OTLP ingest, single-node, non-HA.

Health checks: `/health/live` (liveness), `/health/ready` (readiness, checks the database),
`/health` (liveness alias).

## AI Collaboration Narrative

I built this project with **Claude (Anthropic)** as a collaborator across the whole lifecycle, but I
stayed the decision-maker and treated **automated checks as the objective backstop** — no AI output
was accepted on trust. The detailed design-phase account is in
[`system_design/DESIGN.md` §5](system_design/DESIGN.md#5-how-generative-ai-assisted-the-design-phase);
this section describes how I guided the AI, how I verified its output, and how I kept the final code
honest.

### High-level strategy for guiding the AI

I ran the work as a **staged pipeline**, where each stage is a **dedicated AI skill I invoke with a
slash command**. Every stage has a focused prompt, a defined input/output, and a clean hand-off to
the next — so the AI is always working against an agreed artifact instead of improvising:

```
ADR → PRD → Issues → Plan → Implement (TDD) → Review
```

**I match the model to the stakes of each stage** — the upstream stages shape everything downstream,
so they get the strongest model and my closest review; the mechanical stages run a cheaper model:

| Stage | Skill (slash command) | Model | My attention |
|-------|----------------------|-------|--------------|
| **ADR** | design / ADR skill | strongest | high |
| **PRD** | `/to-prd` | **strongest** | **highest — I review this very carefully** |
| **Plan** | `/plan-issue` | **strongest** | **highest — I review this very carefully** |
| **Issues** | `/to-issues` | medium | normal |
| **Implement (TDD)** | `/implement-issue` (drives TDD itself), `/tdd` | medium | normal |
| **Review** | `/code-review` | medium, run by a **separate AI / fresh context window** | independent pass |

Two deliberate choices sit behind that table:

- **PRD and Plan are where I invest the most.** An error there is cheapest to catch and most
  expensive to let through, so those two stages get the strongest model *and* a careful manual review
  before anything downstream is generated from them. Once the plan is solid, the mechanical stages
  (issues, implementation) can safely run a medium model. The `/implement-issue` skill **drives TDD
  itself** — it works through the plan section by section red-green-refactor, so implementation and
  test-first are the same stage, not two.
- **Review is run by a different AI — or an empty context window.** The reviewer must not be biased
  by the same context that produced the code, so I give the review stage a fresh context (and it's
  fine on a medium model) to get a genuinely independent read.

Two more principles keep the AI honest throughout:

- **Small, vertical slices.** Features were scoped one request/handler at a time (vertical-slice
  CQRS), keeping each AI-generated change small enough to review and test in isolation.
- **The architecture is executable, not advisory.** Boundary rules are encoded as a **NetArchTest**
  suite + the `ProjectReference` graph, so a violation *fails the build* instead of relying on review
  discipline.

### Verifying and refining the AI's output

- **Every change is gated by the toolchain.** Before a change was committed it had to pass the
  compiler with **warnings-as-errors**, the architecture tests, and the full unit/integration suites.
- **Test-first on the core logic.** Business rules are pinned by tests (each `RequestAppointment`
  case maps to a numbered acceptance/business rule, e.g. `AT-08 / BR-01`), so a failing test names
  the rule it broke and refactors stay honest.
- **The dedicated review stage is the refinement loop.** `/code-review` on a fresh context surfaced
  real issues — duplicated guard clauses (consolidated into a shared `Guard` helper), an unclassified
  domain type, and documentation describing an events/outbox system not yet in code — which fed
  straight back into fixes and the roadmap.
- **Dialogue to sharpen the hard parts.** The concurrency strategy ("read availability → let Postgres
  arbitrate via `EXCLUDE` constraints → retry once") was refined in back-and-forth that surfaced the
  race between the availability read and the insert, settling on a DB-enforced invariant over
  application-level locking.

### Ensuring final code quality

- **Objective backstops over subjective trust:** compiler + warnings-as-errors, NetArchTest module
  boundaries, and the three-tier test suite (core logic / integration / architecture) — see [Test](#test).
- **An independent review gate.** The final `/code-review` pass runs on a separate AI / fresh context
  specifically so quality isn't judged by the model that wrote the code.
- **Honest gaps.** Where docs and code diverged (events/outbox is designed in
  [ADR-0002](docs/adrs/0002-events-for-inter-module-communication.md) but not yet implemented), the
  divergence is called out in the [roadmap](system_design/DESIGN.md#roadmap--known-gaps) rather than
  papered over.
- **Reasoning preserved.** ADRs, PRDs, and per-issue plans capture my reasoning at each stage, so the
  design intent is auditable regardless of how any individual line was produced.

## More documentation

See [`CLAUDE.md`](CLAUDE.md) for detailed architecture notes and conventions, and [`docs/`](docs/)
for ADRs, design specs, and per-issue plans/PRDs. The
[design doc](system_design/DESIGN.md) covers the design rationale, the observability strategy, and how
generative AI (Claude) was used as a collaborator during the design phase.
