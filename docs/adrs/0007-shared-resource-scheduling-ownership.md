# ADR-0007: Resource-owned scheduling when a shared resource gains a second consumer

- **Status**: Proposed
- **Date**: 2026-07-11
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0001](0001-modular-monolith.md), [ADR-0002](0002-events-for-inter-module-communication.md), [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md), [ADR-0004](0004-inter-service-communication-strategy.md), [ADR-0006](0006-project-per-module-physical-structure.md)

## Context

[ADR-0003](0003-appointment-as-scheduling-source-of-truth.md) made `Appointment` (Booking) the **sole** source of truth for scheduling state: "technician T is busy over window W" is a *derived* fact — it holds iff a confirmed `Appointment` referencing T overlaps W — and Workforce/Fleet hold master data only. That decision rests on one stated assumption, quoted verbatim from ADR-0003:

> "Booking is the only module that reasons about *time*. Workforce and Fleet know who exists and what they can do, not when."

Under that assumption ADR-0003 is correct, and its single-table `EXCLUDE USING gist` guarantee on `appointments` is the strongest available concurrency control. This ADR does **not** dispute that.

The forcing function is a **new requirement that violates the assumption**: a technician and a service bay are physical resources that more than one workflow will want to occupy. The car-service booking flow is the first consumer; anticipated future consumers (a warranty/recall work-order flow, internal maintenance/downtime, training blocks, an externally-integrated scheduling system) would each need to **reserve the same technician or bay** for a window, and the **no-double-booking guarantee must hold across all of them** — a warranty job and a car-service appointment must not land on the same technician at the same time.

The moment a *second writer* of reservations against a shared resource exists, three things ADR-0003 relied on stop holding:

1. **"Busy" is no longer derivable from `appointments` alone.** It is the *union* of reservations from every consumer. A query over Booking's `appointments` table cannot see a warranty reservation, so it would happily double-book.
2. **The single-table `EXCLUDE` constraint no longer protects the resource.** It guards overlaps *within* `booking.appointments`. It is blind to reservations held by any other table/service. The atomic guarantee — the thing ADR-0005 was chosen for — silently degrades to "no overlap *among car-service bookings*," which is not the invariant we need.
3. **There is no longer a single owner of the resource's calendar.** Each consumer holding its own copy of "when is T busy" is N sources of truth, i.e. zero. This is the exact failure ADR-0003 warned about — but ADR-0003 assumed we could avoid it by having only one writer. A second writer removes that option.

This is explicitly foreseen by the existing ADRs, not a surprise:

- ADR-0003, follow-up (c): revisit "if a hard requirement emerges to hold reservations transiently."
- ADR-0004, "Signals to reconsider" **#2** (a multi-step workflow with real state changes at each step, where each step is a legitimate saga participant) and **#5** (a new consumer needs authoritative "who's booked when").

The open question this ADR answers: **when a shared resource gains a second reservation-writing consumer, who owns the resource's schedule, where does the no-overlap guarantee live, and what shape does a cross-service booking take?**

Constraints carried in from prior ADRs and still binding:

- **Liftable modules** ([ADR-0001](0001-modular-monolith.md), [ADR-0006](0006-project-per-module-physical-structure.md)) — the target design must be reachable without a rewrite and must keep the resource's schedule co-located with the service that would be extracted.
- **Reads stay synchronous with a measured upgrade path** ([ADR-0004](0004-inter-service-communication-strategy.md)); async request-reply *for queries* remains rejected. This ADR concerns cross-service **writes** (reservations), which ADR-0004 already routes through events/sagas, not the query tiers.
- **The atomic no-overlap guarantee is non-negotiable** ([ADR-0005](0005-postgresql-over-document-database.md)) — whatever owns a resource's schedule must be able to enforce it with `EXCLUDE USING gist` on a single table, not push it into application code.

## Decision

We will adopt the following target design **when — and only when — a second reservation-writing consumer of a shared resource is committed** (see Trigger). Until then, [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md) stands unchanged and this ADR is dormant.

**1. Schedule ownership moves to the resource-owning service.** A shared physical resource owns its own calendar in the service that owns the resource's master data:

