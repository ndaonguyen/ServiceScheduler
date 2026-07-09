using AppointmentScheduler.Domain.Booking;

namespace AppointmentScheduler.Application.Abstractions;

/// <summary>
/// Persistence port owned by the <b>Booking</b> module. Adds a new appointment and commits it, and
/// answers the module's own availability question. The aggregate is Booking's own domain type, so
/// referencing it here does not cross a module boundary (AC-02).
/// </summary>
public interface IAppointmentRepository
{
    /// <summary>
    /// Persists the appointment. Throws
    /// <see cref="Features.Booking.AppointmentSlotConflictException"/> when the insert is rejected by
    /// the no-overlap constraint (a concurrent booking took the same bay/technician for the window).
    /// </summary>
    Task AddAsync(Appointment appointment, CancellationToken ct = default);

    /// <summary>
    /// Of the given candidate bays and technicians, which have a <b>confirmed</b> appointment
    /// overlapping the half-open window <c>[start, end)</c> (BR-01/BR-02/BR-03). Only the supplied
    /// candidate ids are considered, so resources at other dealerships are never inspected
    /// (BR-05/BR-06).
    /// </summary>
    Task<BusyResources> GetBusyResourcesAsync(
        IReadOnlyCollection<Guid> candidateBayIds,
        IReadOnlyCollection<Guid> candidateTechnicianIds,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default);
}

/// <summary>The subset of candidate resources that are busy for the requested window.</summary>
public sealed record BusyResources(IReadOnlySet<Guid> BusyBayIds, IReadOnlySet<Guid> BusyTechnicianIds);
