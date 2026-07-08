using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Features.Booking;
using AppointmentScheduler.Domain.Booking;
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
        VehicleOwnership ownership = VehicleOwnership.Owned) =>
        new(
            new FakeCurrentUser(OwnerId),
            new FakeServiceTypeLookup(noServiceType ? null : new ServiceTypeInfo(ServiceTypeId, "Oil change", duration)),
            new FakeServiceBayLookup(noDealership ? null : new DealershipBays("Springfield Downtown", [new BayInfo(BayId, "Bay 3")])),
            new FakeVehicleOwnershipQuery(ownership),
            new FakeQualifiedTechnicianLookup([new TechnicianInfo(TechId, "Alex Chen")]),
            repo,
            new FixedClock(now ?? Now));

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

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        public Appointment? Added { get; private set; }

        public Task AddAsync(Appointment appointment, CancellationToken ct = default)
        {
            Added = appointment;
            return Task.CompletedTask;
        }
    }
}
