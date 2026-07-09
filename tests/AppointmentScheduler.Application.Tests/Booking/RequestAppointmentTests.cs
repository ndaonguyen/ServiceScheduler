using AppointmentScheduler.Booking.Application.Abstractions;
using AppointmentScheduler.Booking.Application.Features;
using AppointmentScheduler.Booking.Domain;
using AppointmentScheduler.BuildingBlocks.Abstractions;
using AppointmentScheduler.Catalog.Contracts;
using AppointmentScheduler.Fleet.Contracts;
using AppointmentScheduler.Workforce.Contracts;
using AwesomeAssertions;
using FluentResults;
using Xunit;

namespace AppointmentScheduler.Application.Tests.Booking;

public class RequestAppointmentTests
{
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid DealershipId = Guid.NewGuid();
    private static readonly Guid ServiceTypeId = Guid.NewGuid();
    private static readonly Guid BayId = Guid.NewGuid();
    private static readonly Guid TechId = Guid.NewGuid();
    private static readonly Guid Bay2Id = Guid.NewGuid();
    private static readonly Guid Tech2Id = Guid.NewGuid();
    private const string OwnerId = "user-123";

    // A fixed "now" safely before the fixtures' requestedStart, so the VR-06 past-start guard does
    // not make the happy-path tests depend on the wall clock (see plan Risk R1).
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static readonly DateTimeOffset FutureStart = DateTimeOffset.Parse("2026-07-08T14:30:00Z");

    // AT-01: happy path — persists a confirmed appointment and returns the assigned bay + technician.
    [Fact]
    public async Task Happy_path_persists_confirmed_appointment_with_assigned_bay_and_technician()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value;

        // Response reflects the auto-assigned resources and their display fields.
        response.Dealership.Id.Should().Be(DealershipId);
        response.Dealership.Name.Should().Be("Springfield Downtown");
        response.ServiceType.Name.Should().Be("Oil change");
        response.ServiceType.DurationMinutes.Should().Be(45);
        response.Vehicle.Id.Should().Be(VehicleId);
        response.ServiceBay.Id.Should().Be(BayId);
        response.ServiceBay.Label.Should().Be("Bay 3");
        response.Technician.Id.Should().Be(TechId);
        response.Technician.Name.Should().Be("Alex Chen");
        response.Status.Should().Be("Confirmed");

