using AppointmentScheduler.Domain.Booking;

namespace AppointmentScheduler.Application.Abstractions;

/// <summary>
/// Persistence port owned by the <b>Booking</b> module. Adds a new appointment and commits it. The
/// aggregate is Booking's own domain type, so referencing it here does not cross a module boundary.
/// </summary>
public interface IAppointmentRepository
{
    Task AddAsync(Appointment appointment, CancellationToken ct = default);
}
