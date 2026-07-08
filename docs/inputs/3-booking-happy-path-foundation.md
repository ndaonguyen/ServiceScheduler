# Input: Booking happy-path appointment creation (foundation)

> **Required input** for `/plan-issue`. The GitHub issue is still the source of truth for acceptance criteria; this file adds design links, brainstorming, and constraints the issue doesn't capture.

## 1. Issue
- Primary: `#3` — https://github.com/ndaonguyen/ServiceScheduler/issues/3
- Related (optional): `#4`, `#5`, `#6`, `#7` (follow-up slices of the same PRD)

## 2. Design / Reference Links
- [PRD: Unified Service Scheduler — Appointment Booking](../prds/appointment-booking.md) — authoritative source for this slice and all four follow-ups. In particular:
  - §8 API Contract — request/response shape for `POST /api/appointments`
  - §9 Domain Model — entities and fields for all four modules
  - §10 Sequence Diagram — this issue implements the full request→persist path **except the busy-set/conflict-detection step** (diagram steps 16–18: the overlapping-appointment SELECT and narrow-to-free logic), which is deferred to `#5`. All other steps — service-type, bay, ownership, and qualified-technician lookups plus the final INSERT — are in scope, with a naive "first candidate" bay/technician pick standing in for real availability narrowing.
- [ADR-0001: Modular monolith](../adrs/0001-modular-monolith.md) — module boundary rules (no cross-module Domain/Infrastructure references; query ports for cross-module reads)
- [ADR-0002: Events for inter-module communication](../adrs/0002-events-for-inter-module-communication.md) — confirms this slice publishes no events (PRD AC-05); query ports are the only cross-module mechanism needed here
- [CLAUDE.md](../../CLAUDE.md) — layer conventions (`Features/<Module>/<Verb>.cs` handler naming, `Endpoints/<Module>Endpoints.cs`, EF configs in `Persistence/Configurations/`, snake_case columns)

## 3. Brainstorming

**Scope boundary vs. follow-up issues:** this slice is a deliberate walking skeleton. The handler picks the *first* bay and *first* qualified technician returned by the query ports — no overlap/conflict checking. That's correct for this issue: `#5` (availability computation) and `#7` (retry-on-violation) add real conflict detection later. Don't over-build here; a trivial "take candidates[0]" selection satisfies AT-01/AT-13 as long as seed data guarantees at least one free bay + technician exist.

**Query ports (per PRD §9, all live in `Application/Abstractions/`, implemented in the owning module's Infrastructure).** Exactly **four** ports. Each carries the **display fields** the PRD §8 201 response needs (names/labels), not just ids — §8 is the authoritative API contract, so the port shapes are richer than a bare-id sketch. The dealership's name is folded into the bay lookup (which already resolves the dealership), keeping the count at four:
- `IServiceTypeLookup.GetAsync(serviceTypeId)` → `{ Id, Name, Duration }` | not-found (Catalog)
- `IServiceBayLookup.ListByDealershipAsync(dealershipId)` → `{ DealershipName, Bays: [{ Id, Label }] }` | dealership-not-found (Fleet)
- `IVehicleOwnershipQuery.CheckAsync(vehicleId, ownerId)` → owned | not-owned | not-found (Fleet)
- `IQualifiedTechnicianLookup.ListAsync(dealershipId, serviceTypeId)` → `[{ Id, Name }]` (Workforce)

The handler resolves the owner from `ICurrentUser` and **calls** `IVehicleOwnershipQuery.CheckAsync`, proceeding on the happy-path `owned` result; the `403`/`404` guard branches on `not-owned`/`not-found` are deferred to `#4` (see §4). The other three lookups are consumed for the service duration, the first-candidate bay, and the first-candidate technician respectively.

**Domain entities (minimal fields, per PRD §9):**
- Booking: `Appointment` (Id, OwnerId string, VehicleId, DealershipId, ServiceTypeId, ServiceBayId, TechnicianId, ScheduledStart UTC, ScheduledEnd UTC, Status, CreatedAt)
- Fleet: `Vehicle` (Id, OwnerId, Make, Model, Year, Vin), `Dealership` (Id, Name, Address), `ServiceBay` (Id, DealershipId, Label)
- Workforce: `Technician` (Id, DealershipId, Name), `TechnicianQualification` (TechnicianId, ServiceTypeId)
- Catalog: `ServiceType` (Id, Name, Duration TimeSpan)

**Auth:** endpoint uses `.RequireAuthorization()`; caller id comes from `ICurrentUser` (existing port), never from the request body — mirrors the pattern already used elsewhere in the Api project.

**Seeding:** extend the existing `DbInitializer.MigrateAndSeedAsync` (already seeds Identity roles + a dev admin) with a Development-only seed step for a small fixed set of dealerships, bays, technicians (with qualifications), service types, and a couple of vehicles owned by the seeded dev users — enough to exercise AT-01/AT-13 by hand or in a test.

**Known edge case explicitly out of scope here:** BR-01/BR-02 (no double-booking) are NOT enforced by this slice. Don't add conflict-checking logic even defensively — it's tracked separately in `#5`/`#6`/`#7` so each slice stays independently reviewable.

## 4. Constraints & Non-goals

- **Constraints:**
  - Must follow ADR-0001 module boundaries: no module's handler `using`s another module's `Domain`/`Infrastructure` types; all cross-module reads go through the four ports above.
  - EF migrations are schema-of-record; new tables need a migration committed alongside the entity/config code (per CLAUDE.md — `dotnet ef migrations add <Name> --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api`).
  - Columns snake_case (EF convention already established for Identity tables).
  - Test seam is unit tests only (`AppointmentScheduler.Application.Tests`), against fake port implementations and a fake `IAppointmentRepository` — no `Api.Tests` integration test required for this slice (PRD AC-06).
- **Non-goals (explicitly deferred, not missing):**
  - Real conflict/overlap detection (→ `#5`)
  - `EXCLUDE USING gist` DB constraint (→ `#6`)
  - Retry-on-violation handling (→ `#7`)
  - Request validation **responses** for not-found/ownership/past-start (→ `#4`) — the four query ports (incl. `IVehicleOwnershipQuery`) are built and called this slice, but the handler assumes happy-path inputs for its own AT-01/AT-13 test and does **not** branch to `#4`'s `400`/`403`/`404` guard clauses. `#4` adds those branches on the results the handler already fetches, so nothing here should make them harder to add
  - Management/CRUD endpoints for any of the new entities (seed-data only, per PRD "Out of Scope")
  - A separate `Customer` aggregate (ownership stays `Vehicle.OwnerId` → `AppUser.Id`)

---
Delete or move to an `archive/` subfolder once the plan PR merges.
