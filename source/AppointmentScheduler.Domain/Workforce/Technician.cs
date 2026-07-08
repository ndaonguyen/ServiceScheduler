namespace AppointmentScheduler.Domain.Workforce;

/// <summary>A technician employed at a dealership. Skills are recorded via
/// <see cref="TechnicianQualification"/>.</summary>
public sealed class Technician
{
    public Guid Id { get; set; }
    public Guid DealershipId { get; set; }
    public string Name { get; set; } = default!;
}
