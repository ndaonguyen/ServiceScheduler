# Input: Booking availability computation & conflict 409s

> **Required input** for `/plan-issue`. The GitHub issue is still the source of truth for acceptance criteria; this file adds design links, brainstorming, and constraints the issue doesn't capture.

## 1. Issue
- Primary: `#5` — https://github.com/ndaonguyen/ServiceScheduler/issues/5
- Blocked by: `#3` (merged — built the four query ports, the `Appointment` table + indexes, and the naive first-candidate handler)
- Builds directly on: `#4` (merged — introduced the `FluentResults` `Result<T>` return + `BookingError`/`{ code, message }` rendering that this slice reuses for its two 409s; the endpoint already renders any `BookingError`, so **no Api change** is expected)
- Related (optional): `#6` (DB-level `EXCLUDE USING gist` race guard), `#7` (retry-on-violation) — both consume this slice's output but are **not** in scope here

## 2. Design / Reference Links
- [PRD: Unified Service Scheduler — Appointment Booking](../prds/appointment-booking.md) — authoritative for this slice. In particular:
  - **§3 Functional Requirements** — FR-04 (auto-select a bay *free* for `[scheduledStart, scheduledEnd)`) and FR-05 (auto-select a technician *qualified* **and** *free* for the window). #3 satisfied the "select a candidate" half; this slice adds the "free for the window" half.
  - **§4 Business Rules** — BR-01 (technician no overlapping confirmed appts), BR-02 (bay no overlapping confirmed appts), **BR-03 (half-open intervals — an appt ending at T does not conflict with one starting at T)**, BR-05 (bay only at its dealership), BR-06 (technician only at their dealership).
  - **§8 API Contract → Error responses** — the two rows this slice implements: `409 NO_QUALIFIED_TECHNICIAN` and `409 NO_BAY_AVAILABLE`, both using the same `{ "code", "message" }` body as #4. **Codes/status must match §8 exactly.**
  - **§10 Sequence Diagram** — implements the steps #3/#4 stubbed: "SELECT overlapping appointments for candidate bays + techs" → "Narrow to free bay + free tech (else 409)". It does **not** implement the final "INSERT … EXCLUDE constraint … retry" race guard (that is #6/#7).
  - **§11 Acceptance Criteria** — AT-08..AT-12 map 1:1 to the new handler unit tests.
  - **AC-02** — availability/time-window logic lives **entirely inside the Booking module against its own `Appointment` data**; Fleet/Workforce/Catalog hold no calendar concept. So the overlap query is Booking-internal (extends the Booking-owned `IAppointmentRepository`), **not** a new cross-module query port.
  - **AC-03 / NFR-01** — the concurrency-safe no-overlap *guarantee* is a **DB `EXCLUDE USING gist` constraint deferred to #6**. This slice does application-level narrowing only; it does **not** close the check→insert (TOCTOU) race.
  - **Testing Notes** — unit tests only, against fake repositories; AT-10/AT-11 pin BR-03, AT-12 pins BR-05/BR-06.
