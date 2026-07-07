# dotnet-template

A **Clean-Architecture .NET service** template, packaged as a `dotnet new` template. It ships
vertical-slice CQRS over a lightweight in-process mediator (no MediatR), JWT auth with
role-based access control, EF Core + PostgreSQL persistence, an optional React + Vite SPA,
OpenTelemetry observability, health checks, and a GitHub Actions CI/CD pipeline.

## Features

- **Clean Architecture** — Domain / Application / Infrastructure / Api projects, one per layer.
- **CQRS** via a lightweight in-process mediator (`ServiceScheduler.Application/Messaging`) — no MediatR.
- **Auth** — JWT access + refresh tokens, transported as `httpOnly` cookies only, RBAC via
  ASP.NET Core Identity. See [`docs/authentication.md`](docs/authentication.md).
- **Persistence** — EF Core + PostgreSQL (`Npgsql`); schema owned by EF Core migrations.
- **Optional React + Vite SPA** — `source/ServiceScheduler.Api/ClientApp`, toggled via template parameter.
- **Observability** — OpenTelemetry traces + metrics over OTLP; split liveness/readiness health
  checks (`/health/live`, `/health/ready`).
- **CI/CD** — GitHub Actions build/test/coverage on every push/PR, GitVersion-based versioning,
  and a deploy pipeline skeleton (build → migrate → deploy).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/) (pinned by `global.json`, prerelease allowed)
- [Docker](https://www.docker.com/) (for local PostgreSQL via `docker-compose.yml`)
- [Node.js](https://nodejs.org/) (only if using the React client)

## Getting started

```bash
dotnet restore && dotnet tool restore

docker compose up -d                      # start local Postgres (empty; EF creates the schema)
dotnet run --project source/ServiceScheduler.Api    # serves /health, /api/widgets, /openapi/v1.json
                                          #   (in Development, auto-applies EF migrations + seeds)
```

### Build & test

```bash
dotnet build -c Release
dotnet test  -c Release
```

## Run (backend + frontend)

### Dev mode — hot reload, two terminals

Vite serves the SPA and proxies API calls to the backend, so the browser sees a single origin.

```bash
# once: trust the local HTTPS cert
dotnet dev-certs https --trust
```

**Terminal 1 — backend** (https://localhost:7443, http://localhost:5080):
```bash
dotnet run --project source/ServiceScheduler.Api --launch-profile https
#  → /health, /api/widgets, /openapi/v1.json
```

**Terminal 2 — frontend** (http://localhost:5173):
```bash
cd source/ServiceScheduler.Api/ClientApp
npm install        # first time only
npm run dev
```

Open **http://localhost:5173** → the **Widgets** page creates/lists widgets through the API.
Vite proxies `/api`, `/openapi`, `/health` to the backend (see `vite.config.ts`; override
the target with `VITE_API_PROXY`).

### Single process — prod-like (one port, no Vite)

`dotnet publish` builds the React app and the API serves it from `wwwroot`:

```bash
dotnet publish source/ServiceScheduler.Api -c Release -o ./publish   # runs npm build → wwwroot
dotnet ./publish/ServiceScheduler.Api.dll                            # SPA + API on one port
```

> **API only** (`--client-framework none`): no ClientApp — just run
> `dotnet run --project source/ServiceScheduler.Api`; `/` redirects to `/openapi/v1.json`.

## Project layout

| Path | Purpose |
|------|---------|
| `source/<Name>/` | Application & library projects: Domain / Application / Infrastructure / Api |
| `source/ServiceScheduler.Api/ClientApp/` | React + Vite SPA (excluded when `--client-framework none`) |
| `tests/<Name>.Tests/` | Test projects (xUnit + AwesomeAssertions) |
| `ServiceScheduler.sln` | Solution file |
| `Directory.Build.props` | Shared MSBuild settings |
| `global.json` | Pins .NET SDK 10 (prerelease) |
| `GitVersion.yml` | Versioning config (GitVersion 6, GitHubFlow) |
| `.config/dotnet-tools.json` | Local dotnet tool manifest |
| `docker-compose.yml` | Local dev PostgreSQL |
| `Dockerfile` | Multi-stage container build |
| `.github/workflows/` | CI (build/test/coverage), PR title lint, deploy pipeline |
| `.template.config/` | `dotnet new` template metadata |
| `.claude/skills/` | Reusable Claude Code workflows |
| `docs/` | Reference docs (`authentication.md`, `database.md`) + artifacts (`prds/`, `plans/`, `adrs/`, `inputs/`) |

## Database migrations

Schema is owned by EF Core migrations (`source/ServiceScheduler.Infrastructure/Migrations`).

```bash
# add a migration after changing entities/DbContext
dotnet ef migrations add Describe_change \
  --project source/ServiceScheduler.Infrastructure --startup-project source/ServiceScheduler.Api

# apply pending migrations manually (Development does this automatically on startup)
export AppDb__ConnectionString="Host=localhost;Port=5432;Database=servicescheduler;Username=servicescheduler;Password=servicescheduler"
dotnet ef database update \
  --project source/ServiceScheduler.Infrastructure --startup-project source/ServiceScheduler.Api
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

curl -b cookies.txt http://localhost:5080/api/widgets      # 200; POST /api/widgets needs "admin" role
curl -b cookies.txt http://localhost:5080/api/profile/me   # 200; current user + roles
curl -b cookies.txt -c cookies.txt -X POST "http://localhost:5080/api/auth/refresh"
```

## Observability

OpenTelemetry (traces + metrics) is wired up in `Program.cs` and exported over OTLP — point
`OTEL_EXPORTER_OTLP_*` env vars at a collector per environment. Health checks:

- `/health/live` — liveness, no dependencies
- `/health/ready` — readiness, checks the database
- `/health` — liveness alias

## Using this as a `dotnet new` template

```bash
dotnet new install .

dotnet new ai-service -n PaymentsApi                          # React SPA (default)
dotnet new ai-service -n PaymentsApi --client-framework none  # Web API only
```

`ServiceScheduler` is renamed to your chosen project name throughout.

## CI/CD

- **`ci.yaml`** — on push/PR to `main`: GitVersion, restore, build, test + coverage.
- **`pr-lint.yml`** — validates PR titles follow Conventional Commits.
- **`deploy.yaml`** — manual (`workflow_dispatch`) pipeline: build & push a GitVersion-tagged
  image to GHCR, run `dotnet ef database update`, then deploy (placeholder — fill in your target).

## More documentation

See [`CLAUDE.md`](CLAUDE.md) for detailed architecture notes and conventions, and
[`docs/`](docs/) for design specs and per-issue plans/PRDs.
