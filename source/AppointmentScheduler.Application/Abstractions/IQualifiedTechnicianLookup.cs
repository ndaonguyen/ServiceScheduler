namespace AppointmentScheduler.Application.Abstractions;

/// <summary>
/// Cross-module read port owned by <b>Workforce</b>: lists technicians at a dealership who hold the
/// qualification for a service type. An empty list means none qualified (#5 turns that into
/// <c>409 NO_QUALIFIED_TECHNICIAN</c>).
/// </summary>
public interface IQualifiedTechnicianLookup
{
    Task<IReadOnlyList<TechnicianInfo>> ListAsync(Guid dealershipId, Guid serviceTypeId, CancellationToken ct = default);
}

public sealed record TechnicianInfo(Guid Id, string Name);
