# AppointmentScheduler

A backend .NET service for scheduling vehicle service appointments across dealerships,
technicians, and service bays. Clean Architecture with vertical-slice CQRS over a lightweight
in-process mediator (no MediatR), JWT + cookie auth with RBAC, EF Core + PostgreSQL, OpenTelemetry
observability, and health checks. No frontend — the API is the product; `/openapi/v1.json` is the
client contract.

## Features

- **Clean Architecture** — Domain / Application / Infrastructure / Api projects, one per layer.
- **CQRS** via a lightweight in-process mediator (`AppointmentScheduler.Application/Messaging`) — no MediatR.
- **Auth** — JWT access + refresh tokens, transported as `httpOnly` cookies only, RBAC via
  ASP.NET Core Identity. See [`docs/authentication.md`](docs/authentication.md).
- **Persistence** — EF Core + PostgreSQL (`Npgsql`); schema owned by EF Core migrations.
- **Observability** — OpenTelemetry traces + metrics over OTLP; split liveness/readiness health
  checks (`/health/live`, `/health/ready`).
- **CI/CD** — GitHub Actions build/test/coverage on every push/PR, GitVersion-based versioning,
  and a deploy pipeline skeleton (build → migrate → deploy).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/) (pinned by `global.json`, prerelease allowed)
- [Docker](https://www.docker.com/) (for local PostgreSQL via `docker-compose.yml`)

## Getting started

```bash
dotnet restore && dotnet tool restore

docker compose up -d                                   # start local Postgres (EF creates the schema)
dotnet run --project source/AppointmentScheduler.Api   # serves /health, /openapi/v1.json, and API endpoints
                                                       # (in Development, auto-applies EF migrations + seeds)
```

`/` redirects to `/openapi/v1.json` — use that as the API contract for any client or
`curl`-based test harness.

### Build & test

```bash
dotnet build -c Release
dotnet test  -c Release
```

## Project layout

| Path | Purpose |
|------|---------|
| `source/AppointmentScheduler.Domain/` | Entities, value objects, domain rules |
| `source/AppointmentScheduler.Application/` | CQRS handlers, ports, mediator |
| `source/AppointmentScheduler.Infrastructure/` | EF Core, repositories, Identity, migrations |
| `source/AppointmentScheduler.Api/` | Minimal-API endpoints, security wiring, `Program.cs` |
| `tests/AppointmentScheduler.Application.Tests/` | Handler unit tests (xUnit + AwesomeAssertions) |
| `tests/AppointmentScheduler.Api.Tests/` | Integration tests over `WebApplicationFactory` |
| `AppointmentScheduler.sln` | Solution file |
| `Directory.Build.props` | Shared MSBuild settings |
| `global.json` | Pins .NET SDK 10 (prerelease) |
| `GitVersion.yml` | Versioning config (GitVersion 6, GitHubFlow) |
| `.config/dotnet-tools.json` | Local dotnet tool manifest |
| `docker-compose.yml` | Local dev PostgreSQL |
| `Dockerfile` | Multi-stage container build |
| `.github/workflows/` | CI (build/test/coverage), PR title lint, deploy pipeline |
| `.claude/skills/` | Reusable Claude Code workflows |
| `docs/` | Reference docs (`authentication.md`, `database.md`) + artifacts (`prds/`, `plans/`, `adrs/`, `inputs/`) |

## Database migrations

Schema is owned by EF Core migrations (`source/AppointmentScheduler.Infrastructure/Migrations`).

```bash
# add a migration after changing entities/DbContext
dotnet ef migrations add Describe_change \
  --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api

# apply pending migrations manually (Development does this automatically on startup)
export AppDb__ConnectionString="Host=localhost;Port=5432;Database=appointmentscheduler;Username=appointmentscheduler;Password=appointmentscheduler"
dotnet ef database update \
  --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
```

In production, migrations run as a deliberate deploy step (see `.github/workflows/deploy.yaml`),
never on startup.

## Authentication

JWT access (15 min) and refresh (7-day) tokens, both transported as `httpOnly`, `Secure`,
`SameSite=Strict` cookies — never exposed to JavaScript. See
[`docs/authentication.md`](docs/authentication.md) for the full design.

```bash
curl -X POST "http://localhost:5080/api/auth/register" -H "Content-Type: application/json" \
  -d '{"email":"me@x.com","password":"Passw0rd!$"}'

curl -c cookies.txt -X POST "http://localhost:5080/api/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"me@x.com","password":"Passw0rd!$"}'

curl -b cookies.txt http://localhost:5080/api/profile/me      # 200; current user + roles
curl -b cookies.txt -c cookies.txt -X POST "http://localhost:5080/api/auth/refresh"
```

## Observability

OpenTelemetry (traces + metrics) is wired up in `Program.cs` and exported over OTLP — point
`OTEL_EXPORTER_OTLP_*` env vars at a collector per environment. Health checks:

- `/health/live` — liveness, no dependencies
- `/health/ready` — readiness, checks the database
- `/health` — liveness alias

## CI/CD

- **`ci.yaml`** — on push/PR to `main`: GitVersion, restore, build, test + coverage.
- **`pr-lint.yml`** — validates PR titles follow Conventional Commits.
- **`deploy.yaml`** — manual (`workflow_dispatch`) pipeline: build & push a GitVersion-tagged
  image to GHCR, run `dotnet ef database update`, then deploy (placeholder — fill in your target).

## More documentation

See [`CLAUDE.md`](CLAUDE.md) for detailed architecture notes and conventions, and
[`docs/`](docs/) for design specs and per-issue plans/PRDs.
