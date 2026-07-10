using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Workforce.Domain;

/// <summary>
/// A skill link: the <see cref="Technician"/> is qualified for the given service type. The service
/// type is referenced as an opaque id (ADR-0001 AC-04) — Workforce holds no Catalog types.
/// Identified by the composite key (<see cref="TechnicianId"/>, <see cref="ServiceTypeId"/>).
/// </summary>
public sealed class TechnicianQualification
{
    private TechnicianQualification() { }

    public Guid TechnicianId { get; private set; }
    public Guid ServiceTypeId { get; private set; }

    public static TechnicianQualification Create(Guid technicianId, Guid serviceTypeId) =>
        new()
        {
            TechnicianId = Guard.NotEmpty(technicianId, nameof(technicianId), "A qualification must reference a technician."),
            ServiceTypeId = Guard.NotEmpty(serviceTypeId, nameof(serviceTypeId), "A qualification must reference a service type."),
        };

    /// <summary>Domain rule: is this qualification for the given service type?</summary>
    public bool IsFor(Guid serviceTypeId) => ServiceTypeId == serviceTypeId;
}
