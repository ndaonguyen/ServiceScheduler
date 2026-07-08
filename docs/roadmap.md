# Roadmap

Product-direction view of anticipated modules and capabilities over the next ~6 months.

Distinct from other planning docs in this repo:

- **PRDs** (`docs/prds/`) — committed slice-level work with acceptance criteria and testing plans.
- **ADRs** (`docs/adrs/`) — locked architectural decisions.
- **This document** — intent, not commitment. Items graduate: roadmap entry → PRD → implementation branch → merged.

A roadmap entry means "we intend to build this and here is the concrete signal that will promote it to a PRD." Nothing here is scheduled; each item names its own trigger.

## Current state (as of 2026-07-08)

- **Auth / Identity** — shipped. JWT + cookie, ASP.NET Core Identity, refresh-token rotation. See [`CLAUDE.md`](../CLAUDE.md) and [`docs/authentication.md`](authentication.md).
- **Booking (first slice)** — planned. See [`prds/appointment-booking.md`](prds/appointment-booking.md). Introduces four modules simultaneously (Booking, Fleet, Workforce, Catalog) because a booking cannot be exercised end-to-end without vehicles, bays, technicians, and service types existing first.

## Next horizon (~6 months)

Ordered by likely sequencing. Sequencing is guidance, not commitment.

### 1. Booking — availability preview (same-module extension, not a new module)

**What:** a read-only endpoint (e.g. `GET /api/appointments/availability?dealershipId=…&serviceTypeId=…&date=…`) that returns free time slots so the client can render a slot picker, instead of submitting a specific `requestedStart` and getting a 409 back if it's busy. Purely additive to the existing Booking module — no new aggregate, no new module.

**Why:** the initial slice ships a *request-and-hope* UX — the client submits a specific time, server returns 201 or 409. Fine for internal / back-office use where the operator knows the schedule roughly; poor for self-service customers over the web or mobile. An availability endpoint lets the client display available times up front so the user picks a known-free slot.

**Why it deserves its own PRD:** non-trivial to implement correctly. Requires computing gaps in the union of technician-and-bay availability across a day, honouring `ServiceType.Duration` and the half-open-interval rule from BR-03. Effectively interval subtraction against the busy set from all confirmed appointments at the dealership. Also introduces cache-invalidation questions once concurrent bookings start writing to the busy set. Not hard, but not free.

**Trigger to promote to PRD:** the first client (web or mobile) needs to display available times before booking, or feedback from initial users indicates the 409-retry UX is a real problem.

**Depends on:** initial Booking slice merged. Nothing else.

### 2. Notifications module (bundles outbox + first event + first subscriber)

**What:** send email / SMS on booking events. Confirmation on `AppointmentConfirmed` first; reminders and status-change notifications later.

**Why its own module:** different failure semantics — a notification failing must never roll back a confirmed appointment. External I/O (SMTP, SMS gateway) with retry / backoff needs unlike OLTP writes. Naturally the first consumer of the outbox stream.

**What ships in this slice — three parts, none shippable alone:**

1. **Outbox pattern implementation** per [`adrs/0002-events-for-inter-module-communication.md`](adrs/0002-events-for-inter-module-communication.md) — `IEventPublisher` port, `outbox` table written in the same DB transaction as `appointments`, post-commit dispatcher with retry and poison-queue behavior.
2. **`AppointmentConfirmed` event + Booking handler modification** — the event record is defined in `Application/Features/Booking/Events/`, and the existing Booking handler is modified as part of this slice to publish it immediately after INSERT. The foundation slice ([`prds/appointment-booking.md`](prds/appointment-booking.md) AC-05) itself remains entirely event-free; event publishing is introduced *here*, not there.
3. **Notifications module + first `IEventHandler<AppointmentConfirmed>`** — subscriber that sends email / SMS confirmation to the customer.

**Why the three are bundled** — none is independently verifiable. An outbox with no event to publish is unused infrastructure. An event record with no subscriber is dead code. A subscriber with no publisher is untestable. Landing all three together lets the first integration test assert the whole dual-write-safe path: booking commits → outbox row is written atomically → dispatcher picks it up post-commit → subscriber receives it → subscriber failure does not roll back the booking. That end-to-end assertion is the entire justification for building the outbox in the first place — deferring any of the three would waste the assertion.

