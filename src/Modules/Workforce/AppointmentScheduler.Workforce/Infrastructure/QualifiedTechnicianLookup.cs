using AppointmentScheduler.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using AppointmentScheduler.Workforce.Contracts;
using AppointmentScheduler.BuildingBlocks.Persistence;

namespace AppointmentScheduler.Workforce.Infrastructure;

/// <summary>
/// Workforce's implementation of <see cref="IQualifiedTechnicianLookup"/>: technicians at the
/// dealership who hold a qualification for the service type.
/// </summary>
internal sealed class QualifiedTechnicianLookup(AppDbContext db) : IQualifiedTechnicianLookup
{
    public async Task<IReadOnlyList<TechnicianInfo>> ListAsync(Guid dealershipId, Guid serviceTypeId, CancellationToken ct = default) =>
        await db.Set<Technician>()
            .Where(t => t.DealershipId == dealershipId
                && db.Set<TechnicianQualification>().Any(q => q.TechnicianId == t.Id && q.ServiceTypeId == serviceTypeId))
            .Select(t => new TechnicianInfo(t.Id, t.Name))
            .ToListAsync(ct);
}
