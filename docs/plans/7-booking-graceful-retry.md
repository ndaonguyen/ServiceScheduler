# Plan: Booking — graceful retry on constraint violation (#7)

> **Issue**: https://github.com/ndaonguyen/ServiceScheduler/issues/7
> **Standalone**: this plan is executable without reading any other file.

## Goal
Turn the rare concurrent double-book — where #6's `EXCLUDE USING gist` constraint rejects an INSERT
with Postgres `23P01` — from an unhandled `500` into a correct outcome: catch the violation, retry
**once** with the next free candidate from #5's narrowed list, and return `201` if it lands or the
matching `409` if it can't. Implements the PRD §10 `ExclusionViolation → retry once with next
candidate` branch. **No schema change, no Api change.**

## Scope & Non-goals

**In scope (this slice):**
- A domain-neutral conflict signal the handler can react to without referencing Npgsql/EF types:
  an Application-defined `AppointmentSlotConflictException(BookingResource resource)` (Design
  Decision **D1**).
- `AppointmentRepository.AddAsync` (Infrastructure) catches the EF/Npgsql exclusion violation
  (`23P01`), maps the constraint name → `BookingResource`, **detaches the failed entity**, and throws
  the Application exception; all other exceptions propagate unchanged.
- `RequestAppointment` handler: replace the single-candidate pick + single insert with the ordered
  **free** lists, an insert loop bounded to **2 attempts** (initial + one retry), advancing the
  conflicting dimension's candidate and returning the matching `409` on exhaustion.
- Unit tests: fake repository throws the conflict on the first insert and succeeds on retry (issue
  AC), plus the no-next-candidate → `409` cases.

**Out of scope (deferred — not missing):**
- Any schema/migration change (the constraints are #6).
- More than one retry, retry-all-candidates, or backoff — **single** retry per the AC.
- Re-running `GetBusyResourcesAsync` between attempts (allowed but not required; baseline reuses the
  narrowed free list).
- Broadening error handling to non-`23P01` failures — those still surface as before (`500`).
- Any Api/endpoint change (a successful retry returns the normal `201`; the two `409`s already render
  via #4/#5's `BookingError` path).
- Integration tests exercising a real concurrent race / the real `23P01`→exception translation →
  future Testcontainers PRD.

## Design Decisions (resolved during planning)

- **D1 — Domain-neutral conflict via an Application-defined exception (not a raw EF/Npgsql leak).**
  The Application layer must not reference `DbUpdateException`/`PostgresException` (AC-04 / clean-arch).
  The repo translates `23P01` into `AppointmentSlotConflictException(BookingResource)`, defined in
  Application. The **exception** shape (vs an enum return) is chosen because the issue AC explicitly
  describes the test as *"a fake repository throwing a conflict exception,"* and because the handler
  **catches it internally** — it never escapes to `LoggingBehavior`, so it does **not** produce an
  error log for an expected, recoverable race. (An enum-return `TryAddAsync` was the considered
  alternative — more consistent with #4/#5's no-exceptions-for-control-flow — but the AC pins the
  exception.)
- **D2 — Constraint-name → resource mapping lives in the repo, as named constants.** `23P01` carries
  `ConstraintName`; `ex_appointments_bay_no_overlap` → `ServiceBay`, `ex_appointments_technician_no_overlap`
  → `Technician`. These strings are shared with #6's raw-SQL migration; the repo declares them as
  `const`s with a comment pointing at the migration (Risk R2). Use `Npgsql.PostgresErrorCodes.ExclusionViolation`
  for the SQLSTATE rather than a bare `"23P01"`.
- **D3 — Retry exactly once; return the 409 of the dimension that lost.** The loop runs at most twice.
  The conflict's `Resource` says which list to advance and which `409` to return when that list is
  exhausted (or when the single retry is spent). This satisfies "the appropriate 409."
- **D4 — Change-tracker cleanup is part of the repo's catch.** After a failed `SaveChanges` the failed
  `Appointment` is still tracked `Added`; the repo detaches it (`db.Entry(appointment).State =
  EntityState.Detached`) before throwing, so the handler's next `AddAsync(newAppointment)` inserts only
  the new row. Missing this re-attempts the failed insert (Risk R6).
- **D5 — First-of-kind, surfaced up front.** There is **no existing custom exception** in `source/` and
  **no existing `PostgresException`/`23P01` handling** anywhere in the repo — both the exception type
  and the Infra translation are new patterns (no sibling to mimic). Implementation should follow EF/
  Npgsql idioms deliberately rather than copy an in-repo analogue.

## Requirement Traceability
| Issue acceptance criterion | Plan section | Verification |
|---|---|---|
| On `ExclusionViolation`, retry once with the next free candidate | Changes → Application (handler loop) + Infrastructure (translate) | Unit test: conflict on 1st insert, success on 2nd → 201 with the **second** candidate |
| No candidates remain after retry → appropriate `409` | Changes → Application (exhaustion branches) | Unit tests: single free bay + bay-conflict → `NO_BAY_AVAILABLE`; single free tech + tech-conflict → `NO_QUALIFIED_TECHNICIAN`; nothing persisted |
| Retry succeeds → `201` as normal | Changes → Application (return `Result.Ok`) | Unit test asserts 201 payload + persisted appointment |
| Unit test: fake repo throws conflict on first insert, succeeds on retry | Changes → Tests | New tests in `RequestAppointmentTests` |
| No unhandled behavior change for non-conflict paths | Scope; D1 | Existing AT-01..AT-13 tests stay green; only `23P01` is caught |

## Changes

### Application (`source/AppointmentScheduler.Application/`)

**New — the conflict signal.** `Features/Booking/AppointmentSlotConflictException.cs`.
> **Pattern to mimic**: none — first custom exception in the codebase (D5). Keep it minimal.
```csharp
namespace AppointmentScheduler.Application.Features.Booking;

