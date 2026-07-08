# Plan: Booking — happy-path appointment creation (foundation) (#3)

> **Issue**: https://github.com/ndaonguyen/ServiceScheduler/issues/3
> **Standalone**: this plan is executable without reading any other file.

## Goal
Stand up the minimal end-to-end skeleton for `POST /api/appointments` — the first vertical slice on
the modular-monolith skeleton — introducing the **Booking**, **Fleet**, **Workforce**, and
**Catalog** modules, their entities/tables, four cross-module query ports, Development seed data, and
an authenticated endpoint that persists a `Confirmed` appointment using a naive "first candidate"
bay/technician pick. **No conflict detection, no validation 4xx, no availability 409s** — those are
deferred to #4/#5/#6/#7.

## Scope & Non-goals

**In scope (this slice):**
- Domain entities across four modules: `Appointment` (Booking); `Vehicle`, `Dealership`,
  `ServiceBay` (Fleet); `Technician`, `TechnicianQualification` (Workforce); `ServiceType` (Catalog).
- EF Core configurations (snake_case) + one migration creating all seven tables.
- Four query ports in `Application/Abstractions/` with Infrastructure implementations in the owning
  module. **Ports carry display fields** (see Design Decision D1) so the endpoint can return the full
  PRD §8 response.
- `IAppointmentRepository` (Booking) + Infrastructure implementation.
- `RequestAppointment` request/handler (first handler in the app) + `RequestAppointmentResponse`.
- `BookingEndpoints` mapping `POST /api/appointments` with `.RequireAuthorization()`.
- Development-only reference-data seeding via `DbInitializer`.
- Unit tests in `AppointmentScheduler.Application.Tests` (first tests in that project) for AT-01 and
  AT-13.

**Out of scope (deferred — not missing):**
- Ownership/not-found/past-start validation and 4xx responses → **#4** (`VR-02..VR-06`, `AT-02..AT-06`).
- Real availability/overlap detection and the two 409s (`NO_BAY_AVAILABLE`, `NO_QUALIFIED_TECHNICIAN`) → **#5** (`AT-08..AT-12`).
- `EXCLUDE USING gist` DB constraint + `btree_gist` extension → **#6**. The migration here creates
  **plain** tables/FKs/indexes only.
- Retry-on-constraint-violation → **#7**.
- Management/CRUD endpoints for any entity; a separate `Customer` aggregate (ownership stays
  `Vehicle.OwnerId` → `AppUser.Id`).
- Integration tests / `WebApplicationFactory` coverage (AC-06 — unit tests only).
- Any domain event publishing (AC-05 — no `IEventPublisher`/outbox until a consumer exists).

## Design Decisions (resolved during planning)

- **D1 — Ports carry display fields (resolves the PRD §8 ⇄ §9 inconsistency).** PRD §8's 201 body
  needs `dealership.name`, `serviceType.name` + `durationMinutes`, `serviceBay.label`, and
  `technician.name`, but §9's port sketch (and the input's brainstorming) only listed ids/duration.
  The API contract (§8) is authoritative — "the API is the product." Resolution: **keep exactly four
  ports** (per the issue AC) but each owning-module lookup returns display fields, and the dealership's
  name is **folded into the bay lookup** (which already resolves the dealership and reports
  `dealership-not-found`). No fifth port.
- **D2 — Ownership port is called, without a guard branch.** `IVehicleOwnershipQuery` is built and
  implemented (issue AC), and the `#3` handler **calls** `CheckAsync` in sequence-diagram order and
  proceeds on the happy-path `Owned` result. The `403`/`404` guard branches are added in **#4** on the
  already-fetched result (purely additive). Seed data guarantees `Owned` for the AT-01 test.
- **D3 — `#3` handler returns the success response directly** (`Task<RequestAppointmentResponse>`),
  with no failure/result union. #4 (4xx) and #5 (409) will evolve the return type to a result union —
  that evolution is the natural seam, not scope this slice must pre-build. Candidate lists are assumed
  non-empty (seed guarantees it); empty-list handling belongs to #5.
- **D4 — Handler naming follows CLAUDE.md**: `Features/Booking/RequestAppointment.cs` holding the
  request record, response record, and `RequestAppointmentHandler` (the PRD's
  "CreateAppointmentHandler" label is illustrative).

