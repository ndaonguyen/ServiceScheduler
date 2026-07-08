# ADR-0003: Appointment is the sole system of record for scheduling state

- **Status**: Accepted
- **Date**: 2026-07-08
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0001](0001-modular-monolith.md), [ADR-0002](0002-events-for-inter-module-communication.md), [PRD — Appointment Booking](../prds/appointment-booking.md)

## Context

A booking references a `Technician` (owned by the Workforce module) and a `ServiceBay` (owned by the Fleet module). During booking, the system must answer: **"is this technician / bay reserved during the requested window?"** The design question is *where the reservation state lives*.

Two shapes were on the table:

- Reservation as a **derived fact**: "technician T is reserved at time W" ⟺ "there exists a confirmed Appointment referencing T over an overlapping window." No separate reservation record. Booking's `Appointment` table is the only place scheduling state lives.
- Reservation as a **stored fact**: separate `TechnicianReservation` (owned by Workforce) and `ServiceBayReservation` (owned by Fleet) tables, updated in lock-step with `Appointment`.

Additional constraints in play:

- The concurrency guarantee (no double-booking under concurrent requests across API instances) is enforced via Postgres `EXCLUDE USING gist` on the `appointments` table — a *single-table* constraint. See [PRD AC-03 / NFR-01](../prds/appointment-booking.md).
- [ADR-0001](0001-modular-monolith.md) requires that modules be extractable into their own services later without a rewrite. Anything that couples modules at the data layer makes extraction expensive.
- Booking is the only module that reasons about *time*. Workforce and Fleet know who exists and what they can do, not when.

## Decision

We will treat **`Appointment` (Booking) as the single source of truth for scheduling state.** Reservation is a **derived fact**, not a stored fact:

- A technician is "busy" over a window ⟺ a confirmed `Appointment` exists that references that technician and overlaps the window.
- A service bay is "busy" over a window ⟺ the same, for the bay.

Workforce and Fleet **own master data only** — technician identity + qualifications, dealership + bay identity + address. Neither module holds any calendar, reservation, or availability state.

Availability queries during booking are answered by scanning `appointments` inside the Booking module's own database, filtered by candidate technician / bay IDs supplied by Workforce and Fleet query ports.

## Alternatives Considered

- **Distributed reservation state** — separate `TechnicianReservation` in Workforce and `ServiceBayReservation` in Fleet, updated alongside `Appointment`.
  Rejected because:
  1. Two sources of truth are zero sources of truth — they will drift, and reconciling them is expensive.
  2. Loses the DB-level `EXCLUDE USING gist` guarantee: the constraint only works on a single table. With split reservations, atomic "reserve technician + reserve bay + insert appointment" requires a distributed transaction (or Saga with compensating actions), pushing correctness into application code.
  3. Violates [ADR-0001](0001-modular-monolith.md) by pushing scheduling logic into Workforce and Fleet — modules whose job is master data, not time. Every future feature that touches scheduling would have to update three tables in three modules.
  4. Makes extraction *harder*: once Booking becomes its own service, keeping the reservation tables in sync across service boundaries requires event pipelines, replay handling, and eventual-consistency reasoning at the write path — where we least want it.

- **Reservation state owned by Booking, but denormalized copies pushed to Workforce/Fleet as local read models** — Booking publishes `AppointmentConfirmed` / `AppointmentCancelled`; Workforce and Fleet build local projections for their own use.
  **Not rejected — deferred.** This is a valid *read-side* optimization for a future scale problem (e.g. Workforce independently answering "is Alex working today?" without calling Booking). Read models are not source of truth. If ever built, they follow this ADR, they don't reverse it. Wait for measured pressure before adding.

## Consequences

- **Positive:**
  - Single-table `EXCLUDE USING gist` remains the concurrency guarantee — the strongest option available, enforced by the database itself, not application code.
  - Booking is a self-contained aggregate at the data layer. Creating an appointment is one INSERT in one transaction.
  - Extracting Booking into its own service is cheap: the `appointments` table moves with it. Workforce and Fleet stay put with their master data. No cross-service data reconciliation.
  - Reasoning about scheduling stays in one place. Anyone debugging a booking-time question reads code in one module.

- **Negative:**
  - Any future capability that needs to answer "is this technician busy?" without going through Booking (e.g. an internal dashboard hosted in a different module, a future integration exposing technician calendars to third parties) must either call Booking as a query or subscribe to Booking's events and build a local read model. Read-time coupling to Booking is real.
  - Reporting queries that span "technicians × their busy days" must join through `appointments`. Fine for OLTP-scale reads; becomes a candidate for a read replica or analytics store when reporting volume grows.
  - The rule requires ongoing discipline: any temptation to add a `LastReservedAt` field to `Technician` (or similar convenience columns) reintroduces a second source of truth and must be rejected in code review.

- **Follow-ups:**
  - Enforce the boundary in code: Workforce and Fleet modules must have no columns, tables, or types named `Reservation`, `Availability`, `Booked`, `Busy`, or similar. Architecture-tests (see [ADR-0001](0001-modular-monolith.md) follow-ups) can add a check for this once they land.
  - When the outbox pattern lands (per [ADR-0002](0002-events-for-inter-module-communication.md) and the Notifications module in [`../roadmap.md`](../roadmap.md)), `AppointmentConfirmed` and `AppointmentCancelled` become the natural feed for any future Workforce/Fleet local read model — not because we need one yet, but so the event contract is designed once with that use case in mind.
  - Revisit this ADR only if: (a) a *measured* read-availability problem appears where Booking becomes a bottleneck for cross-module scheduling queries, or (b) a hard requirement emerges to hold reservations transiently (e.g. 15-minute payment hold) — even then, transient holds would likely be a new aggregate inside Booking (`AppointmentHold`), not a distributed reservation across modules.
