# ADR-0004: Inter-service communication strategy after module extraction

- **Status**: Accepted
- **Date**: 2026-07-08
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0001](0001-modular-monolith.md), [ADR-0002](0002-events-for-inter-module-communication.md), [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md)

## Context

Today the system is a modular monolith — cross-module reads are in-process function calls behind query ports (per [ADR-0001](0001-modular-monolith.md)), and cross-module writes/side effects are in-process events dispatched via a post-commit outbox (per [ADR-0002](0002-events-for-inter-module-communication.md)). Availability coupling between modules does not exist: they share one process.

[ADR-0001](0001-modular-monolith.md) commits us to keeping the design "liftable" — any module should be extractable into its own service without a rewrite. That future forces a decision the monolith postpones: **how do extracted services communicate at runtime for cross-service reads?** Writes and side effects are already answered (events via message bus, per [ADR-0002](0002-events-for-inter-module-communication.md)). Reads are the open question.

The tension is well-documented in Chris Richardson's *Microservices Patterns* (§3.4). Synchronous inter-service calls multiply unavailability:

```
Availability(Booking) = A(Booking) × A(Workforce) × A(Fleet) × A(Catalog)
```

At 99.5% per service, a 4-hop sync chain yields ~98.0% — roughly 7 days of downtime per year, even if every individual service hits its SLO. Circuit breakers, retries, and timeouts fail *fast* but do not raise the ceiling. To beat the multiplication effect, the caller must not be on a sync path at all — which is what Richardson advocates via **self-contained services** with **local read models** built from events published by other services.

The counter-force is complexity. Local read models require: event subscription pipelines per consuming service, projection code, out-of-order and duplicate handling, replay logic for drift recovery, and storage duplication. That cost is worth paying only when a real availability requirement demands it — building read-model infrastructure speculatively is the same kind of premature investment we've already declined for the outbox (see [PRD Future Work](../prds/appointment-booking.md#future-work)).

We need a strategy that (a) works from day 1 of any service extraction without imposing read-model complexity up front, and (b) has a clear, mechanical upgrade path when availability pressure appears.

Constraint from [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md): scheduling state (the `appointments` table + `EXCLUDE USING gist` constraint) stays entirely inside Booking regardless of what pattern the *other* services use. This ADR is about how Booking talks *outward* for master-data lookups, not about who owns scheduling.

**Product constraint pinning the booking hot path to synchronous semantics:** the originating scenario for the first slice (see [PRD — Appointment Booking](../prds/appointment-booking.md)) specifies a **"Real-Time Availability Check"** before confirming. This is a product requirement, not a technical preference. It rules out any pattern where the caller receives a pending acknowledgement and learns the outcome later via notification or polling — the caller must know, within the booking request, whether the appointment was confirmed or rejected and why. Async choreography sagas (fire `AppointmentRequested`, wait for `AvailabilityChecked` responses from each service, then confirm or compensate) violate this constraint by design, even before considering their engineering cost. Any future pattern change on this axis is a product decision first, an architecture decision second.

## Decision

We will use a **tiered strategy** for cross-service reads after extraction, with the tier chosen per read path based on measured availability requirements:

**Tier 1 (default): Synchronous HTTP / gRPC + resilience.**

For any cross-service read, the default implementation of the query port is a sync HTTP or gRPC call to the owning service, with:

- Explicit timeouts (typically 1–5 seconds).
- Retry with exponential backoff on transient failures (5xx / network blip).
- Circuit breaker to fail fast when the callee is down.
- Distributed tracing (correlation ID propagated in headers).

In .NET, this is `IHttpClientFactory` + `.AddStandardResilienceHandler()`. Handler code does not change from the monolith — the query-port interface stays identical; only the implementation swaps from EF query to HTTP client.

**Tier 2 (opt-in): Local read model built from events.**

When a specific read path has a hard availability requirement that the tier-1 multiplication effect cannot meet — e.g. Booking SLO ≥ 99.9% while it has four sync dependencies each at 99.5% — that path upgrades to a local read model:

- The owning service publishes events describing changes to the data (per [ADR-0002](0002-events-for-inter-module-communication.md)).
- The consuming service subscribes, projects events into a local read-only table, and answers the query from the local table.
- The query-port interface is unchanged; only its implementation swaps from HTTP client to local DB query.