- [ADR-0001: Modular monolith](../adrs/0001-modular-monolith.md) — the overlap query reads Booking's own aggregate, so it stays inside Booking; no new cross-module reference.
- [CLAUDE.md](../../CLAUDE.md) — layer conventions; handler in `Features/Booking/RequestAppointment.cs`, port in `Application/Abstractions/IAppointmentRepository.cs`, impl in `Infrastructure/Booking/AppointmentRepository.cs`, EF against `AppDbContext`.
- **Existing code this slice modifies (post-#4 state):**
  - `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — after the #4 guards and the `qualifiedTechnicians.ListAsync(...)` call, it currently does the naive pick `var bay = dealership.Bays[0]; var technician = technicians[0];` (with a comment saying "Empty-list handling → 409 … is #5"). **This is exactly the code #5 replaces.**
  - `source/AppointmentScheduler.Application/Abstractions/IAppointmentRepository.cs` — today only `Task AddAsync(Appointment, ct)`. Extend with the overlap read.
  - `source/AppointmentScheduler.Infrastructure/Booking/AppointmentRepository.cs` — add the EF overlap query implementation.
  - `source/AppointmentScheduler.Application/Features/Booking/BookingErrors.cs` — add two `409` entries (`NO_QUALIFIED_TECHNICIAN`, `NO_BAY_AVAILABLE`) alongside the five from #4.
  - `source/AppointmentScheduler.Api/Endpoints/BookingEndpoints.cs` — **no change**; it already renders any `BookingError` as `{ code, message }` + `error.HttpStatus`, so a `409` flows through unchanged.

## 3. Brainstorming

**Where the overlap query lives.** AC-02 keeps availability logic inside Booking, and `Appointment` is Booking's own aggregate — so this is **not** a cross-module query port. Extend the Booking-owned `IAppointmentRepository` (Application abstraction, Infrastructure impl) with a read method. Proposed shape (final signature is the plan's call):
```
Task<BusyResources> GetBusyResourcesAsync(
    IReadOnlyCollection<Guid> candidateBayIds,
    IReadOnlyCollection<Guid> candidateTechnicianIds,
    DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
// BusyResources = { IReadOnlySet<Guid> BusyBayIds, IReadOnlySet<Guid> BusyTechnicianIds }
```
Passing the **candidate ids** (which come from the dealership-scoped `IServiceBayLookup`/`IQualifiedTechnicianLookup`) means the query only ever inspects those resources — that's what makes **AT-12 / BR-05 / BR-06** fall out for free: another dealership's appointments and resources are never in the candidate set, so they can never be selected or cause a conflict. Returning the *busy* id sets (rather than raw appointments) keeps the narrowing logic in the handler and the SQL simple.

**Half-open overlap predicate (BR-03).** Two intervals `[s1,e1)` and `[s2,e2)` overlap iff `s1 < e2 && s2 < e1`. In the query: an existing confirmed appointment conflicts with the request `[reqStart, reqEnd)` iff `existing.ScheduledStart < reqEnd && existing.ScheduledEnd > reqStart`, filtered to `Status == Confirmed`. This gives AT-10 (existing ends at T, new starts at T → `existing.End (T) > reqStart (T)` is false → **no conflict**) and AT-11 (`[T,T+D)` vs `[T−1s,T+1s)` → overlaps → conflict). Keep the predicate in exactly this form so BR-03 is unambiguous.

**Handler change (replaces the naive pick).** After fetching candidate bays and qualified technicians:
1. Compute `end = start + serviceType.Duration` (already done for BR-07).
2. Call `GetBusyResourcesAsync(candidateBayIds, candidateTechIds, start, end)`.
3. `freeTechs = candidateTechs.Where(t => !busy.BusyTechnicianIds.Contains(t.Id))`; if **empty → `409 NO_QUALIFIED_TECHNICIAN`** (this absorbs the "none qualified" empty-list case #4 left naive **and** the "all qualified busy" case — PRD AT-08 covers both).
4. `freeBays = candidateBays.Where(b => !busy.BusyBayIds.Contains(b.Id))`; if **empty → `409 NO_BAY_AVAILABLE`** (empty bay list or all busy).
5. Pick the **first free** bay and technician (deterministic, matches #3's "first candidate" spirit — no optimization/load-balancing).

**409 ordering.** Each AT-08/AT-09 fixes one shortage in isolation, so any consistent order passes; PRD §8/§10 list technician before bay — recommend checking **qualified-technician availability first, then bay**, and state the chosen order in the plan.

**Reuses #4's machinery.** The two 409s are new `BookingError` entries (HttpStatus 409). The handler returns them via the existing `Result<RequestAppointmentResponse>`; the endpoint already renders `BookingError` → `{ code, message }` + status. So the only new surface is the repository read, the two error entries, and the handler narrowing.

**Testing (unit only).** Extend `RequestAppointmentTests` (or a sibling) with a fake `IAppointmentRepository` whose `GetBusyResourcesAsync` returns configurable busy sets. AT-08: all/one qualified tech busy (and separately: empty qualified list) → 409 NO_QUALIFIED_TECHNICIAN. AT-09: all bays busy → 409 NO_BAY_AVAILABLE. AT-10: existing appt ends exactly at requested start on the same bay/tech → 201 (not busy). AT-11: overlapping-by-1s on the same bay → 409 NO_BAY_AVAILABLE. AT-12: seed one free + one busy resource at the requested dealership plus ample free resources "elsewhere" (i.e. ids never passed as candidates) → 201 assigning the requested dealership's free resource, asserting the query was only asked about the candidate ids. Failure paths assert **nothing persisted**.

## 4. Constraints & Non-goals

- **Constraints:**
  - The two codes/statuses must match **PRD §8** exactly: `NO_QUALIFIED_TECHNICIAN` → 409, `NO_BAY_AVAILABLE` → 409, both with the `{ "code", "message" }` body (reuse #4's `BookingError`/`Results.Json`).
  - Half-open overlap predicate exactly `existing.ScheduledStart < reqEnd && existing.ScheduledEnd > reqStart`, filtered `Status == Confirmed` (BR-03).
  - Availability logic stays **inside Booking** against `Appointment` (AC-02); the overlap read extends the Booking-owned `IAppointmentRepository` — **not** a new cross-module port, and no module references another module's Domain/Infrastructure types (ADR-0001 / AC-04).
  - Candidate resources come only from the dealership-scoped lookups; the overlap query is scoped to those candidate ids so other dealerships are never considered (BR-05/BR-06 / AT-12).
  - **No EF migration** — the `appointments` table and its `service_bay_id` / `technician_id` indexes already exist from #3's `BookingFoundation` migration; this slice is query + handler logic only. (If the plan finds a genuinely missing supporting index it should call it out, but none is expected.)
  - Test seam is **unit tests only** (`AppointmentScheduler.Application.Tests`) against fake repositories (AC-06 / issue AC). No new `Api.Tests`.
- **Non-goals (explicitly deferred, not missing):**
  - The DB-level `EXCLUDE USING gist` constraint + `btree_gist` extension that make the guarantee concurrency-safe (AC-03 / NFR-01) → **#6**. This slice does **not** add the constraint or the migration for it.
  - Closing the check→insert (TOCTOU) race / any concurrency handling → covered by #6's DB constraint; **do not** add locking or transactions for it here.
  - Retry-on-constraint-violation (pick the next candidate when the INSERT trips the gist constraint) → **#7**.
  - The 400/403/404 request validation and its errors → already shipped in **#4**; not revisited.
  - Load-balancing / "best" bay-or-technician selection — first free candidate only.
  - Management/CRUD endpoints, domain events (AC-05), a separate `Customer` aggregate — all still out of scope per the PRD.

---
Delete or move to an `archive/` subfolder once the plan PR merges.
