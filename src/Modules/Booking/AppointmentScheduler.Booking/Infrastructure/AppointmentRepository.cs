using AppointmentScheduler.Booking.Application.Abstractions;
using AppointmentScheduler.Booking.Application.Features;
using AppointmentScheduler.Booking.Domain;
using AppointmentScheduler.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AppointmentScheduler.Booking.Infrastructure;

/// <summary>Booking's implementation of <see cref="IAppointmentRepository"/> over <see cref="AppDbContext"/>.</summary>
internal sealed class AppointmentRepository(AppDbContext db) : IAppointmentRepository
{
    // Exclusion-constraint names from the #6 migration (20260708180933_BookingNoOverlapConstraints).
    // Kept in sync with that raw SQL by hand — see plan #7 Risk R2.
    private const string BayOverlapConstraint = "ex_appointments_bay_no_overlap";
    private const string TechnicianOverlapConstraint = "ex_appointments_technician_no_overlap";

    public async Task AddAsync(Appointment appointment, CancellationToken ct = default)
    {
        db.Set<Appointment>().Add(appointment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation
        } pg)
        {
            // Detach the failed insert so a retry's SaveChanges does not re-attempt it (plan #7 D4/R6).
            db.Entry(appointment).State = EntityState.Detached;
            throw new AppointmentSlotConflictException(ResourceOf(pg.ConstraintName));
        }
    }

    public async Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Set<Appointment>().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task UpdateAsync(Appointment appointment, CancellationToken ct = default)
    {
        // The appointment is already tracked (loaded via GetByIdAsync); SaveChanges flushes the
        // status flip (cancel) or the re-stamped window/resources (reschedule). A reschedule can lose
        // the same concurrency race a booking can, so we surface it identically for the retry loop.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation
        } pg)
        {
            throw new AppointmentSlotConflictException(ResourceOf(pg.ConstraintName));
        }
    }

    private static BookingResource ResourceOf(string? constraintName) =>
        constraintName == TechnicianOverlapConstraint
            ? BookingResource.Technician
            : BookingResource.ServiceBay; // bay constraint (and any unexpected name) -> ServiceBay

    public async Task<BusyResources> GetBusyResourcesAsync(
        IReadOnlyCollection<Guid> candidateBayIds,
        IReadOnlyCollection<Guid> candidateTechnicianIds,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAppointmentId = null,
        CancellationToken ct = default)
    {
        // One pass over confirmed appointments overlapping [start, end) that touch any candidate
        // resource. status = 'Confirmed' (text column) and the shared half-open predicate (BR-03)
        // translate to SQL; the candidate-id filters render as `= ANY(@ids)` (Npgsql). A reschedule
        // passes its own id to exclude, so its current slot is not counted against the new window.
        var rows = await db.Set<Appointment>()
            .Where(a => a.Status == AppointmentStatus.Confirmed)
            .Where(a => excludeAppointmentId == null || a.Id != excludeAppointmentId)
            .Where(AppointmentOverlap.Within(start, end))
            .Where(a => candidateBayIds.Contains(a.ServiceBayId) || candidateTechnicianIds.Contains(a.TechnicianId))
            .Select(a => new { a.ServiceBayId, a.TechnicianId })
            .ToListAsync(ct);

        var busyBayIds = rows.Select(r => r.ServiceBayId).Where(candidateBayIds.Contains).ToHashSet();
        var busyTechnicianIds = rows.Select(r => r.TechnicianId).Where(candidateTechnicianIds.Contains).ToHashSet();
        return new BusyResources(busyBayIds, busyTechnicianIds);
    }
}