Read models are **projections, never sources of truth.** The owning service remains authoritative. This does not conflict with [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md) — Booking's scheduling state stays inside Booking; a read model of Workforce's technician-qualification data inside Booking is a projection of Workforce's authoritative data, not a competing source of truth.

**Never: async request-reply for queries.**

We will not build correlation-ID-based request/response over a message bus to answer "is this technician qualified?" That reinvents RPC with worse latency, worse failure modes, and no benefit. Queries that need a return value use either sync IPC or a local read model. Never a queue.

**Rules for choosing the tier:**

1. Start every new cross-service read at tier 1. Simple, working, measurable.
2. Measure. Instrument the sync path for latency, error rate, and callee availability.
3. Upgrade a specific path to tier 2 only when: (a) the measured availability of that dependency demonstrably prevents the consuming service from hitting its SLO, or (b) the domain justifies staleness — e.g. Workforce data changes slowly enough that a few seconds of drift is acceptable, and Booking cannot tolerate Workforce outages.
4. Do not upgrade all paths together. Each read path is decided independently based on its own SLO and change frequency.

## Alternatives Considered

- **Sync HTTP / gRPC only (no upgrade path).** Rejected: Richardson's multiplication effect is real. A service dependent on N sync callees has SLO ceiling A^N. At N=4, 99.5% callees, ceiling is ~98%. Fine for internal tooling; not fine for customer-facing paths. Forbidding tier 2 forever would eventually force a rewrite once availability pressure lands.

- **Event-sourced / local-read-model everywhere from day 1 of extraction.** Rejected: premature complexity. Speculatively building event pipelines, projections, replay logic, and dedup for every read path before we know which paths actually have availability pressure is the same kind of speculative infrastructure we already declined for the outbox. Read-model complexity should track measured requirements, not architectural aesthetics.

- **Async request-reply over message bus for queries.** Rejected: reinvents RPC without benefits. Correlation IDs, timeouts, ordering, replay — everything HTTP already gives you, with worse latency and less predictable failure modes. The Richardson book itself does not advocate this shape for query paths.

- **Async choreography saga for the booking flow itself** — Booking fires `AppointmentRequested`; Fleet, Workforce, and Catalog each consume it and publish response events; Booking aggregates responses via correlation ID and finalizes or compensates. Rejected on **four** independent grounds, any one of which is disqualifying:
  1. Violates the "Real-Time Availability Check" product constraint in the originating scenario — the caller cannot be told "pending, we'll email you." That is a product decision to reopen before this ADR can be reopened.
  2. Is functionally RPC-over-bus (see rejection above). The bus is just transport; the request/response semantics remain.
  3. Does not remove the sync point that actually matters. The overlap check + INSERT still has to happen atomically inside Booking against its own `appointments` table (per [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md) and the `EXCLUDE USING gist` constraint). The choreography only moves the sync boundary; it does not eliminate it. In practice, race conditions get *harder* to reason about because two concurrent requests can both receive "available" responses before either INSERT resolves.
  4. Complexity cost — event schema versioning, correlation ID tracking, pending state machines, timeout handling, compensating events, out-of-order and duplicate handling, dead-letter queues, distributed tracing across the full flow, WebSocket/polling for the client — for a use case that sync HTTP + a Postgres constraint solves in ~200ms with ~50 lines of handler code.

- **Shared database read-only across services.** Rejected: breaks [ADR-0001](0001-modular-monolith.md) module ownership at the data layer. Each service extraction would drag the shared schema along. Reintroduces the coupling extraction was meant to remove.

- **API gateway with data aggregation (BFF composes cross-service reads).** Rejected as a *replacement* for this decision, though not for its own use case. A BFF composes reads for a client; it does not solve availability multiplication for the caller (Booking) which still needs the data during a write path. Different problem.

## Consequences

- **Positive:**
  - Day-1 extraction of any module is achievable without building event-projection infrastructure first. Tier 1 works out of the box with `IHttpClientFactory` + resilience — no new services to stand up, no message bus wiring required for reads.
  - The upgrade path is mechanical, not architectural. Because query ports abstract the transport, swapping a specific port implementation from HTTP client to local-read-model reader is a per-port change — handlers do not notice.
  - Event contracts designed today (see [ADR-0002](0002-events-for-inter-module-communication.md) and the outbox landing with the Notifications module per [`../roadmap.md`](../roadmap.md)) can be shaped with the knowledge that they *may* later feed local read models in consuming services. This costs nothing today and preserves the option.
  - The two-tier structure gives a clear, measurable trigger for adopting complexity: "we adopted tier 2 for this path because the measured tier-1 availability was X%, below our SLO target Y%." No aesthetic debates about "should we be event-driven."

