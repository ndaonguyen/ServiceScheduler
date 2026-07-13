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

    /// <summary>When the appointment was cancelled (UTC); <c>null</c> while it is confirmed.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

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
        Guard.NotNullOrWhiteSpace(ownerId, nameof(ownerId), "An appointment must have an owner.");

        // Building the value object validates the window (end > start) before we persist it.
        var slot = TimeSlot.FromDuration(start, duration);

        return new Appointment
        {
            // Appointment is runtime, transactional data with no externally-known id, so it assigns
            // its own. (Seed/reference aggregates — Vehicle, Dealership, etc. — instead take a
            // caller-supplied id so their identity is stable across re-seeds.)
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            VehicleId = Guard.NotEmpty(vehicleId, nameof(vehicleId)),
            DealershipId = Guard.NotEmpty(dealershipId, nameof(dealershipId)),
            ServiceTypeId = Guard.NotEmpty(serviceTypeId, nameof(serviceTypeId)),
            ServiceBayId = Guard.NotEmpty(serviceBayId, nameof(serviceBayId)),
            TechnicianId = Guard.NotEmpty(technicianId, nameof(technicianId)),
            ScheduledStart = slot.Start,
            ScheduledEnd = slot.End,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// Cancels the appointment — a soft state transition, never a delete. Enforces the lifecycle
    /// rules: only a confirmed, not-yet-started appointment may be cancelled. Once cancelled the row
    /// drops out of the partial <c>EXCLUDE</c> no-overlap constraint and the availability query
    /// (both scoped to <see cref="AppointmentStatus.Confirmed"/>), so the slot frees itself.
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new AppointmentAlreadyCancelledException(Id);
        if (ScheduledStart <= now)
            throw new AppointmentInPastException(Id);

        Status = AppointmentStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>
    /// Moves the appointment to a new window and (re)assigned bay/technician, keeping its identity and
    /// <see cref="AppointmentStatus.Confirmed"/> status. The caller (handler) resolves which resources
    /// are free for the new window; the aggregate enforces its invariants: the appointment must still
    /// be confirmed and the target window must be strictly in the future. The window is
    /// <c>[start, start + duration)</c> (BR-07 — duration comes from the service type). The database's
    /// partial <c>EXCLUDE</c> constraint re-validates the mutated row against every <b>other</b>
    /// confirmed appointment on commit (a row never conflicts with itself).
    /// </summary>
    public void RescheduleTo(
        Guid serviceBayId,
        Guid technicianId,
        DateTimeOffset start,
        TimeSpan duration,
        DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new AppointmentAlreadyCancelledException(Id);
        if (start <= now)
            throw new AppointmentInPastException(Id);

        // Building the value object validates the window (end > start) before we re-stamp it.
        var slot = TimeSlot.FromDuration(start, duration);

        ServiceBayId = Guard.NotEmpty(serviceBayId, nameof(serviceBayId));
        TechnicianId = Guard.NotEmpty(technicianId, nameof(technicianId));
        ScheduledStart = slot.Start;
        ScheduledEnd = slot.End;
    }
}
