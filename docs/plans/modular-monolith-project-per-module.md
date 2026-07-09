# Plan: Restructure to a project-per-module modular monolith

> **Standalone**: this plan is executable without reading any other file.
> **Status**: in progress on branch `feat/clean-up`.

## Goal

Turn the current *convention-only* modular monolith (bounded contexts as **folders inside 4 shared
projects** — `Domain`, `Application`, `Infrastructure`, `Api`) into a **project-per-module** layout
where module boundaries are **enforced by the compiler** through the `ProjectReference` graph. This
closes the gap ADR-0001 flagged as an overdue follow-up ("add an architecture-test suite once >2
modules exist") and makes future extraction to microservices a *lift*, not a rewrite (per ADR-0004).

Keep **one shared `AppDbContext`** and one migration history for now, but split EF configurations
into the module projects and give each module its **own Postgres schema** so table ownership is
explicit.

## Forced design change: provider-owned contracts (amends ADR-0001 rule #2)

Once modules are separate projects, a *consumer-owned* query port (`IServiceBayLookup` living in
Booking, implemented by Fleet) would force **Fleet → Booking** — itself a forbidden cross-module
reference. So each cross-module query port + its DTOs move to the **provider's** `Contracts`
project; consumers reference only that `Contracts` project, never the implementation. This is
idiomatic for the target and improves alignment with ADR-0004 (interface unchanged on extraction;
only the implementation swaps EF→HTTP).

| Port (today in `Application/Abstractions`) | New home |
|---|---|
| `IServiceTypeLookup` (+ DTOs) | `Catalog.Contracts` |
| `IServiceBayLookup`, `IVehicleOwnershipQuery` (+ DTOs) | `Fleet.Contracts` |
| `IQualifiedTechnicianLookup` (+ DTOs) | `Workforce.Contracts` |
| `IAppointmentRepository` | stays inside Booking (module-internal) |
| `ICurrentUser` | `BuildingBlocks` (cross-cutting) |

## Target structure

```
src/
├─ Host/AppointmentScheduler.Api/                    # only executable
├─ BuildingBlocks/
│  ├─ AppointmentScheduler.BuildingBlocks/           # Messaging (mediator) + ICurrentUser
│  └─ AppointmentScheduler.BuildingBlocks.Persistence/# AppDbContext, AppUser, RefreshToken, Migrations
└─ Modules/
   ├─ Booking/    { AppointmentScheduler.Booking, .Booking.Contracts }
   ├─ Fleet/      { AppointmentScheduler.Fleet, .Fleet.Contracts }
   ├─ Workforce/  { AppointmentScheduler.Workforce, .Workforce.Contracts }
   └─ Catalog/    { AppointmentScheduler.Catalog, .Catalog.Contracts }
tests/
├─ AppointmentScheduler.Application.Tests            # retargeted
├─ AppointmentScheduler.Api.Tests
└─ AppointmentScheduler.ArchitectureTests            # NEW (NetArchTest)
```

Reference graph (acyclic): `BuildingBlocks*` ← `*.Contracts` ← `<Module>` ← `Host`. A module
references its own `Contracts`, `BuildingBlocks`, `BuildingBlocks.Persistence`, and other modules'
`Contracts` only. `.Contracts` projects are near-empty today (events are docs-only) and become the
home for published event records later.

## Shared AppDbContext redesign

1. Move `AppDbContext`, `AppUser`, `RefreshToken`, `RefreshTokenConfiguration` into
   `BuildingBlocks.Persistence` (references EF Core + Identity + Npgsql only — no module).
2. Drop the per-module typed `DbSet` properties; repositories switch to `db.Set<T>()`.
3. `OnModelCreating` applies Identity config + configs from a **runtime-supplied list of module
   assemblies** (passed by the Host) instead of `Assembly.GetExecutingAssembly()`.
4. Each module's EF config sets its schema: `booking`, `fleet`, `workforce`, `catalog`;
   Identity/refresh stay in `public`.

## Step sequence (green build + tests between each)

0. Baseline `dotnet build` + `dotnet test` green. ✔ (17 unit + 20 integration)
1. Scaffold `src/`, `tests/…ArchitectureTests`, empty `.csproj`s, wire references, update `.sln`.
2. BuildingBlocks: move Messaging + `ICurrentUser`; move persistence stack with `Set<T>()` +
   assembly-scan changes.
3. Per module (Catalog → Fleet → Workforce → Booking): move Domain/Application/Infrastructure
   folders + cross-module ports→Contracts; set schemas; build after each.
4. Host: move `Program.cs`, `Endpoints/*`, `Security/*`, `DbInitializer`; split DI into per-module
   `Add<Module>Module` extensions; register module assemblies with the DbContext.
5. Tests: retarget references; add NetArchTest boundary rules.
6. Migration: move/regenerate migrations to `BuildingBlocks.Persistence/Migrations/`; verify
   `dotnet ef database update` on a fresh DB + Dev auto-migrate/seed.
7. Docs: amend ADR-0001 rule #2, add ADR-0006, update CLAUDE.md (layout, migration path, ports).

## Risks

- **R1** Migration/schema move (highest) — verify overlap `EXCLUDE USING gist` constraints (#6)
  survive; regenerate against a throwaway DB.
- **R2** `AppointmentRepository` hardcodes constraint names from migration #6 — keep in sync.
- **R3** Large churn — module-by-module with a green build between each localizes breakage.
- **R4** Keep existing `AppointmentScheduler.<Layer>.<Module>` namespaces (folders move, namespaces
  stay) to minimize churn.

## Open question (before step 6)

Does any environment hold data that must survive the schema move, or is regenerating migrations
against a fresh DB acceptable?

## Verification

- `dotnet build` clean; NetArchTest suite passes (proves boundaries).
- `dotnet test` green at the baseline count.
- `dotnet ef database update` on fresh Postgres; tables land in `booking`/`fleet`/`workforce`/
  `catalog` schemas.
- Dev run: auto-migrate/seed works; `POST /api/appointments` happy path + overlap-conflict path
  still succeed (`BookingEndpointsTests`).
