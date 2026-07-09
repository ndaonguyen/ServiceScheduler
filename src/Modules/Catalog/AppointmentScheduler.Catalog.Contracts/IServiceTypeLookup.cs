namespace AppointmentScheduler.Catalog.Contracts;

/// <summary>
/// Cross-module read port owned by <b>Catalog</b>: resolves a service type's display name and fixed
/// duration. Returns <c>null</c> when the id is unknown (#4 turns that into
/// <c>404 SERVICE_TYPE_NOT_FOUND</c>).
/// </summary>
public interface IServiceTypeLookup
{
    Task<ServiceTypeInfo?> GetAsync(Guid serviceTypeId, CancellationToken ct = default);
}

public sealed record ServiceTypeInfo(Guid Id, string Name, TimeSpan Duration);
