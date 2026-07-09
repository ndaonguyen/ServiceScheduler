# ADR-0001: Modular monolith

- **Status**: Accepted
- **Date**: 2026-07-07
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0002](0002-events-for-inter-module-communication.md), [ADR-0006](0006-project-per-module-physical-structure.md) (refines the physical layout and rule #2)

## Context

The service spans several bounded contexts — Booking (appointments, availability), Fleet
(vehicles, dealerships), Workforce (technicians, skills), and Catalog (service types). Splitting
these into independent microservices from day 1 would introduce distributed-system tax
(N deploy pipelines, service discovery, distributed tracing, cross-service transactions) that
the team size, load profile, and product certainty don't yet justify.

At the same time, a conventional layered monolith with free-form intra-project references makes
future extraction expensive: when scale or ownership pressure eventually forces a split, we're
paying to untangle coupling rather than to lift a module out.

## Decision

> **Refined by [ADR-0006](0006-project-per-module-physical-structure.md) (2026-07-09):** the module
> boundaries below are now enforced by the compiler via a **project-per-module** layout under `src/`
> (not folders inside four shared projects), and rule #2's cross-module query ports are
> **provider-owned** (in each module's `Contracts` project) rather than consumer-owned — physical
> separation makes a consumer-owned port a forbidden provider→consumer reference. The core decision
> (modular monolith, liftable without a rewrite) is unchanged. Paths below reflect the original
> layout; see ADR-0006 for the current one.

We will build ServiceScheduler as a **modular monolith** — a single deployable ASP.NET Core
process organised into **feature modules** with enforced boundaries. A "module" is a bounded
context (Booking, Fleet, Workforce, Catalog, …); each module owns its full vertical slice:

- **Domain aggregates** under `source/AppointmentScheduler.Domain/<Module>/`
- **Application handlers, ports, and events** under `source/AppointmentScheduler.Application/Features/<Module>/`
- **EF configurations & repositories** under `source/AppointmentScheduler.Infrastructure/<Module>/`
  (persistence) and `Persistence/Configurations/<Aggregate>Configuration.cs`
- **Api endpoint group** under `source/AppointmentScheduler.Api/Endpoints/<Module>Endpoints.cs`

**Module boundary rules:**

1. A module **never references another module's Domain or Infrastructure types** directly. No
   `using AppointmentScheduler.Domain.Fleet` from the Booking module's handler code.
2. When one module needs another's data or behaviour, it goes through:
   - a **shared read model / query port** defined in `Application/Abstractions/` (the port
     lives with the consumer; the implementation lives in the owning module's Infrastructure),
     **or**
   - an **event** published by the owning module (see [ADR-0002](0002-events-for-inter-module-communication.md)).
3. Every module can be extracted into its own service by moving its folders and swapping
   the in-process event dispatcher for a message-bus transport. **No code rewrite required.**

The database is still shared today, but table ownership is tracked per module. When a module
is extracted, its tables move with it.

## Alternatives Considered

- **Microservices from day 1** — rejected: premature. Deploy-pipeline multiplication, network
  latency between hot paths (availability query touches Fleet + Workforce + Catalog), and
  distributed transactions for booking outweigh scaling headroom we don't yet need.
- **Traditional layered monolith (no module boundaries)** — rejected: cheaper today, but every
  future feature makes extraction more expensive. We would pay the coupling cost twice —
  once during the monolith years, then again in a rewrite.
- **Distributed monolith (services + shared DB)** — rejected: worst of both worlds — the
  operational cost of microservices with none of the isolation.

## Consequences

- **Positive:**
  - Single deploy target, single database — booking stays transactionally consistent.
  - Fast to build now; team can focus on domain, not infra.
  - Extraction later is a **lift**, not a rewrite: the module folder + its tables + its event
    contracts move as a unit.
  - Clear ownership boundaries make code review and onboarding easier from day 1.
- **Negative:**
  - Boundaries are convention, not runtime-enforced inside a single process. Requires
    discipline in code review — and, once >2 modules exist, an architecture-test suite
    (e.g. NetArchTest) that fails the build on cross-module references.
  - Shared DB means module schemas can accidentally leak into each other's queries. Mitigated
    by port-based access only.
- **Follow-ups:**
  - [ADR-0002](0002-events-for-inter-module-communication.md) codifies the event mechanism.
  - Add an architecture test project when the second module lands.
  - Reassess this ADR when any of: team size >10 engineers, deploy cadence per module
    diverges materially, or a module's scaling profile forces it out.
