# Plan: Booking — availability computation & conflict 409s (#5)

> **Issue**: https://github.com/ndaonguyen/ServiceScheduler/issues/5
> **Standalone**: this plan is executable without reading any other file.

## Goal
Replace the naive first-candidate pick (`dealership.Bays[0]` / `technicians[0]`) that #3 introduced and
#4 preserved with **real availability computation**: query the Booking module's own confirmed
appointments for the candidate bays/technicians at the requested dealership, narrow to the ones
genuinely free for the half-open window `[scheduledStart, scheduledEnd)`, and return the PRD's two
`409`s when none remain. This closes FR-04/FR-05 and enforces BR-01/BR-02/BR-03/BR-05/BR-06. It does
**not** add the DB-level `EXCLUDE USING gist` race guard (that is #6) or retry (that is #7).

## Scope & Non-goals

**In scope (this slice):**
- A Booking-owned overlap read on `IAppointmentRepository` (extends the existing port; **not** a new
  cross-module port — AC-02) that, given candidate bay ids + technician ids and a window, returns the
  subsets that are **busy** (have an overlapping confirmed appointment).
- A shared half-open overlap predicate (`AppointmentOverlap.Within`) reused by both the EF query and
  the unit tests, so AT-10/AT-11 exercise the *exact* expression the SQL is built from (Design
  Decision **D3**).
- Handler change in `RequestAppointment`: after fetching candidate bays + qualified technicians,
  compute `end`, query busy resources, narrow to free, and:
  - no free (or zero qualified) technician → `409 NO_QUALIFIED_TECHNICIAN` (AT-08);
  - no free (or zero) bay → `409 NO_BAY_AVAILABLE` (AT-09);
  - else pick the **first free** bay + technician and persist as before.
- Two new `BookingError` entries (`NO_QUALIFIED_TECHNICIAN`, `NO_BAY_AVAILABLE`, both HTTP `409`),
  reusing #4's `Result<T>` + `{ code, message }` rendering.
- Unit tests AT-08..AT-12 in `AppointmentScheduler.Application.Tests` against a seedable fake
  repository.

**Out of scope (deferred — not missing):**
- The DB `EXCLUDE USING gist` no-overlap constraint + `btree_gist` extension that make the guarantee
  concurrency-safe (AC-03 / NFR-01) → **#6**. This slice adds **no** migration.
- Closing the check→insert (TOCTOU) race / any locking or transaction work → **#6**'s constraint.
  Application-level narrowing here can still race under concurrent requests; that is acceptable for
  this slice and is the exact gap #6 fills.
- Retry-on-constraint-violation (fall through to the next candidate on an insert conflict) → **#7**.
- The 400/403/404 validation + errors → already shipped in **#4**; untouched.
- Load-balancing / "best" resource selection (first free candidate only); management endpoints;
  domain events (AC-05); a separate `Customer` aggregate.
- **No Api change** — `BookingEndpoints` already renders any `BookingError` as `{ code, message }` +
  `error.HttpStatus`, so a `409` flows through unchanged.

## Design Decisions (resolved during planning)

- **D1 — The overlap read extends the Booking-owned `IAppointmentRepository`, not a new query port.**
  `Appointment` is Booking's own aggregate and AC-02 keeps availability logic inside Booking, so this
  is a repository read, not a cross-module port (contrast the four cross-module lookups). Signature:
  ```csharp
  Task<BusyResources> GetBusyResourcesAsync(
      IReadOnlyCollection<Guid> candidateBayIds,
      IReadOnlyCollection<Guid> candidateTechnicianIds,
      DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
  // BusyResources = { IReadOnlySet<Guid> BusyBayIds, IReadOnlySet<Guid> BusyTechnicianIds }
  ```
  Returning the *busy* id subsets (not raw appointments) keeps the narrowing in the handler and the
  SQL trivial.
- **D2 — Candidate scoping gives BR-05/BR-06 / AT-12 for free.** Candidate ids come only from the
  dealership-scoped `IServiceBayLookup`/`IQualifiedTechnicianLookup`. The overlap query filters to
  those candidate ids, so another dealership's appointments and resources are never inspected and can
  never be selected. No extra dealership check is needed.
- **D3 — One shared half-open predicate, reused by EF and tests.** BR-03 is `existing.ScheduledStart <
  end && existing.ScheduledEnd > start` (confirmed only). Expressed once as an
  `Expression<Func<Appointment, bool>>` factory so (a) the EF query translates it to SQL and (b) unit
  tests compile the *same* expression — AT-10/AT-11 then pin the real rule, not a test-double copy
  (see Risk R1). Placed in the Application layer (`Features/Booking/AppointmentOverlap.cs`) so both
  Infrastructure (which references Application) and the tests can use it, keeping the Domain project
  free of `System.Linq.Expressions`.
- **D4 — Reuse #4's error seam; two new `409` `BookingError`s.** No Api change; the handler returns
  the new errors via the existing `Result<RequestAppointmentResponse>`.
- **D5 — Narrowing absorbs the empty-list cases #4 left naive.** `technicians.FirstOrDefault(free)` →
  `null` covers both "no qualified technician exists" and "all qualified are busy" (PRD AT-08 wording);
  same for bays (AT-09). No separate empty-list branch.
- **D6 — 409 order: qualified-technician first, then bay** (matches PRD §8/§10 ordering). Each
  AT-08/AT-09 fixes one shortage in isolation, so the order doesn't affect the ACs; stated for
  determinism.

## Requirement Traceability
| Issue acceptance criterion | Plan section | Verification |
|---|---|---|
| No qualified technician free → 409 `NO_QUALIFIED_TECHNICIAN` (AT-08) | Changes → Application (narrow techs) | Unit tests: all techs busy **and** zero-qualified-list variants |
| All bays busy → 409 `NO_BAY_AVAILABLE` (AT-09) | Changes → Application (narrow bays) | Unit test: all bays busy |
| Appt ending at T doesn't conflict with one starting at T (AT-10, BR-03) | Changes → Application (`AppointmentOverlap.Within`) | Unit test: seed touching appt on same bay+tech → 201 |
| `[T,T+D)` conflicts with `[T−1s,T+1s)` on same bay (AT-11, BR-03) | Changes → Application (`AppointmentOverlap.Within`) | Unit test: seed overlapping-by-1s appt → 409 `NO_BAY_AVAILABLE` |
| Only requested dealership's resources are candidates (AT-12, BR-05/06) | Changes → Application (D2 candidate scoping) | Unit test: "other dealership" appts/ids never in candidates → 201 assigns requested resource |
| Unit tests cover AT-08..AT-12 vs fake repositories | Changes → Tests | Seedable `FakeAppointmentRepository` reusing `AppointmentOverlap.Within` |
| No appointment persisted on a 409 path | Changes → Application (early return) + Tests | Each 409 test asserts `repo.Added` is `null` |

## Changes

### Application (`source/AppointmentScheduler.Application/`)

**New — the shared overlap predicate.** `Features/Booking/AppointmentOverlap.cs`.
```csharp
using System.Linq.Expressions;
using AppointmentScheduler.Domain.Booking;

namespace AppointmentScheduler.Application.Features.Booking;

/// <summary>BR-03 half-open overlap: an appointment's [ScheduledStart, ScheduledEnd) intersects the
/// requested [start, end). Shared by the EF query (translated to SQL) and unit tests (compiled), so
/// both use one definition of "conflict".</summary>
internal static class AppointmentOverlap
{
    public static Expression<Func<Appointment, bool>> Within(DateTimeOffset start, DateTimeOffset end) =>
        a => a.ScheduledStart < end && a.ScheduledEnd > start;
}
```

**Edit — `Abstractions/IAppointmentRepository.cs`.** Add the read + result record.
```csharp
public interface IAppointmentRepository
{
    Task AddAsync(Appointment appointment, CancellationToken ct = default);

    // Booking-internal availability read (AC-02): which of the candidate bays/technicians have an
    // overlapping *confirmed* appointment in [start, end).
    Task<BusyResources> GetBusyResourcesAsync(
        IReadOnlyCollection<Guid> candidateBayIds,
        IReadOnlyCollection<Guid> candidateTechnicianIds,
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
}

public sealed record BusyResources(IReadOnlySet<Guid> BusyBayIds, IReadOnlySet<Guid> BusyTechnicianIds);
```

**Edit — `Features/Booking/BookingErrors.cs`.** Add two 409s next to the five from #4.
```csharp
public static BookingError NoQualifiedTechnician =>
    new("NO_QUALIFIED_TECHNICIAN", 409, "No qualified technician is available at the dealership for the requested time.");

public static BookingError NoBayAvailable =>
    new("NO_BAY_AVAILABLE", 409, "No service bay is available at the dealership for the requested time.");
```

**Edit — `Features/Booking/RequestAppointment.cs`.** Replace the naive pick (lines that currently read
`var bay = dealership.Bays[0]; var technician = technicians[0];`) with availability narrowing. The
guards and everything above/below are unchanged.
```csharp
var technicians = await qualifiedTechnicians.ListAsync(request.DealershipId, request.ServiceTypeId, cancellationToken);

var start = request.RequestedStart;
var end = start + serviceType.Duration; // BR-07

var candidateBayIds = dealership.Bays.Select(b => b.Id).ToList();
var candidateTechnicianIds = technicians.Select(t => t.Id).ToList();
var busy = await appointments.GetBusyResourcesAsync(
    candidateBayIds, candidateTechnicianIds, start, end, cancellationToken);

// D6: technician first, then bay. FirstOrDefault covers "none qualified" and "all busy" (D5).
var technician = technicians.FirstOrDefault(t => !busy.BusyTechnicianIds.Contains(t.Id));
if (technician is null) // BR-01 / AT-08
    return BookingErrors.NoQualifiedTechnician;

var bay = dealership.Bays.FirstOrDefault(b => !busy.BusyBayIds.Contains(b.Id));
if (bay is null) // BR-02 / AT-09
    return BookingErrors.NoBayAvailable;

// ... existing Appointment construction + AddAsync + Result.Ok(...) unchanged (uses bay/technician) ...
```
Add `using System.Linq;` is unnecessary (ImplicitUsings). No other change to the handler.

### Infrastructure (`source/AppointmentScheduler.Infrastructure/`)

**Edit — `Booking/AppointmentRepository.cs`.** Implement `GetBusyResourcesAsync` over `AppDbContext`,
reusing the shared predicate. The status filter translates to `status = 'Confirmed'` (the column is
text via `HasConversion<string>`); the candidate-id filters translate to `= ANY(@ids)` (Npgsql). The
existing `service_bay_id` / `technician_id` indexes (from #3's migration) back the lookups.
```csharp
public async Task<BusyResources> GetBusyResourcesAsync(
    IReadOnlyCollection<Guid> candidateBayIds,
    IReadOnlyCollection<Guid> candidateTechnicianIds,
    DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
{
    var rows = await db.Appointments
        .Where(a => a.Status == AppointmentStatus.Confirmed)
        .Where(AppointmentOverlap.Within(start, end)) // BR-03
        .Where(a => candidateBayIds.Contains(a.ServiceBayId) || candidateTechnicianIds.Contains(a.TechnicianId))
        .Select(a => new { a.ServiceBayId, a.TechnicianId })
        .ToListAsync(ct);

    var busyBays = rows.Select(r => r.ServiceBayId).Where(candidateBayIds.Contains).ToHashSet();
    var busyTechnicians = rows.Select(r => r.TechnicianId).Where(candidateTechnicianIds.Contains).ToHashSet();
    return new BusyResources(busyBays, busyTechnicians);
}
```
(No DI change — `AppointmentRepository` is already registered; no new type to wire.)

### Tests (`tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs`)
> Make the existing `FakeAppointmentRepository` **seedable** with pre-existing appointments and have
> its `GetBusyResourcesAsync` reuse `AppointmentOverlap.Within` (compiled) + the confirmed + candidate
> filters — i.e. the *same* logic as the real repo, so AT-10/AT-11 are faithful (Risk R1). Empty seed
> (the default) keeps every #4/#3 test green (no busy → first candidate free).

```csharp
private sealed class FakeAppointmentRepository(params Appointment[] existing) : IAppointmentRepository
{
    public Appointment? Added { get; private set; }
    public Task AddAsync(Appointment appointment, CancellationToken ct = default)
    { Added = appointment; return Task.CompletedTask; }

    public Task<BusyResources> GetBusyResourcesAsync(
        IReadOnlyCollection<Guid> bayIds, IReadOnlyCollection<Guid> techIds,
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var overlaps = AppointmentOverlap.Within(start, end).Compile();
        var hits = existing.Where(a => a.Status == AppointmentStatus.Confirmed && overlaps(a));
        return Task.FromResult(new BusyResources(
            hits.Select(a => a.ServiceBayId).Where(bayIds.Contains).ToHashSet(),
            hits.Select(a => a.TechnicianId).Where(techIds.Contains).ToHashSet()));
    }
}
```

**New tests (AT-08..AT-12):**

| Test | Arrange | Assert |
|---|---|---|
| `All_qualified_technicians_busy_returns_409_NO_QUALIFIED_TECHNICIAN` | seed a confirmed appt covering the window on the only tech (same bay is free) | 409 `NO_QUALIFIED_TECHNICIAN`, not persisted |
| `No_qualified_technician_exists_returns_409_NO_QUALIFIED_TECHNICIAN` | `IQualifiedTechnicianLookup` fake returns `[]` | 409 `NO_QUALIFIED_TECHNICIAN`, not persisted |
| `All_bays_busy_returns_409_NO_BAY_AVAILABLE` | seed appts covering the window on every candidate bay (a free tech exists) | 409 `NO_BAY_AVAILABLE`, not persisted |
| `Appointment_ending_at_requested_start_does_not_conflict` (AT-10) | seed confirmed appt `[T-D, T)` on the same bay+tech; request starts at `T` | 201; assigned that bay+tech; persisted |
| `Appointment_overlapping_by_one_second_conflicts` (AT-11) | single bay; seed appt `[T, T+D)`; request `[T-1s, …)` overlapping | 409 `NO_BAY_AVAILABLE`, not persisted |
| `Only_requested_dealership_resources_are_candidates` (AT-12) | seed a confirmed appt on **foreign** bay/tech ids that are never returned by the lookups; requested dealership has one free bay+tech | 201; assigns the requested dealership's resource (foreign busy ids ignored) |

Use the pinned-clock `FixedClock` from #4 and a future `requestedStart` so the VR-06 guard passes.
Failure tests assert `repo.Added.Should().BeNull()`.

## Key Files
- `source/AppointmentScheduler.Application/Features/Booking/AppointmentOverlap.cs` — new; shared BR-03 predicate.
- `source/AppointmentScheduler.Application/Abstractions/IAppointmentRepository.cs` — add `GetBusyResourcesAsync` + `BusyResources`.
- `source/AppointmentScheduler.Application/Features/Booking/BookingErrors.cs` — add two 409s.
- `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — narrow to free candidates; return 409s.
- `source/AppointmentScheduler.Infrastructure/Booking/AppointmentRepository.cs` — implement the overlap query.
- `tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs` — seedable fake + AT-08..AT-12.

## Testing & Verification
- `dotnet build -c Release` passes.
- `dotnet test -c Release` passes: new AT-08..AT-12 green; all #3/#4 tests (AT-01..AT-06, AT-13) still
  green (empty-seed fake → first candidate free); `Api.Tests` unchanged.
- **No migration** — no entity/EF-mapping/schema change; the `appointments` table + `service_bay_id`
  / `technician_id` indexes already exist (#3's `BookingFoundation`). Do **not** run `dotnet ef
  migrations add`, and do **not** add `EXCLUDE`/`btree_gist` (that is #6).
- **Manual check** (Development, optional): POST a valid booking, then POST an **overlapping** one for
  the same seeded dealership/service type — expect the second to return `409 NO_BAY_AVAILABLE` or
  `409 NO_QUALIFIED_TECHNICIAN` once the single seeded bay/tech is taken; a **non-overlapping** time
  returns `201`; a booking that starts exactly when the first ends returns `201` (BR-03).

## Branch & PR
- **This plan PR**: branch `plan/5`, title `docs: plan for booking availability computation & conflict 409s (#5)`.
- **Implementation branch** (later, for `/implement-issue`): `feat/5-booking-availability-computation`.
  (A `feat/availability-computation` branch already exists locally, empty/off `main`; reuse or rename
  to the standard `feat/5-…` form.)
- Implementation PR title: `feat: booking availability computation & conflict 409s (#5)`, body closes
  the issue (`Closes #5`) and notes the deferred DB race guard (#6) and retry (#7).

## Notes / Risks surfaced
- **R1 — AT-10/AT-11 are only faithful if the fake reuses the real predicate.** If the fake repo
  reimplemented overlap inline, it could pass while the production EF predicate is wrong. Mitigated by
  the single `AppointmentOverlap.Within` expression used by both the EF query and the compiled fake
  (D3). The one thing unit tests still cannot prove is the EF→SQL *translation* of that expression and
  of `.Contains` (`= ANY`); that is covered by the manual check now and by the future `Api.Tests`
  integration seam (PRD Future Work).
- **R2 — Concurrency/TOCTOU is intentionally unhandled.** Two simultaneous requests can both observe a
  resource free and both insert. This slice does not close that window; NFR-01 is satisfied by #6's
  `EXCLUDE USING gist` constraint (and #7's retry). Called out so review doesn't expect locking here.
- **R3 — `IReadOnlyCollection.Contains` translation.** Npgsql renders `ids.Contains(col)` as `col =
  ANY(@ids)`; pass a materialized `List<Guid>` (as the handler does). Empty candidate lists translate
  to `= ANY('{}')` (always false) and return no rows — harmless; narrowing then yields the right 409.
- **R4 — Predicate placement.** `AppointmentOverlap` lives in Application (not Domain) to keep
  `System.Linq.Expressions` out of the Domain project, while staying reachable by Infrastructure and
  tests. BR-03 is arguably a domain rule; if the team prefers it in Domain that is a valid alternative
  with no behavioural difference.
- **Reuse win:** this slice needs **no** Api change and **no** migration — the #4 `Result`/`BookingError`
  seam and #3's table/indexes carry it. The net new surface is one repository read, one predicate,
  two error entries, and the handler narrowing.
```