## Requirement Traceability
| Issue acceptance criterion | Plan section | Verification |
|---|---|---|
| `POST /api/appointments` exists + `.RequireAuthorization()` | Changes → Api | OpenAPI doc lists it; manual `curl`; auth is the existing pipeline mechanism |
| Domain entities exist (7 across 4 modules) | Changes → Domain | `dotnet build -c Release` |
| EF configs + initial migration create all tables (snake_case) | Changes → Infrastructure | Migration file present; `dotnet build`; snake_case columns in generated `Up()` |
| Query ports exist + implemented (`IServiceTypeLookup`, `IServiceBayLookup`, `IVehicleOwnershipQuery`, `IQualifiedTechnicianLookup`) | Changes → Application + Infrastructure | `dotnet build`; DI resolves each impl |
| `DbInitializer` seeds ref data in Development | Changes → Infrastructure (seed) | Manual dev run; POST succeeds against seeded ids |
| Valid request → 201, persisted, `scheduledEnd = requestedStart + Duration` (AT-01, AT-13) | Changes → Application (handler) | `RequestAppointmentTests` (unit) |
| Owner id from `ICurrentUser`, never body (FR-02) | Changes → Application (handler) | Unit test asserts persisted `OwnerId` == current user; request record has no owner field |
| Unauthenticated rejected by pipeline (AT-07) | Changes → Api | Structural — `.RequireAuthorization()`, already exercised by `AuthEndpointsTests`; no new test |

## Changes

### Domain (`source/AppointmentScheduler.Domain/`)
> **Pattern to mimic**: `source/AppointmentScheduler.Infrastructure/Persistence/RefreshToken.cs` — a
> plain POCO aggregate with auto-properties, `default!` for required strings, no framework refs.
> **Note (first-of-kind):** the Domain project currently has **no entities** — these are the first.

- **New file** `Domain/Booking/Appointment.cs`
- **New file** `Domain/Booking/AppointmentStatus.cs` (enum, currently only `Confirmed`)
- **New file** `Domain/Fleet/Vehicle.cs`
- **New file** `Domain/Fleet/Dealership.cs`
- **New file** `Domain/Fleet/ServiceBay.cs`
- **New file** `Domain/Workforce/Technician.cs`
- **New file** `Domain/Workforce/TechnicianQualification.cs`
- **New file** `Domain/Catalog/ServiceType.cs`

```csharp
namespace AppointmentScheduler.Domain.Booking;

public enum AppointmentStatus { Confirmed }

public sealed class Appointment
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = default!;   // AppUser.Id (opaque, AC-04)
    public Guid VehicleId { get; set; }
    public Guid DealershipId { get; set; }
    public Guid ServiceTypeId { get; set; }
    public Guid ServiceBayId { get; set; }
    public Guid TechnicianId { get; set; }
    public DateTimeOffset ScheduledStart { get; set; }   // UTC
    public DateTimeOffset ScheduledEnd { get; set; }     // UTC
    public AppointmentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// Fleet
public sealed class Vehicle { public Guid Id; public string OwnerId; public string Make; public string Model; public int Year; public string Vin; } // as auto-props
public sealed class Dealership { public Guid Id; public string Name; public string Address; }
public sealed class ServiceBay { public Guid Id; public Guid DealershipId; public string Label; }
// Workforce
public sealed class Technician { public Guid Id; public Guid DealershipId; public string Name; }
public sealed class TechnicianQualification { public Guid TechnicianId; public Guid ServiceTypeId; }
// Catalog
public sealed class ServiceType { public Guid Id; public string Name; public TimeSpan Duration; }
```
(All cross-module references are **opaque Guids/strings** — no navigation properties between modules,
per AC-04. Written as real auto-properties in the actual files.)

### Application (`source/AppointmentScheduler.Application/`)

**Query ports + DTOs** — `Abstractions/` (shared contract; implemented in owning-module Infrastructure).
> **Pattern to mimic**: `Abstractions/ICurrentUser.cs` (port shape) and
> `Infrastructure/Security/RefreshTokenService.cs` (interface + result records co-located).

- **New file** `Abstractions/IServiceTypeLookup.cs`
- **New file** `Abstractions/IServiceBayLookup.cs`
- **New file** `Abstractions/IVehicleOwnershipQuery.cs`
- **New file** `Abstractions/IQualifiedTechnicianLookup.cs`
- **New file** `Abstractions/IAppointmentRepository.cs`

