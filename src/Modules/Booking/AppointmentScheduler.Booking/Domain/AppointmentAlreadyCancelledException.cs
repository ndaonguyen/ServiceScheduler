namespace AppointmentScheduler.Booking.Domain;

/// <summary>
/// Thrown by a lifecycle transition (<see cref="Appointment.Cancel"/> /
/// <see cref="Appointment.RescheduleTo"/>) when the appointment is no longer
/// <see cref="AppointmentStatus.Confirmed"/>. The Application layer maps it to the PRD §8
/// <c>APPOINTMENT_ALREADY_CANCELLED</c> error; keeping it domain-neutral avoids leaking
/// framework types across the boundary (ADR-0001 / AC-04).
/// </summary>
public sealed class AppointmentAlreadyCancelledException(Guid appointmentId) : Exception
{
    public Guid AppointmentId { get; } = appointmentId;
}