**Trigger to promote to PRD:** first requirement for the customer to receive a confirmation email or SMS after booking.

**Depends on:** Booking foundation slice merged (so there is a handler to modify).

### 3. Audit module

**What:** append-only log of business-relevant events (bookings, role changes, sensitive reads) with retention potentially longer than live data.

**Why its own module:** write-only, append-only, different query patterns and possibly different storage (partitioned table or separate schema). Regulatory retention may exceed OLTP retention.

**Trigger to promote to PRD:** a concrete driver — compliance requirement, dealership contract clause, or "before production go-live." Do not build without a driver; audit logs written for their own sake become noise nobody reads.

**Depends on:** Notifications slice (§ 2) merged — that is where the outbox pattern and the first published event (`AppointmentConfirmed`) actually land. Audit becomes the second subscriber on the same event stream, which validates the fan-out design and proves the outbox handles multiple consumers correctly.

### 4. Billing / Invoicing module

**What:** invoicing customers for completed service, payment capture, refunds.

**Why its own module:** different regulatory scope (potentially PCI if card data ever touches the process), different data retention, different auditability requirements. Never merged with Booking.

**Trigger to promote to PRD:** product decision that this system will charge for service (as opposed to the dealership handling payment via its existing POS out-of-band). If the answer is out-of-band, this module never lands and that is fine.

**Depends on:** nothing technical; Notifications and Audit will likely precede it for lower-risk delivery sequencing.

### 5. Reporting / Analytics

**What:** dashboards, exports, or aggregated views for dealerships and admins — e.g. bay utilization, technician throughput, no-show rates.

**Why potentially its own module (or its own store):** read-model shape is unlike the OLTP shape. Typical evolution is a read replica or materialized views first, then a dedicated analytics store once the query patterns stabilize.

**Trigger to promote to PRD:** first non-trivial reporting requirement from an actual stakeholder, with the specific questions named. Committing to a design before the questions are known locks in the wrong shape.

**Depends on:** enough live booking data to make reports meaningful — typically 1–3 months of production traffic.

## Explicit non-goals

Items sometimes proposed for a roadmap that this project is *not* committing to, with reasons. Listing them here so the same conversation does not repeat every quarter.

- **Redis (or any) caching layer.** No measured performance problem, no identified hot query, no cache-invalidation story. Speculative infrastructure. Revisit only under real load with a specific hot path.
- **Distributed application-level locks.** The `EXCLUDE USING gist` constraint from [`prds/appointment-booking.md`](prds/appointment-booking.md) (AC-03) is *the* concurrency answer for double-booking. Application-level locks would be strictly weaker and would reopen a decision already made.
- **Extraction into microservices as a goal in itself.** The modular monolith is a destination, not a stepping stone. Extraction is a tool applied to a specific operational pressure (independent scaling, independent reliability, team ownership, data gravity, regulatory scope) — see [`adrs/0001-modular-monolith.md`](adrs/0001-modular-monolith.md). "We should be microservices" is not a driver.
- **A "Common" or "Shared" module** for cross-cutting types. Death spiral — every module ends up depending on it, creating the coupling modules exist to prevent. Prefer duplication over premature sharing.
- **A separate `Customer` aggregate** distinct from `AppUser` + `Vehicle.OwnerId`. Only add when a concrete customer-profile requirement lands (preferences, credit terms, communication settings, per-customer pricing).
- **CQRS with a separate read store** as a pattern for its own sake. The mediator is already CQRS-shaped for writes. A separate read store waits for a real reporting question, not the aesthetic appeal of the pattern.
- **GraphQL layer / BFF.** One client, one API is fine. Revisit only if a second consumer emerges with materially different needs from the current REST surface.
- **Background job framework (Hangfire / Quartz / etc.) as speculative infrastructure.** The outbox dispatcher is already the first background worker this system will run. If a second periodic job appears (e.g. appointment reminders 24h out), decide *then* whether an in-process `IHostedService` is enough or a framework is warranted.

## How this document stays current

- Update after any PRD merges that graduates a roadmap item to shipped work — move the item from "Next horizon" to "Current state" and link the PRD.
- Add new roadmap entries as product intent solidifies. Delete or move to non-goals as intent changes.
- Review at least once per quarter. Delete items no longer relevant rather than let stale entries rot the doc.
