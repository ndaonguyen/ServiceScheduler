using AppointmentScheduler.Booking.Domain;

namespace AppointmentScheduler.Booking.Application.Abstractions;

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

    /// <summary>Loads a single appointment by id, or <c>null</c> if none exists.</summary>
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Persists changes to an already-tracked appointment (a status flip for cancel, or a re-stamped
    /// window/resources for reschedule). Throws
    /// <see cref="Features.Booking.AppointmentSlotConflictException"/> when a reschedule is rejected by
    /// the no-overlap constraint — the same concurrency signal as <see cref="AddAsync"/>, so callers
    /// reuse the retry-with-next-candidate loop.
    /// </summary>
    Task UpdateAsync(Appointment appointment, CancellationToken ct = default);

    /// <summary>
    /// Of the given candidate bays and technicians, which have a <b>confirmed</b> appointment
    /// overlapping the half-open window <c>[start, end)</c> (BR-01/BR-02/BR-03). Only the supplied
    /// candidate ids are considered, so resources at other dealerships are never inspected
    /// (BR-05/BR-06). When <paramref name="excludeAppointmentId"/> is set, that appointment is ignored
    /// — a reschedule must not treat its own current slot as a conflict against the new window.
    /// </summary>
    Task<BusyResources> GetBusyResourcesAsync(
        IReadOnlyCollection<Guid> candidateBayIds,
        IReadOnlyCollection<Guid> candidateTechnicianIds,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAppointmentId = null,
        CancellationToken ct = default);
}

/// <summary>The subset of candidate resources that are busy for the requested window.</summary>
public sealed record BusyResources(IReadOnlySet<Guid> BusyBayIds, IReadOnlySet<Guid> BusyTechnicianIds);
