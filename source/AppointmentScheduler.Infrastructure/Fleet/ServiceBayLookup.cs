using Microsoft.EntityFrameworkCore;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Infrastructure.Persistence;

namespace AppointmentScheduler.Infrastructure.Fleet;

/// <summary>
/// Fleet's implementation of <see cref="IServiceBayLookup"/>. Resolves the dealership (for its name)
/// and its bays; returns <c>null</c> when the dealership does not exist.
/// </summary>
internal sealed class ServiceBayLookup(AppDbContext db) : IServiceBayLookup
{
    public async Task<DealershipBays?> ListByDealershipAsync(Guid dealershipId, CancellationToken ct = default)
    {
        var name = await db.Dealerships
            .Where(d => d.Id == dealershipId)
            .Select(d => d.Name)
            .SingleOrDefaultAsync(ct);

        if (name is null)
        {
            return null;
        }

        var bays = await db.ServiceBays
            .Where(b => b.DealershipId == dealershipId)
            .Select(b => new BayInfo(b.Id, b.Label))
            .ToListAsync(ct);

        return new DealershipBays(name, bays);
    }
}
