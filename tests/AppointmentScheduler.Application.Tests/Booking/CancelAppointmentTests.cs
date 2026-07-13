using AppointmentScheduler.Booking.Application.Abstractions;
using AppointmentScheduler.Booking.Application.Features;
using AppointmentScheduler.Booking.Domain;
using AppointmentScheduler.BuildingBlocks.Abstractions;
using AwesomeAssertions;
using FluentResults;
using Xunit;

namespace AppointmentScheduler.Application.Tests.Booking;

public class CancelAppointmentTests
{
    private static readonly Guid DealershipId = Guid.NewGuid();
    private static readonly Guid ServiceTypeId = Guid.NewGuid();
    private static readonly Guid BayId = Guid.NewGuid();
    private static readonly Guid TechId = Guid.NewGuid();
    private const string OwnerId = "user-123";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static readonly DateTimeOffset FutureStart = DateTimeOffset.Parse("2026-07-08T14:30:00Z");
    private static readonly DateTimeOffset PastStart = DateTimeOffset.Parse("2026-06-01T09:00:00Z");

    // Happy path: a confirmed, not-yet-started appointment owned by the caller becomes Cancelled and
    // is persisted. The slot frees itself because the row leaves the Confirmed-scoped constraint/query.
    [Fact]
    public async Task Cancel_confirmed_appointment_marks_it_cancelled_and_persists()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, FutureStart);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new CancelAppointment(appointment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.CancelledAt.Should().Be(Now);
        repo.Updated.Should().BeSameAs(appointment);
    }

    // Unknown appointment id -> 404; nothing persisted.
    [Fact]
    public async Task Unknown_appointment_returns_404_APPOINTMENT_NOT_FOUND()
    {
        var repo = new FakeAppointmentRepository(seed: null);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new CancelAppointment(Guid.NewGuid()), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_NOT_FOUND", 404);
        repo.Updated.Should().BeNull();
    }

    // Appointment owned by a different customer -> 403; nothing persisted.
    [Fact]
    public async Task Appointment_owned_by_another_caller_returns_403_APPOINTMENT_NOT_OWNED_BY_CALLER()
    {
        var appointment = ConfirmedOwnedBy("someone-else", FutureStart);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new CancelAppointment(appointment.Id), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_NOT_OWNED_BY_CALLER", 403);
        repo.Updated.Should().BeNull();
    }

    // Cancelling an already-cancelled appointment -> 409; nothing persisted again.
    [Fact]
    public async Task Already_cancelled_appointment_returns_409_APPOINTMENT_ALREADY_CANCELLED()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, FutureStart);
        appointment.Cancel(Now); // already cancelled
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new CancelAppointment(appointment.Id), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_ALREADY_CANCELLED", 409);
        repo.Updated.Should().BeNull();
    }

    // A past or in-progress appointment cannot be cancelled -> 409.
    [Fact]
    public async Task Past_appointment_returns_409_APPOINTMENT_IN_PAST()
    {
        var appointment = ConfirmedOwnedBy(OwnerId, PastStart);
        var repo = new FakeAppointmentRepository(appointment);
        var handler = BuildHandler(repo);

        var result = await handler.Handle(new CancelAppointment(appointment.Id), CancellationToken.None);

        ShouldFailWith(result, "APPOINTMENT_IN_PAST", 409);
        repo.Updated.Should().BeNull();
    }

    private static CancelAppointmentHandler BuildHandler(FakeAppointmentRepository repo) =>
        new(new FakeCurrentUser(OwnerId), repo, new FixedClock(Now));

    private static Appointment ConfirmedOwnedBy(string ownerId, DateTimeOffset start) =>
        Appointment.Schedule(ownerId, Guid.NewGuid(), DealershipId, ServiceTypeId, BayId, TechId,
            start, TimeSpan.FromMinutes(45), createdAt: start);

    private static void ShouldFailWith(Result result, string code, int httpStatus)
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

    private sealed class FakeAppointmentRepository(Appointment? seed) : IAppointmentRepository
    {
        public Appointment? Updated { get; private set; }

        public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(seed is not null && seed.Id == id ? seed : null);

        public Task UpdateAsync(Appointment appointment, CancellationToken ct = default)
        {
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
            CancellationToken ct = default) =>
            Task.FromResult(new BusyResources(new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
