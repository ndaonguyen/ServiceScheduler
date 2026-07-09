namespace AppointmentScheduler.Application.Abstractions;

/// <summary>
/// Cross-module read port owned by <b>Fleet</b>: checks whether a vehicle exists and is owned by the
/// given caller. This slice proceeds on <see cref="VehicleOwnership.Owned"/>; #4 maps
/// <see cref="VehicleOwnership.NotOwned"/> → <c>403</c> and <see cref="VehicleOwnership.NotFound"/> →
/// <c>404</c>.
/// </summary>
public interface IVehicleOwnershipQuery
{
    Task<VehicleOwnership> CheckAsync(Guid vehicleId, string ownerId, CancellationToken ct = default);
}

public enum VehicleOwnership
{
    Owned,
    NotOwned,
    NotFound,
}
