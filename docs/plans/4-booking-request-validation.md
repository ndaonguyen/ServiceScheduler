# Plan: Booking — request validation & failure responses (#4)

> **Issue**: https://github.com/ndaonguyen/ServiceScheduler/issues/4
> **Standalone**: this plan is executable without reading any other file.

## Goal
Add guard-clause validation to the Booking `RequestAppointment` handler so unknown/not-owned
references and past start times return the PRD's stable machine-readable error codes
(`{ code, message }`) with the correct HTTP status, instead of the happy-path skeleton naively
proceeding to bay/technician selection. The four query ports the `#3` slice already built and calls
return exactly the failure signals these guards branch on — this slice is purely additive: no new
port, entity, table, migration, or seed change. Typed failures are carried on the **FluentResults**
`Result<T>` (already added as a dependency).

## Scope & Non-goals

**In scope (this slice):**
- Five guard clauses in `RequestAppointmentHandler`, each mapping a failure signal the handler
  **already fetches** to a PRD §8 error (AT-02..AT-06):
  - `IServiceTypeLookup.GetAsync` → `null` ⇒ 404 `SERVICE_TYPE_NOT_FOUND` (VR-05 / AT-05)
  - `requestedStart` not strictly future (vs injected `TimeProvider`) ⇒ 400 `REQUESTED_START_IN_PAST` (VR-06 / AT-06)
  - `IServiceBayLookup.ListByDealershipAsync` → `null` ⇒ 404 `DEALERSHIP_NOT_FOUND` (VR-04 / AT-04)
  - `IVehicleOwnershipQuery.CheckAsync` → `NotFound` ⇒ 404 `VEHICLE_NOT_FOUND` (VR-02 / AT-02)
  - `IVehicleOwnershipQuery.CheckAsync` → `NotOwned` ⇒ 403 `VEHICLE_NOT_OWNED_BY_CALLER` (VR-03 / AT-03)
- Change the handler's response to FluentResults `Result<RequestAppointmentResponse>` so it returns a
  typed failure instead of throwing (Design Decision **D1**).
- A `BookingError : FluentResults.Error` carrying the PRD's stable `Code` + `HttpStatus` alongside the
  message, plus a `BookingErrors` catalogue holding the five triples in one place.
- Endpoint change in `BookingEndpoints` to render a success as `201 Created` (unchanged body) or a
  failure as `{ "code", "message" }` with the error's HTTP status (PRD §8 error shape).
- Unit tests AT-02..AT-06 in `AppointmentScheduler.Application.Tests` against the existing fake
  ports; plus the **required** update of the existing AT-01/AT-13 tests to unwrap the `Result` and pin
  the clock (Design Decision **D3** / Risk R1).

**Out of scope (deferred — not missing):**
- The two `409` availability responses — `NO_QUALIFIED_TECHNICIAN` (AT-08) and `NO_BAY_AVAILABLE`
  (AT-09) — which require real availability/overlap computation → **#5**. This slice keeps the naive
  `Bays[0]` / `technicians[0]` selection and adds **no** empty-list branch.
- Conflict/overlap detection + half-open intervals (AT-10/AT-11) → **#5**;
  `EXCLUDE USING gist` (AC-03) → **#6**; retry-on-violation → **#7**.
- Re-verifying 401 (AT-07): already enforced by `.RequireAuthorization()` in the ASP.NET pipeline
  before `ISender.Send` runs — no new test (the existing `BookingEndpointsTests` 401 case already
  covers it end-to-end).
- Request *format* validation beyond the five VRs (malformed GUIDs, missing fields) — model-binding
  concern, not in the issue ACs.
- Any Domain, EF configuration, `AppDbContext`, migration, seed, or query-port change.

## Design Decisions (resolved during planning)

- **D1 — Handler returns `FluentResults.Result<RequestAppointmentResponse>`; it does not throw for
  expected 4xx.** The mediator (`Mediator.cs`) returns the handler's declared `TResponse` verbatim, so
  changing the request to `IRequest<Result<RequestAppointmentResponse>>` composes with no mediator
  change and lets each AT be asserted as an ordinary return value against fake ports. Exceptions were
  rejected: the existing `LoggingBehavior` catches and `LogError`s every exception before rethrowing,
  so using exceptions for *expected* validation failures would emit an error log per 4xx. Result
  values keep the pipeline exception-free. This matches the issue's "guard-clause … returning error
  codes" framing. **FluentResults** (v4.0.0) is the chosen carrier — the team preferred a proven
  library over a bespoke type. #5's two 409s reuse the same `Result` + `BookingError` seam. (Verified
  against the installed assembly: `Result<T>` exposes `IsSuccess`/`IsFailed`/`Value`/`Errors` and an
  implicit `Error → Result<T>` conversion; `Error` has an `Error(string)` ctor and `Message`, so a
  `BookingError : Error` with extra `Code`/`HttpStatus` properties is the idiomatic extension point.)
