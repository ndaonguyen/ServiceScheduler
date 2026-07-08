# Architecture Decision Records (ADRs)

Every architectural decision that shapes how this codebase is organised — module boundaries, persistence choices, cross-module communication, transport rules, source-of-truth ownership — lives here as a numbered ADR. **Not code style, not library preferences, not naming conventions.** Only decisions whose reversal would require refactoring, migration, or re-onboarding the team.

Related planning docs:

- [`../prds/`](../prds/) — Product Requirement Documents. Committed slice-level work with acceptance criteria.
- [`../roadmap.md`](../roadmap.md) — anticipated modules and capabilities over ~6 months.
- [`TEMPLATE.md`](TEMPLATE.md) — copy this when authoring a new ADR.

## Index

Ordered by ADR number. Status column reflects the current state; a Superseded ADR is kept for history and links forward to the ADR that replaced it.

| # | Title | Status | Date | One-line summary |
|---|---|---|---|---|
| [0001](0001-modular-monolith.md) | Modular monolith | Accepted | 2026-07-07 | Single deployable process split into feature modules (Booking, Fleet, Workforce, Catalog) with enforced boundaries, designed to be liftable into services later without a rewrite. |
| [0002](0002-events-for-inter-module-communication.md) | Events for inter-module communication | Accepted | 2026-07-07 | Cross-module reads go through query ports; cross-module writes and side effects go through past-tense domain events dispatched from a post-commit outbox. |
| [0003](0003-appointment-as-scheduling-source-of-truth.md) | Appointment is the sole source of truth for scheduling state | Accepted | 2026-07-08 | Booking's `Appointment` table is the only place scheduling state (reservations, overlaps) lives; Workforce and Fleet hold master data only. "Technician is busy" is a derived fact, not a stored one. |
| [0004](0004-inter-service-communication-strategy.md) | Inter-service communication strategy after module extraction | Accepted | 2026-07-08 | Sync cross-boundary reads throughout — in-process today, HTTP/gRPC + resilience after extraction. Driven by the "Real-Time Availability Check" product constraint. Async choreography for the booking flow explicitly rejected; escalate specific read paths to local read models via events only under measured availability pressure. |

## What belongs in an ADR

Yes:

- Choice of framework or datastore that shapes the code (e.g. Postgres + `EXCLUDE USING gist` for scheduling concurrency).
- Module boundaries and communication rules.
- Source-of-truth ownership decisions (which module owns which fact).
- Auth mechanism and transport (cookie-JWT with SameSite=Strict, role-based).
- Any decision that would require a migration to reverse.

No:

- Library choices with no ripple effects (which JSON serializer, which logging package).
- Naming conventions, formatting, one-file-per-type, snake_case columns — these are conventions, documented in [`../../CLAUDE.md`](../../CLAUDE.md).
- Product decisions (what feature to build next) — these are PRDs and the roadmap.

If unsure, err toward writing the ADR. A five-minute ADR beats a two-hour code archaeology session six months later.

## How to add one

1. Copy [`TEMPLATE.md`](TEMPLATE.md) to `NNNN-short-kebab-title.md` using the next unused number.
2. Fill in Context, Decision, Alternatives Considered, Consequences. Keep it factual — no advocacy in Context, no hedging in Decision.
3. Add a row to the Index table above with status **Accepted** (or **Proposed** if still under review).
4. Cross-link from anywhere the decision is enforced or referenced — CLAUDE.md, related ADRs, PRDs.

## How to change one

Decisions are not immutable — reality changes. When an existing ADR is being reversed or materially altered:

1. **Do not edit the original.** Keep it intact as history.
2. Author a new ADR that supersedes it, referencing the old one in **Related**.
3. Update the old ADR's status to **Superseded by ADR-NNNN** with a forward link.
4. Update the Index table above: mark the old one superseded, add the new one.

This preserves the reasoning chain — someone in 2027 can see not only what we decided today but what we tried before and why we changed our minds.
