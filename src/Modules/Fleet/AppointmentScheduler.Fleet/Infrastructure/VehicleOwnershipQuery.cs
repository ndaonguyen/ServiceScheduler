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
        var vehicle = await db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);

        if (vehicle is null)
        {
            return VehicleOwnership.NotFound;
        }

        // The ownership rule lives on the aggregate (Vehicle.IsOwnedBy), not in this adapter.
        return vehicle.IsOwnedBy(ownerId) ? VehicleOwnership.Owned : VehicleOwnership.NotOwned;
    }
}