- **D2 — Error body is the PRD §8 `{ code, message }` shape, NOT `Results.Problem`/ProblemDetails.**
  `AuthEndpoints` uses `Results.Problem`/`Results.ValidationProblem` (RFC 7807), but PRD §8 defines a
  distinct, stable `{ "code": "<STABLE_CODE>", "message": "<human-readable>" }` contract for booking
  errors. §8 is authoritative ("the API is the product"), so the endpoint renders `Results.Json(new {
  code, message }, statusCode)` from the `BookingError` and does not reuse the auth ProblemDetails
  helpers. The five `code` strings and their statuses match §8 character-for-character. (FluentResults
  ships a `Message` on every error but no HTTP status or stable code — hence `BookingError` adds both.)
- **D3 — Guard order follows the §10 sequence diagram:** service-type lookup → past-start check →
  dealership/bays lookup → ownership check. Each AT-02..AT-06 fixes exactly one bad input, so any
  consistent order passes the ACs; we follow §10 for fidelity with the documented flow. The
  past-start check compares against the handler's injected `TimeProvider` (`clock.GetUtcNow()`),
  never `DateTimeOffset.UtcNow`, so AT-06 is deterministic.
- **D4 — Naive candidate selection is preserved.** After the guards, the handler still takes
  `dealership.Bays[0]` and `technicians[0]`. Empty-list handling (→ 409, #5) is explicitly *not*
  added, keeping this slice independently reviewable and matching the issue's ACs (which stop at
  AT-06).

## Requirement Traceability
| Issue acceptance criterion | Plan section | Verification |
|---|---|---|
| Unknown `vehicleId` → 404 `VEHICLE_NOT_FOUND` (AT-02) | Changes → Application (guard on `CheckAsync == NotFound`) | Unit test `Unknown_vehicle_returns_404_VEHICLE_NOT_FOUND` |
| Vehicle owned by another customer → 403 `VEHICLE_NOT_OWNED_BY_CALLER` (AT-03) | Changes → Application (guard on `CheckAsync == NotOwned`) | Unit test `Vehicle_owned_by_another_caller_returns_403_VEHICLE_NOT_OWNED_BY_CALLER` |
| Unknown `dealershipId` → 404 `DEALERSHIP_NOT_FOUND` (AT-04) | Changes → Application (guard on `ListByDealershipAsync == null`) | Unit test `Unknown_dealership_returns_404_DEALERSHIP_NOT_FOUND` |
| Unknown `serviceTypeId` → 404 `SERVICE_TYPE_NOT_FOUND` (AT-05) | Changes → Application (guard on `GetAsync == null`) | Unit test `Unknown_service_type_returns_404_SERVICE_TYPE_NOT_FOUND` |
| `requestedStart` not strictly future → 400 `REQUESTED_START_IN_PAST` (AT-06) | Changes → Application (guard vs `clock.GetUtcNow()`) | Unit test `Past_requested_start_returns_400_REQUESTED_START_IN_PAST` |
| Error responses use `{ code, message }` (PRD §8) | Changes → Api (`Results.Json`) + `BookingError` | Unit tests assert `BookingError.Code`; endpoint renders `{ code, message }` + status |
| Unit tests cover AT-02..AT-06 vs fake query ports | Changes → Tests | 5 new tests in `RequestAppointmentTests` reuse the existing hand-rolled fakes |
| No appointment persisted on a failure path | Changes → Application (early return) + Tests | Each failure test asserts `repo.Added` is `null` |

## Changes

### Application (`source/AppointmentScheduler.Application/`)

**New — the booking error type + catalogue.** `Features/Booking/BookingErrors.cs`.
> Extends FluentResults' `Error` (which supplies `Message`) with the PRD's stable `Code` and the
> `HttpStatus` the API layer renders, so no code→status mapping table is needed. Single source of
> truth for the five codes/statuses (input-file constraint); `message` text is not asserted by the ACs
> but should be sensible. The five codes/statuses are copied verbatim from PRD §8.

