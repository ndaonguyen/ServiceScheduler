# ADR-0002: Events for inter-module communication

- **Status**: Accepted
- **Date**: 2026-07-07
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0001](0001-modular-monolith.md)

## Context

[ADR-0001](0001-modular-monolith.md) commits us to a modular monolith where each module owns a
bounded context and is extractable to a microservice later without a rewrite. That promise
depends on **how modules talk to each other today** — because whatever we pick becomes the
contract that has to survive the split.

Two families of options exist for cross-module communication:

- **Synchronous** — one module calls another's code or query port and waits for the response.
  Necessary when the caller genuinely needs the answer to continue (e.g. Booking asking Fleet
  "is this vehicle owned by this customer?" before accepting a request).
- **Asynchronous** — one module publishes an event; interested modules react on their own
  schedule (e.g. Booking says "AppointmentConfirmed"; Notifications sends an email, Analytics
  logs a metric).

Synchronous calls at scale become a **distributed monolith** — every service call on the hot
path is a coupling point that can't fail independently. Asynchronous events preserve module
autonomy but introduce eventual consistency.

## Decision

We will use **domain events** as the default for inter-module communication. Synchronous query
ports are permitted only when the caller must have the answer before proceeding (read-only
reads across module boundaries) — never for state changes.

> **Implementation status:** the mechanism below is the **target design**, not code that exists
> today. The first slice ([PRD — Appointment Booking](../prds/appointment-booking.md) AC-05)
> publishes no domain events, so the `IEventPublisher` port, `outbox` table, and dispatcher have
> not been built yet. They land together with the **first cross-module event consumer** — expected
> to be the Notifications module (see [`../roadmap.md`](../roadmap.md) and this ADR's Follow-ups).
> Building them speculatively — before a consumer exists to prove the dual-write-safe path
> end-to-end — would be unverifiable infrastructure and is explicitly declined.

**Mechanism (target):**

- Events are **plain C# records** placed in `Application/Features/<Module>/Events/`, e.g.
  `AppointmentConfirmed(Guid AppointmentId, Guid CustomerId, Instant At)`.
- Publishing goes through an **`IEventPublisher`** port defined in `Application/Abstractions/`.
  Handlers implement **`IEventHandler<TEvent>`** — one class per (event, handler) pair,
  discoverable by DI registration.
- **In-process dispatcher today.** Infrastructure provides an implementation that:
  1. Records the event to an **outbox table** in the same DB transaction as the state change
     that produced it (transactional outbox pattern).
  2. After the transaction commits, dispatches to registered handlers **out of band** — a
     handler failure doesn't roll back the originating operation.
  3. Retries on transient failures; sends to a poison queue after N attempts.
- **Extraction path.** When a module leaves for its own service, the in-process dispatcher is
  swapped for a message-bus transport (RabbitMQ / Kafka / Azure Service Bus). Event **records
  remain the contract** — publishers and handlers see no change. The outbox table becomes a
  relay that publishes to the bus.

**What events are, and are not:**

- Events describe **facts that already happened**, past tense: `AppointmentConfirmed`,
  `TechnicianAssigned`. Never commands (`ConfirmAppointment`) — commands stay intra-module.
- Events are **owned by the publishing module**. Consumers subscribe; publishers don't know
  who listens. Adding a new consumer never requires changing the publisher.
- Events carry **IDs and small immutable facts**, not full aggregates. Consumers use IDs to
  read fresh data from the owning module's read port if they need more.

**When synchronous is still allowed:**

- Read-only cross-module queries where the caller must have the answer to continue (e.g. "does
  this technician exist and hold the required skill?" during availability check). These use
  Application ports in `Application/Abstractions/`, implemented in the owning module's
  Infrastructure. Never for writes; never chained more than one deep.

## Alternatives Considered

- **Direct method calls between modules** — rejected: compile-time coupling that has to be
  torn out on extraction. Kills the ADR-0001 promise.
- **Shared services layer accessed by all modules** — rejected: reintroduces the god-object
  pattern the modular split exists to avoid.
- **Message bus (RabbitMQ / Kafka) from day 1** — rejected: real infra to run, deploy, monitor,
  and simulate in tests, for zero benefit while we're still one process. Adopt it at
  extraction time, not before.
- **MediatR pipeline as the event mechanism** — rejected: we already run a custom
  in-process mediator (`Application/Messaging/`) with no MediatR dependency; extending it is
  cheaper than pulling in a library, and keeps the code review surface small.

## Consequences

- **Positive:**
  - Modules decouple today; extraction later is a **transport swap**, not a redesign.
  - Cross-cutting concerns (notifications, audit, analytics) plug in as event handlers with
    zero change to the producing module.
  - The outbox pattern makes cross-module effects **at-least-once** even inside the monolith,
    which is the same guarantee we'll have on a bus — no behavioural surprise at extraction.
- **Negative:**
  - Eventual consistency across modules. Transactional guarantees hold only inside the module
    that owns the aggregate. Cross-module workflows need explicit compensation or retries.
  - Debugging fan-out is harder than a call stack. Mitigated by OpenTelemetry spans on the
    dispatcher — each dispatch is a child span of the originating request.
  - Requires the outbox table and a background dispatcher — small infra tax now.
- **Follow-ups:**
  - Implement `IEventPublisher` + `IEventHandler<T>` + the outbox table + the post-commit
    dispatcher as part of the first module that needs cross-module effects.
  - Document the outbox schema and dispatcher lifecycle in `docs/database.md` when it lands.
  - Revisit when we introduce a bus: this ADR is expected to be **amended, not superseded** —
    the abstraction is the same; only the transport changes.
