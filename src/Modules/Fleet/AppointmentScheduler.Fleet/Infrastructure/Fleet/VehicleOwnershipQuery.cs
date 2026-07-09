using AppointmentScheduler.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using AppointmentScheduler.Fleet.Contracts;
using AppointmentScheduler.BuildingBlocks.Persistence;

namespace AppointmentScheduler.Fleet.Infrastructure;

/// <summary>Fleet's implementation of <see cref="IVehicleOwnershipQuery"/> over <see cref="AppDbContext"/>.</summary>
internal sealed class VehicleOwnershipQuery(AppDbContext db) : IVehicleOwnershipQuery
{
    public async Task<VehicleOwnership> CheckAsync(Guid vehicleId, string ownerId, CancellationToken ct = default)
    {
        var owner = await db.Set<Vehicle>()
            .Where(v => v.Id == vehicleId)
            .Select(v => v.OwnerId)
            .SingleOrDefaultAsync(ct);

        if (owner is null)
        {
            return VehicleOwnership.NotFound;
        }

        return owner == ownerId ? VehicleOwnership.Owned : VehicleOwnership.NotOwned;
    }
}