```csharp
namespace AppointmentScheduler.Application.Abstractions;

public interface IServiceTypeLookup
{   // null => not found (#4 turns null into 404 SERVICE_TYPE_NOT_FOUND)
    Task<ServiceTypeInfo?> GetAsync(Guid serviceTypeId, CancellationToken ct = default);
}
public sealed record ServiceTypeInfo(Guid Id, string Name, TimeSpan Duration);

public interface IServiceBayLookup
{   // null => dealership not found (#4 turns null into 404 DEALERSHIP_NOT_FOUND). D1: carries name.
    Task<DealershipBays?> ListByDealershipAsync(Guid dealershipId, CancellationToken ct = default);
}
public sealed record DealershipBays(string DealershipName, IReadOnlyList<BayInfo> Bays);
public sealed record BayInfo(Guid Id, string Label);

public enum VehicleOwnership { Owned, NotOwned, NotFound }
public interface IVehicleOwnershipQuery
{   // #3 proceeds on Owned; #4 maps NotOwned->403, NotFound->404.
    Task<VehicleOwnership> CheckAsync(Guid vehicleId, string ownerId, CancellationToken ct = default);
}

public interface IQualifiedTechnicianLookup
{   // empty => #5 turns into 409 NO_QUALIFIED_TECHNICIAN. D1: carries name.
    Task<IReadOnlyList<TechnicianInfo>> ListAsync(Guid dealershipId, Guid serviceTypeId, CancellationToken ct = default);
}
public sealed record TechnicianInfo(Guid Id, string Name);

public interface IAppointmentRepository   // Booking-owned; Domain type is same module
{
    Task AddAsync(AppointmentScheduler.Domain.Booking.Appointment appointment, CancellationToken ct = default);
}
```

**Handler** — `Features/Booking/RequestAppointment.cs`
> **Pattern to mimic**: **none yet — this is the first `IRequestHandler` in the app** (surfaced per
> plan process). Follow the contract in `Application/Messaging/IRequestHandler.cs`; handlers are
> auto-registered by reflection in `Application/DependencyInjection.cs` (no manual registration).

- **New file** `Features/Booking/RequestAppointment.cs` — request record, response record, handler.

```csharp
namespace AppointmentScheduler.Application.Features.Booking;

// Request binds directly from the POST body — note: NO owner field (FR-02, resolved from ICurrentUser).
public sealed record RequestAppointment(
    Guid VehicleId, Guid DealershipId, Guid ServiceTypeId, DateTimeOffset RequestedStart)
    : IRequest<RequestAppointmentResponse>;

public sealed record RequestAppointmentResponse(
    Guid AppointmentId,
    DealershipRef Dealership,
    ServiceTypeRef ServiceType,
    VehicleRef Vehicle,
    ServiceBayRef ServiceBay,
    TechnicianRef Technician,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    string Status);

public sealed record DealershipRef(Guid Id, string Name);
public sealed record ServiceTypeRef(Guid Id, string Name, int DurationMinutes);
public sealed record VehicleRef(Guid Id);
public sealed record ServiceBayRef(Guid Id, string Label);
public sealed record TechnicianRef(Guid Id, string Name);

internal sealed class RequestAppointmentHandler(
    ICurrentUser currentUser,
    IServiceTypeLookup serviceTypes,
    IServiceBayLookup bays,
    IVehicleOwnershipQuery ownership,
    IQualifiedTechnicianLookup technicians,
    IAppointmentRepository appointments,
    TimeProvider clock) : IRequestHandler<RequestAppointment, RequestAppointmentResponse>
{
    public async Task<RequestAppointmentResponse> Handle(RequestAppointment request, CancellationToken ct)
    {
        var ownerId = currentUser.UserId!;                       // #3: authenticated (RequireAuthorization)

        var serviceType = await serviceTypes.GetAsync(request.ServiceTypeId, ct);      // #3 happy: non-null
        var dealership  = await bays.ListByDealershipAsync(request.DealershipId, ct);   // #3 happy: non-null
        _ = await ownership.CheckAsync(request.VehicleId, ownerId, ct);                 // D2: Owned; proceed
        var techs       = await technicians.ListAsync(request.DealershipId, request.ServiceTypeId, ct);

        var bay = dealership!.Bays[0];                            // naive "first candidate" (D3; #5 adds selection)
        var tech = techs[0];
        var start = request.RequestedStart;
        var end = start + serviceType!.Duration;                 // BR-07 / AT-13

        var appointment = new Domain.Booking.Appointment
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, VehicleId = request.VehicleId,
            DealershipId = request.DealershipId, ServiceTypeId = request.ServiceTypeId,
            ServiceBayId = bay.Id, TechnicianId = tech.Id,
            ScheduledStart = start, ScheduledEnd = end,
            Status = Domain.Booking.AppointmentStatus.Confirmed, CreatedAt = clock.GetUtcNow(),
        };
        await appointments.AddAsync(appointment, ct);

        return new RequestAppointmentResponse(
            appointment.Id,
            new DealershipRef(request.DealershipId, dealership.DealershipName),
            new ServiceTypeRef(serviceType.Id, serviceType.Name, (int)serviceType.Duration.TotalMinutes),
            new VehicleRef(request.VehicleId),
            new ServiceBayRef(bay.Id, bay.Label),
            new TechnicianRef(tech.Id, tech.Name),
            start, end, appointment.Status.ToString());
    }
}
```

