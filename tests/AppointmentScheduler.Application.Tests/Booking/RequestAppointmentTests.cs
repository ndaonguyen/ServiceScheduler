using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Features.Booking;
using AppointmentScheduler.Domain.Booking;
using AwesomeAssertions;
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

    // AT-01: happy path — persists a confirmed appointment and returns the assigned bay + technician.
    [Fact]
    public async Task Happy_path_persists_confirmed_appointment_with_assigned_bay_and_technician()
    {
        var repo = new FakeAppointmentRepository();
        var handler = BuildHandler(repo, duration: TimeSpan.FromMinutes(45));
        var start = DateTimeOffset.Parse("2026-07-08T14:30:00Z");

        var response = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, start), CancellationToken.None);

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
        var start = DateTimeOffset.Parse("2026-07-08T14:30:00Z");

        var response = await handler.Handle(
            new RequestAppointment(VehicleId, DealershipId, ServiceTypeId, start), CancellationToken.None);

        response.ScheduledStart.Should().Be(start);
        response.ScheduledEnd.Should().Be(start.AddMinutes(45));
        repo.Added!.ScheduledStart.Should().Be(start);
        repo.Added.ScheduledEnd.Should().Be(start.AddMinutes(45));
    }

    private static RequestAppointmentHandler BuildHandler(FakeAppointmentRepository repo, TimeSpan duration) =>
        new(
            new FakeCurrentUser(OwnerId),
            new FakeServiceTypeLookup(new ServiceTypeInfo(ServiceTypeId, "Oil change", duration)),
            new FakeServiceBayLookup(new DealershipBays("Springfield Downtown", [new BayInfo(BayId, "Bay 3")])),
            new FakeVehicleOwnershipQuery(VehicleOwnership.Owned),
            new FakeQualifiedTechnicianLookup([new TechnicianInfo(TechId, "Alex Chen")]),
            repo,
            TimeProvider.System);

    // --- hand-rolled fakes (no mocking library in this repo) ---

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
