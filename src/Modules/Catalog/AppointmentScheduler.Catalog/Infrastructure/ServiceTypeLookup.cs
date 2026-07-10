using AppointmentScheduler.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using AppointmentScheduler.Catalog.Contracts;
using AppointmentScheduler.BuildingBlocks.Persistence;

namespace AppointmentScheduler.Catalog.Infrastructure;

/// <summary>Catalog's implementation of <see cref="IServiceTypeLookup"/> over <see cref="AppDbContext"/>.</summary>
internal sealed class ServiceTypeLookup(AppDbContext db) : IServiceTypeLookup
{
    public async Task<ServiceTypeInfo?> GetAsync(Guid serviceTypeId, CancellationToken ct = default) =>
        await db.Set<ServiceType>()
            .Where(s => s.Id == serviceTypeId)
            .Select(s => new ServiceTypeInfo(s.Id, s.Name, s.Duration))
            .SingleOrDefaultAsync(ct);
}
