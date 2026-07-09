namespace AppointmentScheduler.Domain.Booking;

/// <summary>
/// Lifecycle state of an <see cref="Appointment"/>. Only <see cref="Confirmed"/> exists in this
/// slice; cancellation / rescheduling are future work (see the PRD "Out of Scope").
/// </summary>
public enum AppointmentStatus
{
    Confirmed,
}
