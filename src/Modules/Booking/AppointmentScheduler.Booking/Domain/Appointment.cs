using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Booking.Domain;

/// <summary>
/// The Booking module's aggregate root: a confirmed reservation of a service bay and a qualified
/// technician for a vehicle at a dealership over a <see cref="TimeSlot"/>. All cross-module
/// references are opaque ids (ADR-0001 AC-04) — there are no navigation properties into Fleet /
/// Workforce / Catalog. All times are UTC.
///
/// Constructed only through <see cref="Schedule"/>, which enforces the aggregate's invariants;
/// setters are private so the appointment cannot be mutated into an invalid state from outside.
/// </summary>
public sealed class Appointment : Entity<Guid>, IAggregateRoot
{
    // EF materialization uses this; application code goes through Schedule(...).
    private Appointment() { }

    /// <summary>Owning customer's <c>AppUser.Id</c> (opaque string).</summary>
    public string OwnerId { get; private set; } = default!;

    public Guid VehicleId { get; private set; }
    public Guid DealershipId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public Guid ServiceBayId { get; private set; }
    public Guid TechnicianId { get; private set; }

    // Persisted as two scalar columns (queryable on every EF provider); the reserved window is
    // exposed to the domain as the TimeSlot value object below.
    public DateTimeOffset ScheduledStart { get; private set; }
    public DateTimeOffset ScheduledEnd { get; private set; }

    /// <summary>The reserved window <c>[Start, End)</c> as a value object.</summary>
    public TimeSlot Slot => new(ScheduledStart, ScheduledEnd);

    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Books a new confirmed appointment. Enforces the aggregate invariants: every reference id is
    /// present, an owner is set, and the window is <c>[start, start + duration)</c> (BR-07 — the
    /// duration comes from the service type, never the client). The no-double-booking invariant is
    /// cross-aggregate and concurrent, so it is enforced at the database (ADR-0005), not here.
    /// </summary>
    public static Appointment Schedule(
        string ownerId,
        Guid vehicleId,
        Guid dealershipId,
        Guid serviceTypeId,
        Guid serviceBayId,
        Guid technicianId,
        DateTimeOffset start,
        TimeSpan duration,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("An appointment must have an owner.", nameof(ownerId));
        }

        RequirePresent(vehicleId, nameof(vehicleId));
        RequirePresent(dealershipId, nameof(dealershipId));
        RequirePresent(serviceTypeId, nameof(serviceTypeId));
        RequirePresent(serviceBayId, nameof(serviceBayId));
        RequirePresent(technicianId, nameof(technicianId));

        // Building the value object validates the window (end > start) before we persist it.
        var slot = TimeSlot.FromDuration(start, duration);

        return new Appointment
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            VehicleId = vehicleId,
            DealershipId = dealershipId,
            ServiceTypeId = serviceTypeId,
            ServiceBayId = serviceBayId,
            TechnicianId = technicianId,
            ScheduledStart = slot.Start,
            ScheduledEnd = slot.End,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = createdAt,
        };
    }

    private static void RequirePresent(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException($"{name} is required.", name);
        }
    }
}