```csharp
namespace AppointmentScheduler.Application.Features.Booking;

using FluentResults;

// A FluentResults error carrying the PRD §8 stable code + HTTP status alongside the message.
public sealed class BookingError(string code, int httpStatus, string message) : Error(message)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
}

internal static class BookingErrors
{
    // Fresh instance per access — FluentResults' Error is mutable (Reasons/Metadata), so we don't
    // share a singleton that downstream code could accidentally mutate.
    public static BookingError ServiceTypeNotFound => new("SERVICE_TYPE_NOT_FOUND", 404, "The specified service type does not exist.");
    public static BookingError RequestedStartInPast => new("REQUESTED_START_IN_PAST", 400, "requestedStart must be in the future.");
    public static BookingError DealershipNotFound   => new("DEALERSHIP_NOT_FOUND", 404, "The specified dealership does not exist.");
    public static BookingError VehicleNotFound       => new("VEHICLE_NOT_FOUND", 404, "The specified vehicle does not exist.");
    public static BookingError VehicleNotOwned       => new("VEHICLE_NOT_OWNED_BY_CALLER", 403, "The specified vehicle is not owned by the caller.");
}
```

**Edit — `Features/Booking/RequestAppointment.cs`.** Change the response type to
`Result<RequestAppointmentResponse>` and insert the five guards in §10 order (D3). The `using
FluentResults;` is added; the record shapes (`RequestAppointmentResponse`, `DealershipRef`, …) are
unchanged.

```csharp
using FluentResults;
// ...

// was: IRequest<RequestAppointmentResponse>
public sealed record RequestAppointment(
    Guid VehicleId, Guid DealershipId, Guid ServiceTypeId, DateTimeOffset RequestedStart)
    : IRequest<Result<RequestAppointmentResponse>>;

internal sealed class RequestAppointmentHandler( /* ctor unchanged */ )
    : IRequestHandler<RequestAppointment, Result<RequestAppointmentResponse>>
{
    public async Task<Result<RequestAppointmentResponse>> Handle(RequestAppointment request, CancellationToken ct)
    {
        var ownerId = currentUser.UserId!;                       // RequireAuthorization guarantees presence

        var serviceType = await serviceTypes.GetAsync(request.ServiceTypeId, ct);
        if (serviceType is null)                                 // VR-05 / AT-05
            return BookingErrors.ServiceTypeNotFound;            // implicit Error -> failed Result<T>

        if (request.RequestedStart <= clock.GetUtcNow())         // VR-06 / AT-06 (strictly future)
            return BookingErrors.RequestedStartInPast;

        var dealership = await serviceBays.ListByDealershipAsync(request.DealershipId, ct);
        if (dealership is null)                                   // VR-04 / AT-04
            return BookingErrors.DealershipNotFound;

        var ownership = await vehicleOwnership.CheckAsync(request.VehicleId, ownerId, ct);
        if (ownership == VehicleOwnership.NotFound)               // VR-02 / AT-02
            return BookingErrors.VehicleNotFound;
        if (ownership == VehicleOwnership.NotOwned)               // VR-03 / AT-03
            return BookingErrors.VehicleNotOwned;

        var technicians = await qualifiedTechnicians.ListAsync(request.DealershipId, request.ServiceTypeId, ct);

        // D4: naive "first candidate" preserved; empty-list -> 409 is #5, not this slice.
        var bay = dealership.Bays[0];
        var technician = technicians[0];
        var start = request.RequestedStart;
        var end = start + serviceType.Duration;                  // BR-07

        var appointment = new Appointment { /* unchanged construction */ };
        await appointments.AddAsync(appointment, ct);

        return Result.Ok(new RequestAppointmentResponse(/* unchanged */)); // Ok<T> infers Result<T>
    }
}
```
Notes:
- The failure returns rely on FluentResults' implicit `Error → Result<TValue>` conversion (verified
  present in 4.0.0); `BookingError : Error` upcasts, then converts to a failed `Result<T>`. `Result.Ok(value)`
  uses the generic `Ok<TValue>` overload, inferring `Result<RequestAppointmentResponse>`.
- The `!`/`_ =` placeholders from `#3` disappear — `serviceType`/`dealership` are now guarded
  non-null, and the ownership result is consumed rather than discarded. No new port call is introduced;
  the technician lookup stays after the ownership guard, matching §10.

### Api (`source/AppointmentScheduler.Api/`)

**Edit — `Endpoints/BookingEndpoints.cs`.** Render success vs the PRD §8 error body.
> **Pattern to mimic**: the existing group; per **D2** use `Results.Json` for the `{ code, message }`
> shape rather than `Results.Problem`. Adds `using FluentResults;` (for `Result` inference) — though
> `IsSuccess`/`Value`/`Errors` are used, not the type name directly.