/// <summary>Which resource lost a concurrent race for the requested window.</summary>
public enum BookingResource { ServiceBay, Technician }

/// <summary>
/// Thrown by <see cref="Abstractions.IAppointmentRepository.AddAsync"/> when the database rejects the
/// insert because the chosen bay/technician was taken concurrently (the #6 EXCLUDE constraint). The
/// handler catches it and retries with the next free candidate. Domain-neutral: no EF/Npgsql types
/// leak into Application (AC-04).
/// </summary>
public sealed class AppointmentSlotConflictException(BookingResource resource) : Exception
{
    public BookingResource Resource { get; } = resource;
}
```

**Edit — `Abstractions/IAppointmentRepository.cs`.** Document the new throw on `AddAsync` (signature
unchanged):
```csharp
/// <summary>
/// Persists the appointment. Throws <see cref="Features.Booking.AppointmentSlotConflictException"/>
/// if the insert is rejected by the no-overlap constraint (a concurrent booking took the slot).
/// </summary>
Task AddAsync(Appointment appointment, CancellationToken ct = default);
```

**Edit — `Features/Booking/RequestAppointment.cs`.** Replace the single pick + single insert (current
lines ~83–108) with free-list + retry-once. Everything above (guards, narrowing query) is unchanged.
```csharp
var freeTechnicians = technicians.Where(t => !busy.BusyTechnicianIds.Contains(t.Id)).ToList();
if (freeTechnicians.Count == 0) return BookingErrors.NoQualifiedTechnician; // AT-08

var freeBays = dealership.Bays.Where(b => !busy.BusyBayIds.Contains(b.Id)).ToList();
if (freeBays.Count == 0) return BookingErrors.NoBayAvailable;               // AT-09

