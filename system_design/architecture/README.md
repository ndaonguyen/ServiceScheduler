# Architecture — Vehicle Service Scheduler

> **The full system design lives in [`../DESIGN.md`](../DESIGN.md) — that is the single source of
> truth.** This folder just holds the high-level diagram it references.

A backend .NET service for scheduling vehicle service appointments across dealerships, technicians,
and service bays: a **modular monolith** (Clean Architecture + vertical-slice CQRS), one deployable
process partitioned into feature modules that can be extracted into their own services without a
rewrite. There is no frontend — the API is the product, and `/openapi/v1.json` is the client
contract.

## High-level diagram

![High-level architecture](high-level.png)

Source: [`high-level.excalidraw`](high-level.excalidraw) (edit in [Excalidraw](https://excalidraw.com)).

## Where things are

| Looking for… | Go to |
|---|---|
| Context, guiding principle, component roles | [`../DESIGN.md`](../DESIGN.md) §1–3 |
| Architecture trade-offs & module boundaries | [`../DESIGN.md`](../DESIGN.md) §4 |
| Request/data flow, overlap-prevention concurrency | [`../DESIGN.md`](../DESIGN.md) §5–6 |
| Technology choices, cross-cutting concerns, observability | [`../DESIGN.md`](../DESIGN.md) §7–8 |
| Data model, testing, trade-off summary, extension path | [`../DESIGN.md`](../DESIGN.md) §9–12 |
| The atomic decisions | [ADRs](../../docs/adrs/) |
| The product surface | [PRD](../../docs/prds/appointment-booking.md) |
| How to run it | [`../DESIGN.md`](../DESIGN.md) — Appendix |