### Infrastructure (`source/AppointmentScheduler.Infrastructure/`)

**DbContext** — add `DbSet`s to `Persistence/AppDbContext.cs`:
`Appointments`, `Vehicles`, `Dealerships`, `ServiceBays`, `Technicians`, `TechnicianQualifications`,
`ServiceTypes`. (Configs are still picked up by the existing
`ApplyConfigurationsFromAssembly` call — no change needed there.)

**EF configurations** — `Persistence/Configurations/` (one per aggregate, snake_case).
> **Pattern to mimic**: `Persistence/Configurations/RefreshTokenConfiguration.cs` (`ToTable`,
> `HasColumnName` snake_case, `HasKey`, `HasIndex`).

- **New** `AppointmentConfiguration.cs` — table `appointments`; `status` stored as text via
  `HasConversion<string>()`; indexes on `service_bay_id`, `technician_id` (plain — **no** gist/EXCLUDE, #6).
- **New** `VehicleConfiguration.cs` — table `vehicles`.
- **New** `DealershipConfiguration.cs` — table `dealerships`.
- **New** `ServiceBayConfiguration.cs` — table `service_bays`.
- **New** `TechnicianConfiguration.cs` — table `technicians`.
- **New** `TechnicianQualificationConfiguration.cs` — table `technician_qualifications`; composite key
  `(technician_id, service_type_id)`.
- **New** `ServiceTypeConfiguration.cs` — table `service_types`; `duration` as Postgres `interval` (Npgsql maps `TimeSpan`).

**Port implementations** — per module folder.
> **Pattern to mimic**: `Infrastructure/Security/RefreshTokenService.cs` (constructor-injected
> `AppDbContext`, EF queries) and the DI in `Infrastructure/DependencyInjection.cs`.

- **New** `Catalog/ServiceTypeLookup.cs` : `IServiceTypeLookup` — `ServiceTypes.FindAsync`/projection.
- **New** `Fleet/ServiceBayLookup.cs` : `IServiceBayLookup` — loads `Dealership` (name) + its
  `ServiceBay`s; returns `null` if the dealership is unknown.
- **New** `Fleet/VehicleOwnershipQuery.cs` : `IVehicleOwnershipQuery` — `NotFound` if no vehicle,
  else `Owned`/`NotOwned` by comparing `OwnerId`.
- **New** `Workforce/QualifiedTechnicianLookup.cs` : `IQualifiedTechnicianLookup` — join
  `Technician` × `TechnicianQualification` filtered by dealership + service type.
- **New** `Booking/AppointmentRepository.cs` : `IAppointmentRepository` — `Add` + `SaveChangesAsync`.

**DI registration** — `Infrastructure/DependencyInjection.cs`: register all five above as `AddScoped`.

**Seeding** — extend `Persistence/DbInitializer.cs`.
> **Pattern to mimic**: existing `MigrateAndSeedAsync` (idempotent role/admin seeding).

- Add a `SeedReferenceDataAsync` step invoked at the end of `MigrateAndSeedAsync` (already
  Development-only via `Program.cs`). Idempotent (guard on "any dealership exists"). Uses **fixed
  Guids** so manual testing can reference known ids. Ensures a dev customer `AppUser` exists (role
  `user`), then seeds: 1–2 `ServiceType`s, 1 `Dealership` with ≥1 `ServiceBay`, ≥1 `Technician` with a
  matching `TechnicianQualification`, and ≥1 `Vehicle` owned by the dev customer — enough that a POST
  with the seeded ids satisfies AT-01/AT-13 by hand.

### Api (`source/AppointmentScheduler.Api/`)

**Endpoint** — `Endpoints/BookingEndpoints.cs`.
> **Pattern to mimic**: `Endpoints/ProfileEndpoints.cs` (group + `.RequireAuthorization()`) and
> `Endpoints/AuthEndpoints.cs` (`MapGroup`, `ISender` not yet used elsewhere — first `ISender.Send`).

- **New file** `Endpoints/BookingEndpoints.cs`:

```csharp
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments").WithTags("Booking");

        group.MapPost("", async (RequestAppointment body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(body, ct);
            return Results.Created($"/api/appointments/{result.AppointmentId}", result);
        })
        .WithName("RequestAppointment")
        .RequireAuthorization();

        return app;
    }
}
```
(The Application `RequestAppointment` record binds directly from the JSON body — its four fields are
exactly the wire contract, and owner is resolved server-side, so no separate Api DTO is needed.)

- **Edit** `Program.cs`: add `app.MapBookingEndpoints();` alongside `MapAuthEndpoints()`/`MapProfileEndpoints()`.

### Tests (`tests/AppointmentScheduler.Application.Tests/`)
> **Pattern to mimic**: **no existing unit tests in this project** (surfaced per plan process — first
> tests here). Style reference for xUnit + AwesomeAssertions: `Api.Tests/ProfileEndpointsTests.cs`.
> No mocking library is used in this repo — write small hand-rolled fakes.

- **New file** `Booking/RequestAppointmentTests.cs` with hand-written fakes for the four ports,
  `IAppointmentRepository` (captures the added `Appointment`), `ICurrentUser` (fixed `UserId`), and a
  `TimeProvider` (`TimeProvider.System` is fine — `CreatedAt` is not asserted).
  - **AT-01** — happy path: given the fakes return a service type, one bay, `Owned`, and one
    technician, `Handle` returns a response whose `ServiceBay`/`Technician` match the single
    candidates, and the fake repository received an `Appointment` with `OwnerId` == current user,
    `Status == Confirmed`, and the requested vehicle/dealership/bay/tech.
  - **AT-13** — duration: `ServiceType.Duration = 45 min`, `RequestedStart = T` ⇒
    `ScheduledEnd == T + 45 min` (and response `ServiceType.DurationMinutes == 45`).

## Key Files
- `source/AppointmentScheduler.Domain/{Booking,Fleet,Workforce,Catalog}/*.cs` — new entities.
- `source/AppointmentScheduler.Application/Abstractions/*.cs` — 4 query ports + `IAppointmentRepository` + DTOs.
- `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — first handler.
- `source/AppointmentScheduler.Infrastructure/Persistence/AppDbContext.cs` — new `DbSet`s.
- `source/AppointmentScheduler.Infrastructure/Persistence/Configurations/*.cs` — 7 new configs.
- `source/AppointmentScheduler.Infrastructure/{Catalog,Fleet,Workforce,Booking}/*.cs` — port/repo impls.
- `source/AppointmentScheduler.Infrastructure/Persistence/DbInitializer.cs` — reference-data seed.
- `source/AppointmentScheduler.Infrastructure/Migrations/<ts>_BookingFoundation.*` — generated migration.
- `source/AppointmentScheduler.Api/Endpoints/BookingEndpoints.cs` + `Program.cs` — endpoint + wiring.
- `tests/AppointmentScheduler.Application.Tests/Booking/RequestAppointmentTests.cs` — new unit tests.

## Testing & Verification
- `dotnet build -c Release` passes.
- `dotnet test -c Release` passes; new `RequestAppointmentTests` (AT-01, AT-13) green.
- **Migration** generated (after all code compiles):
  ```
  dotnet ef migrations add BookingFoundation --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
  ```
  Review the generated `Up()` — seven `CreateTable`s, snake_case columns, plain FKs/indexes, **no**
  `btree_gist`/`EXCLUDE` (that is #6). Commit the generated files.
- **Manual check** (Development): `docker compose up` Postgres, run the API (auto-migrates + seeds),
  `POST /api/appointments` with a seeded `vehicleId`/`dealershipId`/`serviceTypeId` and a future
  `requestedStart` while authenticated (login via `/api/auth`), expect `201` with the assigned bay +
  technician and `scheduledEnd = requestedStart + duration`.

## Branch & PR
- **Implementation branch** (for `/implement-issue`): `feat/3-booking-happy-path`
- **PR title**: `feat: booking happy-path appointment creation (#3)`
- PR body should close the issue (`Closes #3`) and note the deferred slices (#4/#5/#6/#7).

## Notes / Risks surfaced
- **Two "first-of-kind" changes** (no existing analogue): the first `IRequestHandler`
  (`RequestAppointment`) and the first tests in `Application.Tests`. Expected — this is the PRD's
  "first vertical slice built on the skeleton" — but called out so implementation follows the mediator
  contract and xUnit/AwesomeAssertions conventions rather than an in-repo sibling.
- **Single shared `AppDbContext`** holds all modules' `DbSet`s — consistent with CLAUDE.md
  ("slices persist through it via repositories"); module ownership is enforced by folder/handler
  boundaries and the query-port rule (AC-04), not by separate contexts.
- **Deviation from the input's brainstorming port sketch** (`bayIds`/`technicianIds`/`duration`): the
  ports return display fields instead (Design Decision D1) because PRD §8's response contract outranks
  the brainstorming notes. The input doc is being updated to match.
```