- **Workforce** owns `technician_reservation` (the technician's calendar).
- **Fleet** owns `bay_reservation` (the bay's calendar).

Each is the *single* source of truth for "is this resource occupied over window W," aggregating reservations from **every** consumer.

**2. Separate the generic `Reservation` from the domain-specific `Appointment`.** Two distinct concepts, two owners:

- **Reservation** (Workforce/Fleet) — generic, reason-agnostic: *"resource R is held for `[start, end)` by reservation #X, requested by consumer C, external ref E."* This is where the no-overlap invariant lives.
- **Appointment / work-order** (Booking or any other consumer) — domain-specific: *"customer K's car service, which holds reservation #X."* Each consumer owns its own work record and references the reservation id.

**3. The `EXCLUDE USING gist` no-overlap constraint moves to the reservation table.** It relocates from `booking.appointments` to `workforce.technician_reservation` and `fleet.bay_reservation`, keyed on `(resource_id, time_range)`. This preserves the ADR-0005 database-enforced guarantee, now correctly scoped to *all* consumers of the resource rather than to car-service bookings only.

**4. Availability is answered by the resource owner, not by Booking.** The availability endpoints discussed for Workforce/Fleet query their own reservation tables (in-process today; per ADR-0004 tier-1 sync IPC after extraction). Booking stops being the authority for "is technician T free."

**5. Reserving is a command, and cross-service booking becomes an orchestrated saga.** A consumer no longer writes `technician_id` into its own row directly; it invokes a **reserve command** on the owning service (`IReserveTechnician`, `IReserveBay`), which returns a reservation id or a conflict. A full car-service booking then spans multiple services with real state changes at each step, which is [ADR-0004](0004-inter-service-communication-strategy.md) signal #2 — a legitimate **orchestrated saga**:

```
reserve technician (Workforce) → reserve bay (Fleet) → create appointment (Booking) → [charge deposit (Billing)]
        │                               │ fail ✗
        └──── compensate: release technician reservation ◄── release bay reservation ◄────┘
```

Compensation for any failed step is **release the reservation(s) already taken**. We choose **orchestration over choreography** here for the same reasons ADR-0004 gives: with money and multi-step compensation involved, one coordinator that owns the flow and its compensations is far easier to reason about, trace, and debug than implicit event chains. Async *choreography for the availability check itself* remains rejected per ADR-0004.

**6. Transient holds use a TTL state machine.** Because a step that cannot sit inside a DB transaction (payment, or a human confirmation step) may separate "reserve" from "confirm," a reservation is not born confirmed. It follows `Pending → Confirmed` or, on timeout/failure, `Pending → Expired/Released` (the compensation). This is the standard seat-hold pattern. The `EXCLUDE` constraint treats a `Pending` (non-expired) reservation as occupying, so a hold blocks competitors while payment resolves.

Upon **acceptance at trigger**, this ADR **supersedes [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md) for shared, multi-consumer resources** (technician, bay). ADR-0003 continues to govern any resource that remains single-consumer. Booking's `Appointment` stays Booking's aggregate; what changes is that it references a reservation id instead of *being* the reservation.

## Alternatives Considered

- **Keep ADR-0003 as-is; each consumer stores its own reservations and reconciles via events.** Rejected: this is N sources of truth for one physical resource. It cannot offer an atomic cross-consumer no-overlap guarantee — two services can each pass their local availability check and both insert, and no single `EXCLUDE` constraint sees both. Correctness would move into eventual-consistency reconciliation on the *write* path, exactly where ADR-0003 and ADR-0004 both refuse to put it.

- **A dedicated central Scheduling/Reservation service** owning the calendars of *all* resource types. Rejected as the default (not forever): it re-centralizes what the modular monolith deliberately split, and drags an extraction seam through the middle of every resource. Workforce already owns technician identity and Fleet owns bay identity; co-locating each resource's calendar with its master data keeps the ADR-0001 "liftable" property and avoids a new always-on dependency on the booking hot path. Revisit only if a third+ resource type and genuinely cross-cutting scheduling policies make a shared engine cheaper than per-owner calendars.

- **Booking remains the sole writer; other consumers submit their work *through* Booking.** Rejected: it forces unrelated bounded contexts (warranty, maintenance, training) to model their workflows as car-service appointments, coupling every future consumer to Booking's schema and uptime. It also contradicts ADR-0001 ownership — a warranty work-order is not a Booking concept.

- **Distributed transaction / two-phase commit across the reserve steps.** Rejected: 2PC across services is the failure mode sagas exist to avoid — blocking locks held across network calls, coordinator as a single point of failure, and no support in the target runtime (Postgres + HTTP services). The saga with compensations (Decision §5) is the accepted pattern.

- **Fixed time-slot grid per resource** (discretize each day into buckets, mark taken). Rejected: services already have variable durations (45-min oil change, 30-min rotation), there is no fixed-grid calendar UI, and a grid explodes storage (resource × slot × day, mostly empty). Continuous `[start, end)` ranges with `EXCLUDE USING gist` remain strictly more expressive. A slot grid would only pay off for a product that sells pre-defined slots.

## Consequences

- **Positive:**
  - The no-double-booking guarantee stays **atomic and database-enforced** (ADR-0005) and, crucially, becomes correct across *all* consumers — the constraint sits on the reservation table every consumer writes through.
  - Each resource's calendar is co-located with its master data, so extracting Workforce or Fleet stays cheap (the reservation table moves with the service) — the ADR-0001 "liftable" property is preserved for the new state.
  - New consumers (warranty, maintenance, training, external integrations) are additive: they reserve through the same command port and own their own work record. No consumer touches another's schema.
  - Availability endpoints have a clear, correct owner (the resource service), resolving the Option-A/Option-B question from design discussion in favour of resource-owned availability.

- **Negative:**
  - Booking's happy path changes from **one local INSERT** to a **multi-step saga** with compensations, TTL holds, and a `Pending → Confirmed` lifecycle on `Appointment`. This is a real increase in complexity and failure surface — justified only once a second writer exists, never before.
  - The reserve step is a cross-service **write** on the booking hot path after extraction. Unlike the ADR-0004 read tiers, a reservation cannot be answered from a stale local read model — it must hit the authoritative reservation table. This is inherent to holding a real lock on a shared resource and cannot be optimized away with projections.
  - Two scheduling models coexist during migration: single-consumer resources under ADR-0003, shared resources under this ADR. Reviewers must know which regime a given resource is in.
  - A `Pending` hold that never confirms must be reaped (TTL expiry), adding a background concern (expiry sweeper or time-bounded constraint) that ADR-0003's born-confirmed model did not need.

- **Follow-ups:**
  - **No code today.** This ADR is dormant until the Trigger fires. Do not build reservation tables, saga orchestration, or TTL holds speculatively — that is the same premature-infrastructure trap ADR-0004 declines for read models.
  - When the trigger lands, write the second consumer's PRD first; it defines the reservation contract (what fields a reservation needs, whether holds are required, TTL length). Design the `reserve` command and the `Reservation` aggregate from that PRD, not from this ADR's sketch.
  - On acceptance, update this ADR's status to **Accepted**, set [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md) to **Superseded by ADR-0007** *for shared resources* (noting it still governs single-consumer resources), and update the ADRs [README](README.md) index for both.
  - The saga orchestrator, when built, rides the outbox/event infrastructure from [ADR-0002](0002-events-for-inter-module-communication.md); reservation lifecycle events (`ReservationConfirmed`, `ReservationReleased`) become part of the contract.
  - Payments ([`../roadmap.md`](../roadmap.md)) is the most likely concrete trigger, because a deposit charge is both a saga step *and* the reason a `Pending` hold with TTL is needed. When a Billing PRD lands, decide saga-vs-alternatives there, applying this ADR.

## Trigger (when this ADR activates)

Activate this design when **any** of the following is committed (not merely hypothesized):

1. A **second reservation-writing consumer** of a technician or bay is accepted into the roadmap/PRD (warranty/recall work-orders, internal maintenance/downtime as first-class blocks, training, or an external scheduling integration that must hold real time on our resources).
2. A **transient hold** requirement lands — e.g. a payment deposit or a human-confirmation step that separates "reserve" from "confirm" — matching ADR-0003 follow-up (c) and ADR-0004 signal #2.

Absent one of these, a single writer plus ADR-0003's derived-fact model remains simpler and strictly correct. "We might go microservices" is **not** a trigger — extraction alone (with Booking still the sole booker) is handled by ADR-0004's read tiers, not by this ADR.
