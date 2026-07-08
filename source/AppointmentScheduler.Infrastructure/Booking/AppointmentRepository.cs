using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Features.Booking;
using AppointmentScheduler.Domain.Booking;
using AppointmentScheduler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AppointmentScheduler.Infrastructure.Booking;

/// <summary>Booking's implementation of <see cref="IAppointmentRepository"/> over <see cref="AppDbContext"/>.</summary>
internal sealed class AppointmentRepository(AppDbContext db) : IAppointmentRepository
{
    public async Task AddAsync(Appointment appointment, CancellationToken ct = default)
    {
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BusyResources> GetBusyResourcesAsync(
        IReadOnlyCollection<Guid> candidateBayIds,
        IReadOnlyCollection<Guid> candidateTechnicianIds,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default)
    {
        // One pass over confirmed appointments overlapping [start, end) that touch any candidate
        // resource. status = 'Confirmed' (text column) and the shared half-open predicate (BR-03)
        // translate to SQL; the candidate-id filters render as `= ANY(@ids)` (Npgsql).
        var rows = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Confirmed)
            .Where(AppointmentOverlap.Within(start, end))
            .Where(a => candidateBayIds.Contains(a.ServiceBayId) || candidateTechnicianIds.Contains(a.TechnicianId))
            .Select(a => new { a.ServiceBayId, a.TechnicianId })
            .ToListAsync(ct);

        var busyBayIds = rows.Select(r => r.ServiceBayId).Where(candidateBayIds.Contains).ToHashSet();
        var busyTechnicianIds = rows.Select(r => r.TechnicianId).Where(candidateTechnicianIds.Contains).ToHashSet();
        return new BusyResources(busyBayIds, busyTechnicianIds);
    }
}