- **Negative:**
  - Two implementation strategies coexist in the codebase once the first tier-2 path lands. Reviewers must understand both patterns and know which applies to which port. Mitigated by keeping the query-port interface identical across tiers — the strategy is confined to the Infrastructure implementation.
  - Tier 1 does not solve availability multiplication. Teams operating a customer-facing service with a strict SLO will eventually pay the cost of building tier 2 for at least some paths. That cost is deferred, not avoided.
  - Tier 2 introduces eventual-consistency semantics on the read path. Some domain rules that seem obvious under strong consistency ("only technicians currently in the qualification table can be assigned") become subtler ("only technicians in the qualification table *as of the last event we processed*"). Consumers must be idempotent, must dedup, and must handle out-of-order events. Domain rules that cannot tolerate any staleness must stay on tier 1 or be redesigned.
  - When a tier-2 read model drifts (missed events, corrupted projection), rebuild requires replay capability. The message bus and event-storage choice made when Notifications lands (per [`../roadmap.md`](../roadmap.md)) must support replay from a retained log. Kafka-shaped, not RabbitMQ-shaped.

- **Follow-ups:**
  - **This ADR is only load-bearing after the first module extraction.** No code changes today. But the shape of events written for the outbox — starting with the Notifications module — should be **thick enough** to feed a future read model: include full state of the changed entity, not just the ID that changed. "TechnicianQualified { technicianId, serviceTypeId }" is fine because it *is* the full state. "TechnicianUpdated { technicianId }" is not fine — a subscriber cannot project from it.
  - When the first cross-service read path lands (post-extraction), instrument it before deciding the tier. Do not preemptively build tier 2 based on hypothetical SLO. Do not stay on tier 1 past a measured problem.
  - Revisit this ADR if the industry consensus shifts (rare) or if we adopt a service mesh (Istio, Linkerd) that changes the sync-IPC failure model materially — a mesh can add retries and mTLS but does not change the availability math.
  - When a tier-2 read model is adopted for a specific path, write a short follow-up ADR naming the path, the SLO trigger, and the events consumed. That ADR does not supersede this one; it applies it.

- **Signals to reconsider async-heavy patterns (including choreography):**
  Sync is not "the answer forever." It is the answer given today's product constraints and system size. Any of the following would justify reopening this ADR with a new alternative on the table:
  1. **Product UX shift** — the "Real-Time Availability Check" requirement is relaxed or replaced (e.g. the business decides pending-then-notify is acceptable, or introduces "waitlist a slot"). This is the single most likely trigger, and it is a product decision, not an engineering one.
  2. **Multi-step business workflow appears** — a booking flow that inherently spans services with real state changes at each step (e.g. `book → charge card → confirm → notify kitchen`) where each step is a legitimate saga participant, not a disguised query. Payments in [`../roadmap.md`](../roadmap.md) is the plausible near-term candidate; when a Billing PRD lands, saga vs. two-phase-commit vs. keeping payment inside Booking is decided *then*, and gets its own ADR.
  3. **Measured availability pressure** — Booking's SLO tightens (e.g. from 99% to 99.9%) *and* instrumentation shows sync HTTP dependencies are the binding constraint. Tier 2 (local read model per ADR-0004) is the first response; async choreography is not, because it still doesn't remove the atomicity sync point.
  4. **Scale changes fundamentally** — booking volume grows to where the ratio of availability queries to bookings makes local read models cheaper to run than sync HTTP with autoscaling. This is a cost, not correctness, argument.
  5. **New consumer of scheduling data forces the shape** — an external system integration or a new module needs authoritative "who's booked when" without depending on Booking's uptime. Read models over events (per [ADR-0002](0002-events-for-inter-module-communication.md)) are the answer, not a choreography rewrite of the booking flow itself.

  Absent one of these, sync stays. "It feels dated" is not a signal.
