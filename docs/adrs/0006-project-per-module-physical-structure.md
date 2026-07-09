# ADR-0006: Project-per-module physical structure, module schemas, provider-owned contracts

- **Status**: Accepted
- **Date**: 2026-07-09
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0001](0001-modular-monolith.md) (refines rule #2 and the physical layout), [ADR-0002](0002-events-for-inter-module-communication.md), [ADR-0004](0004-inter-service-communication-strategy.md), [ADR-0005](0005-postgresql-over-document-database.md)

## Context

[ADR-0001](0001-modular-monolith.md) committed us to a modular monolith with module boundaries, but
implemented them as **folders inside four shared projects** (`Domain`, `Application`,
`Infrastructure`, `Api`). Those boundaries were **convention only** — nothing stopped a Booking
handler from writing `using AppointmentScheduler.Domain.Fleet;`; only code review caught it.
ADR-0001 itself flagged the follow-up: *"once >2 modules exist, an architecture-test suite … that
fails the build on cross-module references."* Four modules now exist (Booking, Fleet, Workforce,
Catalog) and the guardrail was overdue.

The goal — "each module behaves like an in-process microservice, extractable without a rewrite"
(ADR-0001, ADR-0004) — is materially advanced by making boundaries **enforced by the compiler**
through the `ProjectReference` graph rather than by convention.

## Decision

We restructure the codebase into **one class-library project per module** with the boundary carried
by project references:

```
src/
├─ Host/AppointmentScheduler.Api/                      # the only executable
├─ BuildingBlocks/
│  ├─ AppointmentScheduler.BuildingBlocks/             # the mediator + cross-cutting ports (ICurrentUser)
│  └─ AppointmentScheduler.BuildingBlocks.Persistence/ # shared AppDbContext, Identity, refresh tokens, migrations
└─ Modules/<Module>/
   ├─ AppointmentScheduler.<Module>/                   # Domain/ Application/ Infrastructure/ folders
   └─ AppointmentScheduler.<Module>.Contracts/         # the module's public surface for other modules
```

Reference graph (acyclic): `BuildingBlocks*` ← `*.Contracts` ← `<Module>` ← `Host`. A module
references its own `Contracts`, the `BuildingBlocks`, and **other modules' `Contracts` only** —
never another module's implementation. A **NetArchTest** suite
(`tests/AppointmentScheduler.ArchitectureTests`) fails the build on a violation.

Three sub-decisions:

1. **Contracts are provider-owned** (this *refines* [ADR-0001](0001-modular-monolith.md) rule #2).
   ADR-0001 said the cross-module query port "lives with the consumer." Physical separation makes
   that impossible: a consumer-owned port (`IServiceBayLookup` in Booking, implemented by Fleet)
   forces **Fleet → Booking**, itself a forbidden cross-module reference and a cycle risk. So each
   cross-module query port and its DTOs move to the **provider's** `Contracts` project; consumers
   depend on that contract, never the implementation. This is strictly better aligned with
   [ADR-0004](0004-inter-service-communication-strategy.md): on extraction the query-port interface
   is unchanged and only the implementation swaps from EF query to HTTP client. A module-internal
   port (e.g. Booking's `IAppointmentRepository`) stays inside its module.

2. **One shared `AppDbContext`, per-module Postgres schemas.** The database stays shared for now
   (per ADR-0001), but each module owns a Postgres schema (`booking`, `fleet`, `workforce`,
   `catalog`; Identity and refresh tokens stay in `public`). The context lives in
   `BuildingBlocks.Persistence` and references **no module**: module aggregates are reached via
   `Set<T>()` and each module contributes its `IEntityTypeConfiguration<>` mappings, discovered by
   scanning the assemblies named in a host-supplied `ModuleConfigurations`. When a module is
   extracted, its schema's tables move with it.

3. **Namespaces are preserved** from the pre-restructure layout
   (`AppointmentScheduler.<Layer>.<Module>`). Boundary enforcement comes from the reference graph
   and the arch tests, not from namespaces, so this was a physical *lift* with near-zero churn to
   `using` statements. A namespace tidy-up (to `AppointmentScheduler.<Module>.<Layer>`) is a
   possible cosmetic follow-up, not a correctness requirement.

## Alternatives Considered

- **Keep the four shared projects, add only NetArchTest.** Rejected as the primary approach: it
  adds the guardrail ADR-0001 wanted but leaves boundaries un-enforced by the compiler and does not
  advance extractability. (The arch tests were kept anyway, as a backstop.)
- **Project-per-layer-per-module** (`Booking.Domain`, `Booking.Application`, …; ~20 projects).
  Rejected: maximal isolation but the largest refactor and the most per-feature ceremony, for
  isolation the single-project-per-module already delivers via internal folders.
- **A `DbContext` per module now.** Rejected for now: closest to real service isolation but a
  significant Identity/migration untangle before any extraction pressure exists. Deferred; the
  shared context with per-module schemas is the intermediate step.
- **Regenerate the migration history for the schema move.** Rejected: unnecessary. Changing a
  table's schema is a non-destructive `ALTER TABLE … SET SCHEMA` (`RenameTable` with `newSchema`),
  so a single additive `AddModuleSchemas` migration preserves data, indexes, and the
  `EXCLUDE USING gist` overlap constraints (verified against Postgres).

## Consequences

- **Positive:**
  - Cross-module coupling is now a **compile error**, backed by an arch-test suite that fails CI.
  - Extraction is closer to a lift: a module + its Postgres schema + its `Contracts` move as a unit;
    only the `Contracts` implementation swaps EF→HTTP (ADR-0004).
  - Table ownership is explicit at the schema level.
- **Negative:**
  - More projects (~14) — more `.csproj` to maintain and a longer solution.
  - `BuildingBlocks.Persistence` remains a point all modules share (the shared-DB trade-off from
    ADR-0001 persists until a module needs its own store).
  - Namespaces no longer strictly mirror their project/folder (the preserved-namespace trade-off).
- **Follow-ups:**
  - The `*.Contracts` projects are near-empty today; they become the home for the published event
    records when [ADR-0002](0002-events-for-inter-module-communication.md)'s outbox is implemented.
  - Revisit `DbContext`-per-module (and true schema separation) when a module's extraction is
    actually scheduled.