        // Appointment persisted with the caller (from ICurrentUser) as owner and the selected bay/tech.
        repo.Added.Should().NotBeNull();
        repo.Added!.OwnerId.Should().Be(OwnerId);
        repo.Added.VehicleId.Should().Be(VehicleId);
        repo.Added.DealershipId.Should().Be(DealershipId);
        repo.Added.ServiceTypeId.Should().Be(ServiceTypeId);
        repo.Added.ServiceBayId.Should().Be(BayId);
        repo.Added.TechnicianId.Should().Be(TechId);
        repo.Added.Status.Should().Be(AppointmentStatus.Confirmed);
        response.AppointmentId.Should().Be(repo.Added.Id);
    }

    // AT-13 / BR-07: end = start + ServiceType.Duration (client supplies no duration).
    [Fact]
    public async Task ScheduledEnd_is_start_plus_service_duration()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ScheduledStart.Should().Be(FutureStart);
        result.Value.ScheduledEnd.Should().Be(FutureStart.AddMinutes(45));
        repo.Added!.ScheduledStart.Should().Be(FutureStart);
        repo.Added.ScheduledEnd.Should().Be(FutureStart.AddMinutes(45));
    }

    // AT-05 / VR-05: unknown service type -> 404 SERVICE_TYPE_NOT_FOUND; nothing persisted.
    [Fact]
    public async Task Unknown_service_type_returns_404_SERVICE_TYPE_NOT_FOUND()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45), noServiceType: true);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "SERVICE_TYPE_NOT_FOUND", 404);
        repo.Added.Should().BeNull();
    }

    // AT-06 / VR-06: requestedStart not strictly in the future -> 400 REQUESTED_START_IN_PAST.
    [Fact]
    public async Task Past_requested_start_returns_400_REQUESTED_START_IN_PAST()
    {
        var repo = new FakeAppointmentRepository();
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var pastStart = DateTimeOffset.Parse("2026-07-08T11:59:00Z");
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45), now: now);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, pastStart), CancellationToken.None);

        ShouldFailWith(result, "REQUESTED_START_IN_PAST", 400);
        repo.Added.Should().BeNull();
    }

    // AT-04 / VR-04: unknown dealership -> 404 DEALERSHIP_NOT_FOUND.
    [Fact]
    public async Task Unknown_dealership_returns_404_DEALERSHIP_NOT_FOUND()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45), noDealership: true);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "DEALERSHIP_NOT_FOUND", 404);
        repo.Added.Should().BeNull();
    }

    // AT-02 / VR-02: unknown vehicle -> 404 VEHICLE_NOT_FOUND.
    [Fact]
    public async Task Unknown_vehicle_returns_404_VEHICLE_NOT_FOUND()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45), ownership: VehicleOwnership.NotFound);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "VEHICLE_NOT_FOUND", 404);
        repo.Added.Should().BeNull();
    }

    // AT-03 / VR-03: vehicle owned by a different customer -> 403 VEHICLE_NOT_OWNED_BY_CALLER.
    [Fact]
    public async Task Vehicle_owned_by_another_caller_returns_403_VEHICLE_NOT_OWNED_BY_CALLER()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45), ownership: VehicleOwnership.NotOwned);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "VEHICLE_NOT_OWNED_BY_CALLER", 403);
        repo.Added.Should().BeNull();
    }

    // AT-08 / BR-01: the only qualified technician is busy for the window -> 409. (Bay stays free —
    // the seeded appointment's bay is a non-candidate id.)
    [Fact]
    public async Task All_qualified_technicians_busy_returns_409_NO_QUALIFIED_TECHNICIAN()
    {
        var repo = new FakeAppointmentRepository(
            Confirmed(bayId: Guid.NewGuid(), technicianId: TechId, FutureStart, FutureStart.AddMinutes(45)));
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "NO_QUALIFIED_TECHNICIAN", 409);
        repo.Added.Should().BeNull();
    }

    // AT-08 (other half): no technician is qualified at all -> same 409.
    [Fact]
    public async Task No_qualified_technician_exists_returns_409_NO_QUALIFIED_TECHNICIAN()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45), noTechnicians: true);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "NO_QUALIFIED_TECHNICIAN", 409);
        repo.Added.Should().BeNull();
    }

    // AT-09 / BR-02: the only bay is busy for the window -> 409. (Technician stays free.)
    [Fact]
    public async Task All_bays_busy_returns_409_NO_BAY_AVAILABLE()
    {
        var repo = new FakeAppointmentRepository(
            Confirmed(bayId: BayId, technicianId: Guid.NewGuid(), FutureStart, FutureStart.AddMinutes(45)));
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "NO_BAY_AVAILABLE", 409);
        repo.Added.Should().BeNull();
    }

    // AT-10 / BR-03: an existing appointment ending exactly at the requested start does not conflict.
    [Fact]
    public async Task Appointment_ending_at_requested_start_does_not_conflict()
    {
        var repo = new FakeAppointmentRepository(
            Confirmed(bayId: BayId, technicianId: TechId, FutureStart.AddMinutes(-45), FutureStart));
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ServiceBay.Id.Should().Be(BayId);
        result.Value.Technician.Id.Should().Be(TechId);
        repo.Added.Should().NotBeNull();
    }

    // AT-11 / BR-03: an existing appointment [T, T+D) conflicts with a request overlapping by 1s on the
    // same bay -> 409 NO_BAY_AVAILABLE. (Seeded on a non-candidate technician so the bay is the shortage.)
    [Fact]
    public async Task Appointment_overlapping_by_one_second_conflicts_returns_409_NO_BAY_AVAILABLE()
    {
        var repo = new FakeAppointmentRepository(
            Confirmed(bayId: BayId, technicianId: Guid.NewGuid(), FutureStart, FutureStart.AddMinutes(45)));
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart.AddSeconds(-1)),
            CancellationToken.None);

        ShouldFailWith(result, "NO_BAY_AVAILABLE", 409);
        repo.Added.Should().BeNull();
    }

    // AT-12 / BR-05/BR-06: a busy appointment on another dealership's resources (ids never returned by
    // the lookups) is never a candidate, so the requested dealership's free resource is assigned.
    [Fact]
    public async Task Only_requested_dealership_resources_are_candidates()
    {
        var repo = new FakeAppointmentRepository(
            Confirmed(bayId: Guid.NewGuid(), technicianId: Guid.NewGuid(), FutureStart, FutureStart.AddMinutes(45)));
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ServiceBay.Id.Should().Be(BayId);
        result.Value.Technician.Id.Should().Be(TechId);
        repo.Added.Should().NotBeNull();
    }

    // #7 / AT: insert loses the race on the bay, retry with the next free bay succeeds -> 201.
    [Fact]
    public async Task Bay_conflict_then_retry_succeeds_returns_201_with_next_bay()
    {
        var repo = new FakeAppointmentRepository { ConflictOnFirstInsert = BookingResource.ServiceBay };
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45),
            bays: [new BayInfo(BayId, "Bay 3"), new BayInfo(Bay2Id, "Bay 4")]);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.AddCalls.Should().Be(2);
        result.Value.ServiceBay.Id.Should().Be(Bay2Id);
        repo.Added!.ServiceBayId.Should().Be(Bay2Id);
    }

    // #7 / AT: insert loses the race on the technician, retry with the next free technician succeeds -> 201.
    [Fact]
    public async Task Technician_conflict_then_retry_succeeds_returns_201_with_next_technician()
    {
        var repo = new FakeAppointmentRepository { ConflictOnFirstInsert = BookingResource.Technician };
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45),
            technicians: [new TechnicianInfo(TechId, "Alex Chen"), new TechnicianInfo(Tech2Id, "Sam Lee")]);

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.AddCalls.Should().Be(2);
        result.Value.Technician.Id.Should().Be(Tech2Id);
        repo.Added!.TechnicianId.Should().Be(Tech2Id);
    }

    // #7 / AT: bay conflict with no other free bay -> 409 NO_BAY_AVAILABLE; nothing persisted.
    [Fact]
    public async Task Bay_conflict_with_no_other_bay_returns_409_NO_BAY_AVAILABLE()
    {
        var repo = new FakeAppointmentRepository { ConflictOnFirstInsert = BookingResource.ServiceBay };
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45)); // single bay

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "NO_BAY_AVAILABLE", 409);
        repo.Added.Should().BeNull();
    }

    // #7 / AT: technician conflict with no other free technician -> 409 NO_QUALIFIED_TECHNICIAN.
    [Fact]
    public async Task Technician_conflict_with_no_other_technician_returns_409_NO_QUALIFIED_TECHNICIAN()
    {
        var repo = new FakeAppointmentRepository { ConflictOnFirstInsert = BookingResource.Technician };
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45)); // single technician

        var result = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, FutureStart), CancellationToken.None);

        ShouldFailWith(result, "NO_QUALIFIED_TECHNICIAN", 409);
        repo.Added.Should().BeNull();
    }

    private static void ShouldFailWith(Result<RequestAppointmentResponse> result, string code, int httpStatus)
    {
        result.IsFailed.Should().BeTrue();
        var error = result.Errors.OfType<BookingError>().Single();
        error.Code.Should().Be(code);
        error.HttpStatus.Should().Be(httpStatus);
    }

    private static RequestAppointmentHandler BuildHandler(
        FakeAppointmentRepository repo,
        TimeSpan duration,
        DateTimeOffset? now = null,
        bool noServiceType = false,
        bool noDealership = false,
        bool noTechnicians = false,
        VehicleOwnership ownership = VehicleOwnership.Owned,
        IReadOnlyList<BayInfo>? bays = null,
        IReadOnlyList<TechnicianInfo>? technicians = null) =>
        new(
            new FakeCurrentUser(OwnerId),
            new FakeServiceTypeLookup(noServiceType ? null : new ServiceTypeInfo(ServiceTypeId, "Oil change", duration)),
            new FakeServiceBayLookup(noDealership ? null : new DealershipBays("Springfield Downtown", bays ?? [new BayInfo(BayId, "Bay 3")])),
            new FakeVehicleOwnershipQuery(ownership),
            new FakeQualifiedTechnicianLookup(noTechnicians ? [] : (technicians ?? [new TechnicianInfo(TechId, "Alex Chen")])),
            repo,
            new FixedClock(now ?? Now));

    // A confirmed appointment occupying the given bay/technician over [start, end).
    private static Appointment Confirmed(Guid bayId, Guid technicianId, DateTimeOffset start, DateTimeOffset end) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = "other-owner",
            VehicleId = Guid.NewGuid(),
            DealershipId = DealershipId,
            ServiceTypeId = ServiceTypeId,
            ServiceBayId = bayId,
            TechnicianId = technicianId,
            ScheduledStart = start,
            ScheduledEnd = end,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = start,
        };

    // --- hand-rolled fakes (no mocking library in this repo) ---

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
        public bool IsAuthenticated => userId is not null;
        public bool IsInRole(string role) => false;
    }

    private sealed class FakeServiceTypeLookup(ServiceTypeInfo? info) : IServiceTypeLookup
    {
        public Task<ServiceTypeInfo?> GetAsync(Guid serviceTypeId, CancellationToken ct = default) =>
            Task.FromResult(info);
    }

    private sealed class FakeServiceBayLookup(DealershipBays? bays) : IServiceBayLookup
    {
        public Task<DealershipBays?> ListByDealershipAsync(Guid dealershipId, CancellationToken ct = default) =>
            Task.FromResult(bays);
    }

    private sealed class FakeVehicleOwnershipQuery(VehicleOwnership result) : IVehicleOwnershipQuery
    {
        public Task<VehicleOwnership> CheckAsync(Guid vehicleId, string ownerId, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeQualifiedTechnicianLookup(IReadOnlyList<TechnicianInfo> techs) : IQualifiedTechnicianLookup
    {
        public Task<IReadOnlyList<TechnicianInfo>> ListAsync(Guid dealershipId, Guid serviceTypeId, CancellationToken ct = default) =>
            Task.FromResult(techs);
    }

    private sealed class FakeAppointmentRepository(params Appointment[] existing) : IAppointmentRepository
    {
        public Appointment? Added { get; private set; }
        public int AddCalls { get; private set; }

        /// <summary>When set, the first <see cref="AddAsync"/> call throws a conflict for this resource.</summary>
        public BookingResource? ConflictOnFirstInsert { get; init; }

        public Task AddAsync(Appointment appointment, CancellationToken ct = default)
        {
            AddCalls++;
            if (ConflictOnFirstInsert is { } resource && AddCalls == 1)
            {
                throw new AppointmentSlotConflictException(resource);
            }

            Added = appointment;
            return Task.CompletedTask;
        }

        public Task<BusyResources> GetBusyResourcesAsync(
            IReadOnlyCollection<Guid> candidateBayIds,
            IReadOnlyCollection<Guid> candidateTechnicianIds,
            DateTimeOffset start,
            DateTimeOffset end,
            CancellationToken ct = default)
        {
            // Reuse the production overlap expression (compiled) so AT-10/AT-11 pin the real BR-03
            // rule, not a test-double reimplementation.
            var overlaps = AppointmentOverlap.Within(start, end).Compile();
            var hits = existing.Where(a => a.Status == AppointmentStatus.Confirmed && overlaps(a)).ToList();
            return Task.FromResult(new BusyResources(
                hits.Select(a => a.ServiceBayId).Where(candidateBayIds.Contains).ToHashSet(),
                hits.Select(a => a.TechnicianId).Where(candidateTechnicianIds.Contains).ToHashSet()));
        }
    }
}
