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

public class RescheduleAppointmentTests
{
    private static readonly Guid DealershipId = Guid.NewGuid();
    private static readonly Guid ServiceTypeId = Guid.NewGuid();
    private static readonly Guid BayId = Guid.NewGuid();
    private static readonly Guid Bay2Id = Guid.NewGuid();
    private static readonly Guid TechId = Guid.NewGuid();
    private static readonly Guid Tech2Id = Guid.NewGuid();
    private const string OwnerId = "user-123";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static readonly DateTimeOffset OriginalStart = DateTimeOffset.Parse("2026-07-08T09:00:00Z");
    private static readonly DateTimeOffset NewStart = DateTimeOffset.Parse("2026-07-09T14:30:00Z");
    private static readonly DateTimeOffset PastStart = DateTimeOffset.Parse("2026-06-01T09:00:00Z");

    // Happy path: the appointment moves to the new window, keeps its identity and Confirmed status,
    // and the availability check excludes the appointment's own current slot (self-overlap guard).
    [Fact]
    public async Task Reschedule_moves_to_new_window_keeps_id_and_excludes_own_slot()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, OriginalStart);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, NewStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AppointmentId.Should().Be(appointment.Id);            // identity stable
        result.Value.ScheduledStart.Should().Be(NewStart);
        result.Value.ScheduledEnd.Should().Be(NewStart.AddMinutes(45));    // end = start + duration (BR-07)
        result.Value.Status.Should().Be("Confirmed");
        appointment.ScheduledStart.Should().Be(NewStart);
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        repo.Updated.Should().BeSameAs(appointment);
        repo.LastExcludeId.Should().Be(appointment.Id);                    // its own slot is excluded
    }

    // Unknown appointment id -> 404; nothing persisted.
    [Fact]
    public async Task Unknown_appointment_returns_404_APPOINTMENT_NOT_FOUND()
    {
        var repo = new FakeAppointmentRepository(seed: null);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(Guid.NewGuid(), NewStart), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_NOT_FOUND", 404);
        repo.Updated.Should().BeNull();
    }

    // Appointment owned by a different customer -> 403.
    [Fact]
    public async Task Appointment_owned_by_another_caller_returns_403_APPOINTMENT_NOT_OWNED_BY_CALLER()
    {
        var appointment = ConfirmedOwnedBy("someone-else", OriginalStart);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, NewStart), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_NOT_OWNED_BY_CALLER", 403);
        repo.Updated.Should().BeNull();
    }

    // A cancelled appointment cannot be rescheduled -> 409.
    [Fact]
    public async Task Cancelled_appointment_returns_409_APPOINTMENT_ALREADY_CANCELLED()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, OriginalStart);
        appointment.Cancel(Now);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, NewStart), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_ALREADY_CANCELLED", 409);
        repo.Updated.Should().BeNull();
    }

    // Target start not strictly in the future -> 409.
    [Fact]
    public async Task New_start_in_past_returns_409_APPOINTMENT_IN_PAST()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, OriginalStart);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, PastStart), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_IN_PAST", 409);
        repo.Updated.Should().BeNull();
    }

    // The only qualified technician is busy for the new window -> 409.
    [Fact]
    public async Task No_free_technician_returns_409_NO_QUALIFIED_TECHNICIAN()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, OriginalStart);
        var repo = new FakeAppointmentRepository(appointment)
        {
            Busy = new BusyResources(new HashSet<Guid>(), new HashSet<Guid> { TechId }),
        };
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, NewStart), CancellationToken.None);

        ShouldFailWith(result, "NO_QUALIFIED_TECHNICIAN", 409);
        repo.Updated.Should().BeNull();
    }

    // The only bay is busy for the new window -> 409.
    [Fact]
    public async Task No_free_bay_returns_409_NO_BAY_AVAILABLE()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, OriginalStart);
        var repo = new FakeAppointmentRepository(appointment)
        {
            Busy = new BusyResources(new HashSet<Guid> { BayId }, new HashSet<Guid>()),
        };
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, NewStart), CancellationToken.None);

        ShouldFailWith(result, "NO_BAY_AVAILABLE", 409);
        repo.Updated.Should().BeNull();
    }

    // The update loses the race on the bay; retry with the next free bay succeeds.
    [Fact]
    public async Task Bay_conflict_then_retry_succeeds_with_next_bay()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, OriginalStart);
        var repo = new FakeAppointmentRepository(appointment) { ConflictOnFirstUpdate = BookingResource.ServiceBay };
        var handler = BuildHandler(repo, bays: [new BayInfo(BayId, "Bay 3"), new BayInfo(Bay2Id, "Bay 4")]);

        var result = await handler.Handle(new RescheduleAppointment(appointment.Id, NewStart), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(2);
        result.Value.ServiceBay.Id.Should().Be(Bay2Id);
        appointment.ServiceBayId.Should().Be(Bay2Id);
    }

    private static RescheduleAppointmentHandler BuildHandler(
        FakeAppointmentRepository repo,
        IReadOnlyList<BayInfo>? bays = null,
        IReadOnlyList<TechnicianInfo>? technicians = null,
        bool noServiceType = false,
        bool noDealership = false) =>
        new(
            new FakeCurrentUser(OwnerId),
            new FakeServiceTypeLookup(noServiceType ? null : new ServiceTypeInfo(ServiceTypeId, "Oil change", TimeSpan.FromMinutes(45))),
            new FakeServiceBayLookup(noDealership ? null : new DealershipBays("Springfield Downtown", bays ?? [new BayInfo(BayId, "Bay 3")])),
            new FakeQualifiedTechnicianLookup(technicians ?? [new TechnicianInfo(TechId, "Alex Chen")]),
            repo,
            new FixedClock(Now));

    private static Appointment ConfirmedOwnedBy(string ownerId, DateTimeOffset start) =>
        Appointment.Schedule(ownerId, Guid.NewGuid(), DealershipId, ServiceTypeId, BayId, TechId,
            start, TimeSpan.FromMinutes(45), createdAt: start);

    private static void ShouldFailWith(Result<RequestAppointmentResponse> result, string code, int httpStatus)
    {
        result.IsFailed.Should().BeTrue();
        var error = result.Errors.OfType<BookingError>().Single();
        error.Code.Should().Be(code);
        error.HttpStatus.Should().Be(httpStatus);
    }

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
        public Task<ServiceTypeInfo?> GetAsync(Guid serviceTypeId, CancellationToken ct = default) => Task.FromResult(info);
    }

    private sealed class FakeServiceBayLookup(DealershipBays? bays) : IServiceBayLookup
    {
        public Task<DealershipBays?> ListByDealershipAsync(Guid dealershipId, CancellationToken ct = default) => Task.FromResult(bays);
    }

    private sealed class FakeQualifiedTechnicianLookup(IReadOnlyList<TechnicianInfo> techs) : IQualifiedTechnicianLookup
    {
        public Task<IReadOnlyList<TechnicianInfo>> ListAsync(Guid dealershipId, Guid serviceTypeId, CancellationToken ct = default) =>
            Task.FromResult(techs);
    }

    private sealed class FakeAppointmentRepository(Appointment? seed) : IAppointmentRepository
    {
        public Appointment? Updated { get; private set; }
        public int UpdateCalls { get; private set; }
        public Guid? LastExcludeId { get; private set; }
        public BusyResources Busy { get; init; } = new(new HashSet<Guid>(), new HashSet<Guid>());
        public BookingResource? ConflictOnFirstUpdate { get; init; }

        public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(seed is not null && seed.Id == id ? seed : null);

        public Task UpdateAsync(Appointment appointment, CancellationToken ct = default)
        {
            UpdateCalls++;
            if (ConflictOnFirstUpdate is { } resource && UpdateCalls == 1)
            {
                throw new AppointmentSlotConflictException(resource);
            }

            Updated = appointment;
            return Task.CompletedTask;
        }

        public Task AddAsync(Appointment appointment, CancellationToken ct = default) => Task.CompletedTask;

        public Task<BusyResources> GetBusyResourcesAsync(
            IReadOnlyCollection<Guid> candidateBayIds,
            IReadOnlyCollection<Guid> candidateTechnicianIds,
            DateTimeOffset start,
            DateTimeOffset end,
            Guid? excludeAppointmentId = null,
            CancellationToken ct = default)
        {
            LastExcludeId = excludeAppointmentId;
            return Task.FromResult(Busy);
        }
    }
}