```csharp
group.MapPost("", async (RequestAppointment body, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(body, ct);
    if (result.IsSuccess)
        return Results.Created($"/api/appointments/{result.Value.AppointmentId}", result.Value);

    // Every failure this handler produces is a BookingError (carries the PRD §8 code + status).
    var error = result.Errors.OfType<BookingError>().First();
    return Results.Json(
        new { code = error.Code, message = error.Message },
        statusCode: error.HttpStatus);
})
.WithName("RequestAppointment")
.RequireAuthorization();
```
The success 201 body is byte-for-byte what `#3` returned (`result.Value` *is* the old
`RequestAppointmentResponse`), so the existing happy-path integration test stays green. `Send`'s
`TResponse` now infers to `Result<RequestAppointmentResponse>` automatically from the request record.
(If #5 adds a second endpoint needing the same rendering, extract a
`ToHttpResult(this ResultBase)` helper then — not now.)

### Tests (`tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs`)
> Reuse the file's existing hand-rolled fakes (`FakeServiceTypeLookup`, `FakeServiceBayLookup`,
> `FakeVehicleOwnershipQuery`, `FakeQualifiedTechnicianLookup`, `FakeAppointmentRepository`,
> `FakeCurrentUser`). They already accept the exact failure signals each AT needs (nullable infos, the
> `VehicleOwnership` enum), so no fake changes are required — only new arrange values. Add
> `using FluentResults;` and `using AwesomeAssertions;` (the latter already present).

**Required edits to the existing two tests (AT-01, AT-13):**
1. **Unwrap the Result** — assertions now read `result.IsSuccess.Should().BeTrue()` then
   `result.Value.Dealership.Id.Should().Be(...)`, etc. (was `response.Dealership.Id`).
2. **Pin the clock (Risk R1)** — both currently pass `TimeProvider.System` with
   `requestedStart = 2026-07-08T14:30:00Z`; today is 2026-07-08, so once the VR-06 guard exists these
   would fail whenever the suite runs after 14:30Z. Add a `FixedClock` and pass a `now` strictly
   before the fixture start.

```csharp
// new fake — deterministic clock for the past-start guard
private sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

// BuildHandler gains a `now` param (default safely before the fixtures' 14:30Z start) plus
// optional overrides for the failing signals each new test needs.
private static RequestAppointmentHandler BuildHandler(
    FakeAppointmentRepository repo,
    TimeSpan duration,
    DateTimeOffset? now = null,
    ServiceTypeInfo? serviceType = /* default: the Oil-change fixture */ null,
    DealershipBays? dealership = /* default: Springfield + Bay 3 */ null,
    VehicleOwnership ownership = VehicleOwnership.Owned,
    IReadOnlyList<TechnicianInfo>? technicians = /* default: [Alex Chen] */ null) =>
    new(new FakeCurrentUser(OwnerId),
        new FakeServiceTypeLookup(serviceType ?? DefaultServiceType(duration)),
        new FakeServiceBayLookup(dealership ?? DefaultDealership),
        new FakeVehicleOwnershipQuery(ownership),
        new FakeQualifiedTechnicianLookup(technicians ?? DefaultTechnicians),
        repo,
        new FixedClock(now ?? DateTimeOffset.Parse("2026-07-01T00:00:00Z")));
```

**New tests (AT-02..AT-06)** — each asserts the returned `BookingError.Code` (+ `HttpStatus`) and that
**nothing was persisted**. Assertion shape:
```csharp
result.IsFailed.Should().BeTrue();
var error = result.Errors.OfType<BookingError>().Single();
error.Code.Should().Be("SERVICE_TYPE_NOT_FOUND");
error.HttpStatus.Should().Be(404);
repo.Added.Should().BeNull();
```

| Test | Arrange (override) | Assert |
|---|---|---|
| `Unknown_service_type_returns_404_SERVICE_TYPE_NOT_FOUND` | `serviceType: null` | code `SERVICE_TYPE_NOT_FOUND`, 404, not persisted |
| `Past_requested_start_returns_400_REQUESTED_START_IN_PAST` | `now = 2026-07-08T12:00Z`, start `2026-07-08T11:59Z` | code `REQUESTED_START_IN_PAST`, 400, not persisted |
| `Unknown_dealership_returns_404_DEALERSHIP_NOT_FOUND` | `dealership: null` | code `DEALERSHIP_NOT_FOUND`, 404, not persisted |
| `Unknown_vehicle_returns_404_VEHICLE_NOT_FOUND` | `ownership: NotFound` | code `VEHICLE_NOT_FOUND`, 404, not persisted |
| `Vehicle_owned_by_another_caller_returns_403_VEHICLE_NOT_OWNED_BY_CALLER` | `ownership: NotOwned` | code `VEHICLE_NOT_OWNED_BY_CALLER`, 403, not persisted |

