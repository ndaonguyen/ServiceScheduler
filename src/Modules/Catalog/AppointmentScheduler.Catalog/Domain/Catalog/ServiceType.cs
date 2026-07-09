namespace AppointmentScheduler.Catalog.Domain;

/// <summary>
/// A bookable service with a fixed <see cref="Duration"/>. The duration alone determines an
/// appointment's length — never client input (PRD BR-07).
/// </summary>
public sealed class ServiceType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public TimeSpan Duration { get; set; }
}
