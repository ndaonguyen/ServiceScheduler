namespace AppointmentScheduler.Booking.Domain;

/// <summary>
/// Thrown by a lifecycle transition (<see cref="Appointment.Cancel"/> /
/// <see cref="Appointment.RescheduleTo"/>) when the appointment has already started (or the target
/// window is not strictly in the future): a past or in-progress appointment cannot be changed. The
/// Application layer maps it to the PRD §8 <c>APPOINTMENT_IN_PAST</c> error.
/// </summary>
public sealed class AppointmentInPastException(Guid appointmentId) : Exception
{
    public Guid AppointmentId { get; } = appointmentId;
}
