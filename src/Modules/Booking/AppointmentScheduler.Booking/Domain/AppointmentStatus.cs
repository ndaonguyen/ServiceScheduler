namespace AppointmentScheduler.Booking.Domain;

/// <summary>
/// Lifecycle state of an <see cref="Appointment"/>. A booking is born <see cref="Confirmed"/> and
/// may transition to <see cref="Cancelled"/> (a soft state, never a row delete). Rescheduling keeps
/// the appointment <see cref="Confirmed"/> and re-stamps its window/resources in place.
/// </summary>
public enum AppointmentStatus
{
    Confirmed,
    Cancelled,
}
