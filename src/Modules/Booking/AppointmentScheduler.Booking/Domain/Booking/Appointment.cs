namespace AppointmentScheduler.Domain.Booking;

/// <summary>
/// The Booking module's aggregate: a confirmed reservation of a service bay and a qualified
/// technician for a vehicle at a dealership over a half-open time window
/// <c>[ScheduledStart, ScheduledEnd)</c>. All cross-module references are opaque ids (ADR-0001
/// AC-04) — there are no navigation properties into Fleet / Workforce / Catalog. All times are UTC.
/// </summary>
public sealed class Appointment
{
    public Guid Id { get; set; }

    /// <summary>Owning customer's <c>AppUser.Id</c> (opaque string).</summary>
    public string OwnerId { get; set; } = default!;

    public Guid VehicleId { get; set; }
    public Guid DealershipId { get; set; }
    public Guid ServiceTypeId { get; set; }
    public Guid ServiceBayId { get; set; }
    public Guid TechnicianId { get; set; }

    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset ScheduledEnd { get; set; }

    public AppointmentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