var techIndex = 0;
var bayIndex = 0;
BookingResource lastConflict = default;
for (var attempt = 0; attempt < 2; attempt++) // initial + one retry (issue AC)
{
    var technician = freeTechnicians[techIndex];
    var bay = freeBays[bayIndex];
    var appointment = new Appointment { /* Id, OwnerId, ... , ServiceBayId = bay.Id, TechnicianId = technician.Id, ... */ };
    try
    {
        await appointments.AddAsync(appointment, cancellationToken);
        return Result.Ok(new RequestAppointmentResponse(appointment.Id, /* dealership, serviceType, */
            /* vehicle, */ new ServiceBayRef(bay.Id, bay.Label), new TechnicianRef(technician.Id, technician.Name),
            start, end, appointment.Status.ToString()));
    }
    catch (AppointmentSlotConflictException conflict)
    {
        lastConflict = conflict.Resource;
        if (conflict.Resource == BookingResource.ServiceBay)
        {
            if (++bayIndex >= freeBays.Count) return BookingErrors.NoBayAvailable;
        }
        else if (++techIndex >= freeTechnicians.Count)
        {
            return BookingErrors.NoQualifiedTechnician;
        }
    }
}
// Single retry spent and still conflicting (a next candidate existed): stop, return the matching 409.
return lastConflict == BookingResource.ServiceBay
    ? BookingErrors.NoBayAvailable
    : BookingErrors.NoQualifiedTechnician;
```
(The empty-list `409` guards keep #5's behaviour; the loop body reuses the exact `Appointment`
construction and success `RequestAppointmentResponse` from #5, only parameterised by the current
candidate.)

### Infrastructure (`source/AppointmentScheduler.Infrastructure/`)

**Edit — `Booking/AppointmentRepository.cs`.** Translate the exclusion violation on `AddAsync`.
> **Pattern to mimic**: none in-repo (first `DbUpdateException`/`PostgresException` handling, D5).
> Uses `Microsoft.EntityFrameworkCore` (`DbUpdateException`, `EntityState`) + `Npgsql`
> (`PostgresException`, `PostgresErrorCodes`).
```csharp
public async Task AddAsync(Appointment appointment, CancellationToken ct = default)
{
    db.Appointments.Add(appointment);
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.ExclusionViolation } pg)
    {
        // Don't leave the failed insert tracked, or the retry's SaveChanges re-attempts it (D4).
        db.Entry(appointment).State = EntityState.Detached;
        throw new AppointmentSlotConflictException(ResourceOf(pg.ConstraintName));
    }
}

// Constraint names are defined in the #6 migration (20260708180933_BookingNoOverlapConstraints).
private const string BayConstraint = "ex_appointments_bay_no_overlap";
private const string TechnicianConstraint = "ex_appointments_technician_no_overlap";

private static BookingResource ResourceOf(string? constraintName) => constraintName == TechnicianConstraint
    ? BookingResource.Technician
    : BookingResource.ServiceBay; // bay constraint (and any unexpected name) -> ServiceBay
