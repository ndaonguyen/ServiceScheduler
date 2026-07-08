namespace AppointmentScheduler.Domain.Workforce;

/// <summary>
/// A skill link: the <see cref="Technician"/> is qualified for the given service type. The service
/// type is referenced as an opaque id (ADR-0001 AC-04) — Workforce holds no Catalog types.
/// Identified by the composite key (<see cref="TechnicianId"/>, <see cref="ServiceTypeId"/>).
/// </summary>
public sealed class TechnicianQualification
{
    public Guid TechnicianId { get; set; }
    public Guid ServiceTypeId { get; set; }
}
