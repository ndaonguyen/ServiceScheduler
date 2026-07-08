# Input: Booking graceful retry on constraint violation

> **Required input** for `/plan-issue`. The GitHub issue is still the source of truth for acceptance criteria; this file adds design links, brainstorming, and constraints the issue doesn't capture.

## 1. Issue
- Primary: `#7` — https://github.com/ndaonguyen/ServiceScheduler/issues/7
- Blocked by (all merged/implemented): `#3` (handler + `IAppointmentRepository`), `#5` (candidate narrowing + `GetBusyResourcesAsync`), `#6` (the `EXCLUDE USING gist` constraints that raise the violation this slice catches)
- Closes the loop `#6` opened: #6 makes a concurrent double-book raise `23P01` → currently an unhandled `500`; #7 turns that into a correct `201` (retry succeeds) or `409` (no candidate left).

## 2. Design / Reference Links
- [PRD: Unified Service Scheduler — Appointment Booking](../prds/appointment-booking.md) — authoritative. In particular:
  - **§10 Sequence Diagram** — the exact step: *"INSERT appointment (final race guard = EXCLUDE constraint) → OK | ExclusionViolation → retry once with next candidate"*. This slice implements the `ExclusionViolation → retry once` branch.
  - **§6 NFR-01 / §7 AC-03** — the DB constraint (from #6) is the concurrency backstop; #7 is the graceful application-side reaction to it, so a race loser gets a clean outcome instead of a `500`.
  - **§8 API Contract** — the two 409s reused when the retry can't place the appointment: `NO_QUALIFIED_TECHNICIAN`, `NO_BAY_AVAILABLE` (already defined as `BookingError`s in #4/#5).
  - **§4 BR-01/BR-02** — the rules the constraint enforces; the retry picks a *different* free resource in the dimension that lost the race.
- [ADR-0001: Modular monolith](../adrs/0001-modular-monolith.md) / **AC-04** — no module references another's Domain/Infrastructure types, and (relevant here) the **Application layer must not reference Npgsql/EF exception types**. The Postgres `23P01` must be translated to a domain-neutral signal at the Infrastructure boundary (see brainstorming).
- [CLAUDE.md](../../CLAUDE.md) — handler `Features/Booking/RequestAppointment.cs`, repo port `Abstractions/IAppointmentRepository.cs`, impl `Infrastructure/Booking/AppointmentRepository.cs`, unit tests in `Application.Tests`.
- **Existing code this slice changes (post-#5/#6 state):**
  - `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — today (lines ~83–108) narrows to a **single** first-free technician + bay (`FirstOrDefault`) then does one `await appointments.AddAsync(appointment, ct)`. **This is the block #7 rewrites** into a keep-the-free-lists + insert-with-retry loop.
  - `source/AppointmentScheduler.Application/Abstractions/IAppointmentRepository.cs` — `AddAsync` currently returns `Task` and can throw a raw `DbUpdateException` on conflict. #7 changes how a conflict is surfaced (domain-neutral).
  - `source/AppointmentScheduler.Infrastructure/Booking/AppointmentRepository.cs` — `AddAsync` does `db.Appointments.Add(appointment); await db.SaveChangesAsync(ct);`. #7 adds the catch/translate + change-tracker cleanup here.
  - `source/AppointmentScheduler.Application/Features/Booking/BookingErrors.cs` — the two 409s already exist; reused, not changed.
  - `source/AppointmentScheduler.Api/Endpoints/BookingEndpoints.cs` — **no change** (renders any `BookingError`; a successful retry returns the normal 201).

## 3. Brainstorming

**Translate the DB conflict at the Infrastructure boundary (do NOT leak Npgsql into Application).** On `SaveChanges`, EF raises `DbUpdateException` whose inner is `Npgsql.PostgresException` with `SqlState == "23P01"` and a populated `ConstraintName` (`ex_appointments_bay_no_overlap` or `ex_appointments_technician_no_overlap`). The repository catches that, maps the constraint name to which resource lost, and surfaces a **domain-neutral** conflict the handler can react to without referencing Npgsql/EF types. The issue's AC test says *"a fake repository throwing a conflict exception on the first insert attempt"* — so the intended shape is an **Application-defined exception**, e.g.:
```csharp
public enum BookingResource { ServiceBay, Technician }
public sealed class AppointmentSlotConflictException(BookingResource resource) : Exception
{ public BookingResource Resource { get; } = resource; }
```
thrown by the repo's `AddAsync` (translated from `23P01`), caught by the handler. (Alternative: an enum return `TryAddAsync → Inserted|BayConflict|TechnicianConflict`, which avoids exceptions entirely and is more consistent with #4/#5's no-exceptions-for-control-flow. The plan should pick one; the AC wording nudges toward the exception, and because the handler **catches it internally**, it never escapes to `LoggingBehavior`, so it won't spam error logs. Flag this trade-off in the plan.)

**Change-tracker cleanup is essential.** After a failed `SaveChanges`, the failed `Appointment` is still tracked as `Added`; a subsequent `AddAsync(newAppointment)` + `SaveChanges` would try to insert **both** and fail again. On catching the violation, the repo must detach the failed entity (`db.Entry(appointment).State = EntityState.Detached`, or `db.ChangeTracker.Clear()`) before returning/throwing, so the retry starts clean. Call this out — it's the easy-to-miss bug.

**Handler: keep the free lists, retry once, pick the right 409.** Replace the single-`FirstOrDefault` pick with the ordered *free* lists and an insert loop bounded to **2 attempts** (initial + one retry, per the AC "retry once"):
```
freeTechnicians = technicians.Where(t => !busy.BusyTechnicianIds.Contains(t.Id)).ToList();
freeBays        = dealership.Bays.Where(b => !busy.BusyBayIds.Contains(b.Id)).ToList();
if (freeTechnicians is empty) return NoQualifiedTechnician;   // unchanged from #5
if (freeBays is empty)        return NoBayAvailable;
int techIdx = 0, bayIdx = 0;
for (attempt in 0..1):
    try { AddAsync(Build(freeBays[bayIdx], freeTechnicians[techIdx])); return 201; }
    catch conflict(resource):
        if resource == ServiceBay: bayIdx++; if (bayIdx == freeBays.Count) return NoBayAvailable;
        else:                      techIdx++; if (techIdx == freeTechnicians.Count) return NoQualifiedTechnician;
// exhausted the single retry and still conflicting -> the appropriate 409 for the last conflict
return lastConflictResource == ServiceBay ? NoBayAvailable : NoQualifiedTechnician;
```
The constraint name tells us **which** dimension lost, so we advance that list and return the matching 409 when it's exhausted (satisfies "the appropriate 409"). Selection stays "next free candidate" — no re-query of `GetBusyResourcesAsync` is required by the AC (the free list from the first narrowing is the candidate pool); the plan may optionally re-narrow instead, but reusing the list is simpler and matches the wording.

**Testing (unit only, per the AC).** Extend the fake `IAppointmentRepository` in `RequestAppointmentTests` to throw `AppointmentSlotConflictException` on the **first** `AddAsync` and succeed on the **second**, capturing the finally-persisted appointment. Cases:
- retry succeeds → 201, and the persisted appointment uses the **second** (next free) candidate in the conflicted dimension (the AC's core test);
- conflict with **no next candidate** in that dimension → the matching 409, nothing persisted;
- (edge) both attempts conflict → the appropriate 409.
The Infra translation of the real `23P01`/constraint-name → `AppointmentSlotConflictException` is **not** unit-testable with fakes (needs real Postgres); verify it manually against the #6 constraint and defer automated coverage to the future integration-test PRD.

## 4. Constraints & Non-goals

- **Constraints:**
  - **No Npgsql/EF types in Application.** The `23P01` violation is translated to a domain-neutral conflict (Application-defined exception or result) at the Infrastructure boundary; the handler reacts to that abstraction only (AC-04 / clean-arch).
  - **Retry exactly once** (2 insert attempts max) per the AC — not a loop over all candidates, no backoff.
  - On exhaustion, return **the 409 matching the conflicting dimension** (`NO_BAY_AVAILABLE` / `NO_QUALIFIED_TECHNICIAN`), reusing #4/#5's `BookingError` + `Result` + endpoint rendering (**no Api change**).
  - The retry path must reset EF change-tracker state so the failed insert isn't re-attempted.
  - **No schema change / no migration** — the constraint is #6; this slice is handler + repository logic only.
  - **Only `23P01` (exclusion_violation) is treated as a conflict**; any other `DbUpdateException`/exception propagates unchanged (still a `500`) — this slice doesn't broaden error handling.
  - Test seam is **unit tests only** (`Application.Tests`) against a fake repository (issue AC). No new `Api.Tests`; existing suites must stay green.
- **Non-goals (explicitly deferred / out of scope):**
  - Multiple retries, retry-all-candidates, or exponential backoff — single retry only.
  - Re-running `GetBusyResourcesAsync` between attempts (allowed if the plan prefers it, but not required; reusing the narrowed free list is the baseline).
  - Any change to #5's narrowing logic or #6's constraints/migration.
  - Integration tests exercising a real concurrent race / the real `23P01` translation → future Testcontainers PRD.
  - Handling conflicts for statuses other than `Confirmed` (the constraint is confirmed-only), or resources at other dealerships (already excluded by #5's candidate scoping).

---
Delete or move to an `archive/` subfolder once the plan PR merges.
