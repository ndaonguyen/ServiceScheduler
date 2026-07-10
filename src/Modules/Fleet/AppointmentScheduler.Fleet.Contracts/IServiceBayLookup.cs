namespace AppointmentScheduler.Fleet.Contracts;

/// <summary>
/// Cross-module read port owned by <b>Fleet</b>: resolves a dealership and its service bays. Returns
/// <c>null</c> when the dealership is unknown (#4 turns that into <c>404 DEALERSHIP_NOT_FOUND</c>).
/// The dealership's display name rides along here because resolving the bays already loads the
/// dealership — this keeps the endpoint's response fully populated without a separate port.
/// </summary>
public interface IServiceBayLookup
{
    Task<DealershipBays?> ListByDealershipAsync(Guid dealershipId, CancellationToken ct = default);
}

public sealed record DealershipBays(string DealershipName, IReadOnlyList<BayInfo> Bays);

public sealed record BayInfo(Guid Id, string Label);