```

**No other changes.** No DI change; no `AppDbContext`, entity, config, migration, or Api edit.

### Tests (`tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs`)
> Extend the fake `IAppointmentRepository` to optionally throw `AppointmentSlotConflictException` on
> the **first** `AddAsync` (for a configured `BookingResource`) and succeed thereafter, tracking the
> call count and the finally-persisted appointment. `BuildHandler` gains optional multi-candidate
> `bays`/`technicians` so a "next free candidate" exists.
```csharp
private sealed class FakeAppointmentRepository(
    BookingResource? conflictOnFirstInsert = null,
    params Appointment[] existing) : IAppointmentRepository
{
    public Appointment? Added { get; private set; }
    public int AddCalls { get; private set; }

    public Task AddAsync(Appointment appointment, CancellationToken ct = default)
    {
        AddCalls++;
        if (conflictOnFirstInsert is { } r && AddCalls == 1)
            throw new AppointmentSlotConflictException(r);
        Added = appointment;
        return Task.CompletedTask;
    }
    // GetBusyResourcesAsync unchanged (reuses AppointmentOverlap; empty `existing` -> none busy).
}
```

| Test | Arrange | Assert |
|---|---|---|
| `Bay_conflict_then_retry_succeeds_returns_201_with_next_bay` | two free bays; fake conflicts (`ServiceBay`) on 1st insert | 201; `AddCalls == 2`; persisted `ServiceBayId` == **second** bay |
| `Technician_conflict_then_retry_succeeds_returns_201_with_next_technician` | two free techs; conflict (`Technician`) on 1st | 201; persisted `TechnicianId` == second tech |
| `Bay_conflict_with_no_other_bay_returns_409_NO_BAY_AVAILABLE` | single free bay; conflict (`ServiceBay`) on 1st | 409 `NO_BAY_AVAILABLE`; `Added` is null |
| `Technician_conflict_with_no_other_technician_returns_409_NO_QUALIFIED_TECHNICIAN` | single free tech; conflict (`Technician`) on 1st | 409 `NO_QUALIFIED_TECHNICIAN`; `Added` is null |

All existing AT-01..AT-13 tests must still pass (the empty-`conflictOnFirstInsert` fake behaves like #5).

## Key Files
- `source/AppointmentScheduler.Application/Features/Booking/AppointmentSlotConflictException.cs` — new; `BookingResource` + exception.
- `source/AppointmentScheduler.Application/Abstractions/IAppointmentRepository.cs` — doc the throw.
- `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — free-list + retry-once loop.
- `source/AppointmentScheduler.Infrastructure/Booking/AppointmentRepository.cs` — translate `23P01` + detach + throw.
- `tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs` — seedable-conflict fake + retry tests.

## Testing & Verification
- `dotnet build -c Release` passes.
- `dotnet test -c Release` passes: new retry tests green; all #3/#4/#5 tests unchanged.
- **No migration.**
- **Manual check** (Development, optional — the real translation isn't unit-testable): force a race by
  inserting a conflicting `Confirmed` row via SQL between narrowing and insert is impractical by hand,
  so at minimum confirm that a booking which *would* violate the #6 constraint now returns a clean
  `409` (or `201` if a second candidate exists) rather than the pre-#7 `500`. Full concurrent-race
  verification is deferred to the future Testcontainers PRD.

## Branch & PR
- **This plan PR**: branch `plan/7`, title `docs: plan for booking graceful retry on constraint violation (#7)`.
- **Implementation branch** (later): `feat/7-booking-graceful-retry`.
- Implementation PR title: `feat: booking graceful retry on constraint violation (#7)`, body closes
  the issue (`Closes #7`). This is the final slice of the booking PRD (#3–#7).

## Notes / Risks surfaced
- **R1 — First-of-kind (D5).** No existing custom exception or DB-exception handling to mimic; the
  exception type and the `23P01` translation are new. Deliberately following EF/Npgsql idioms.
- **R2 — Magic-string coupling to the migration.** The constraint names are duplicated between #6's
  raw-SQL migration and the repo's `ResourceOf` mapping. If a constraint is renamed in a future
  migration without updating the repo, the mapping silently defaults to `ServiceBay`. Mitigated by
  `const`s + a comment; a shared constant isn't practical across raw SQL. Flag in review.
- **R3 — The real translation is not unit-tested.** Unit tests use the fake throwing the Application
  exception, so they cover the handler's retry orchestration and the exception contract — **not** the
  actual `DbUpdateException`→`AppointmentSlotConflictException` mapping (needs real Postgres). Deferred
  to the integration-test PRD; do a manual check.
- **R4 — Exception for control flow.** Justified: it models a genuinely exceptional concurrent race and
  is caught inside the handler, so it never reaches `LoggingBehavior` (no error-log noise). Consistent
  enough with #4/#5 given the AC pins the exception shape.
- **R5 — Single retry may still 409 with capacity theoretically free.** Per the AC we retry once; if
  the one retry also loses, we return the matching `409` even if further candidates exist. Acceptable
  and bounded; a fuller retry loop is a deliberate non-goal.
- **R6 — Change-tracker detach is mandatory.** Without detaching the failed entity, the retry's
  `SaveChanges` re-inserts it and conflicts again. Called out as the key implementation detail (D4).
```
