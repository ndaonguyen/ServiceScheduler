namespace AppointmentScheduler.Application.Features.Booking;

/// <summary>Which resource lost a concurrent race for the requested window.</summary>
public enum BookingResource
{
    ServiceBay,
    Technician,
}

/// <summary>
/// Thrown by <see cref="Abstractions.IAppointmentRepository.AddAsync"/> when the database rejects the
/// insert because the chosen bay/technician was booked concurrently (the #6 <c>EXCLUDE</c> constraint
/// raised a Postgres exclusion violation). The handler catches it and retries once with the next free
/// candidate. Domain-neutral by design: it lets the Infrastructure layer surface a concurrency
/// conflict without leaking EF/Npgsql exception types into the Application layer (ADR-0001 / AC-04).
/// </summary>
public sealed class AppointmentSlotConflictException(BookingResource resource) : Exception
{
    public BookingResource Resource { get; } = resource;
}