(Boundary check for VR-06 "strictly future" — `requestedStart == now` → rejected — is covered by the
`<=` comparison; optionally add an equal-instant case.)

**No change to `tests/AppointmentScheduler.Api.Tests/BookingEndpointsTests.cs`.** Its happy-path 201
body and 401 case are unaffected (success shape unchanged; auth is pipeline-level). Negative-path
integration tests are not required by the issue (unit-test seam) and are out of scope here.

## Key Files
- `source/AppointmentScheduler.Application/Features/Booking/BookingErrors.cs` — new; `BookingError : Error` + the 5 stable codes.
- `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — return type → `Result<…>`; 5 guards.
- `source/AppointmentScheduler.Api/Endpoints/BookingEndpoints.cs` — render success/`{code,message}` from `BookingError`.
- `tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs` — unwrap Result, pin clock, +5 tests.

## Testing & Verification
- **FluentResults 4.0.0 is already installed** (package references added to the projects) — no
  `dotnet add package` step. Only Application (handler + `BookingError`), Api (endpoint), and
  `Application.Tests` actually consume it for this slice.
- `dotnet build -c Release` passes.
- `dotnet test -c Release` passes: AT-01/AT-13 (updated) + AT-02..AT-06 (new) green; existing
  `BookingEndpointsTests` (201 + 401) still green.
- **No migration** — this slice changes no entity, EF configuration, or `AppDbContext`; do **not** run
  `dotnet ef migrations add`.
- **Manual check** (Development, optional): with the seeded data, `POST /api/appointments` while
  authenticated using —
  - an unknown `serviceTypeId` → `404 { "code": "SERVICE_TYPE_NOT_FOUND", "message": … }`
  - a past `requestedStart` → `400 { "code": "REQUESTED_START_IN_PAST", … }`
  - an unknown `dealershipId` → `404 DEALERSHIP_NOT_FOUND`
  - a `vehicleId` owned by another user → `403 VEHICLE_NOT_OWNED_BY_CALLER`
  - an unknown `vehicleId` → `404 VEHICLE_NOT_FOUND`
  and confirm a fully valid request still returns `201`.

## Branch & PR
- **This plan PR**: branch `plan/4`, title `docs: plan for booking request validation & failure responses (#4)`.
- **Implementation branch** (later, for `/implement-issue`): `feat/4-booking-request-validation`.
- Implementation PR title: `feat: booking request validation & failure responses (#4)`, body closes
  the issue (`Closes #4`) and notes the deferred 409s (#5).

## Notes / Risks surfaced
- **R1 — Latent time-dependency in the existing happy-path tests (must fix here).** AT-01/AT-13 use
  `requestedStart = 2026-07-08T14:30:00Z` with `TimeProvider.System`; today is 2026-07-08. `#3` has no
  past-start check so they pass regardless, but the VR-06 guard this slice adds would make them fail
  whenever the suite runs after 14:30Z. Pinning the clock (above) is therefore part of this slice, not
  optional cleanup.
- **R2 — Response-type change ripples to two callers only.** The handler's `TResponse` changes from
  `RequestAppointmentResponse` to `Result<RequestAppointmentResponse>`; the only touch-points are
  `BookingEndpoints` and the Application unit tests. The Api integration test declares its **own**
  private response record for JSON deserialization, so it is unaffected. Confirmed no other
  `sender.Send(new RequestAppointment(...))` or `.Handle(` call sites exist.
- **R3 — Deliberate divergence from the repo's error convention.** Booking errors use PRD §8's
  `{ code, message }` (via `Results.Json`), not `AuthEndpoints`' `Results.Problem`/ProblemDetails.
  This is intentional (D2) — §8 is the authoritative client contract — and should be called out in
  review so it isn't "corrected" to ProblemDetails.
- **R4 — FluentResults is a new shared dependency.** It was added to all projects; for #4 only
  Application, Api, and `Application.Tests` reference it in code. The `Domain` and `Infrastructure`
  package references are currently unused by this slice — harmless, and a candidate to trim if the
  team prefers minimal references, but #5 (and likely later slices) will consume the same `Result`
  seam from Application, so leaving it broadly available is defensible. `BookingError`/`Result` are the
  reuse point for #5's two 409s.
- **PRD AC-06 vs reality:** the PRD said "unit tests only, no `Api.Tests`", but a
  `BookingEndpointsTests` integration file already exists (added after #3). This plan honours the
  issue's explicit "unit tests" wording for the new coverage and simply keeps the pre-existing
  integration tests green.
```
