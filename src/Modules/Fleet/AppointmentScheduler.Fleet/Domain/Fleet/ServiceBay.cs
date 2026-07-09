namespace AppointmentScheduler.Domain.Fleet;

/// <summary>A physical service bay belonging to a <see cref="Dealership"/>.</summary>
public sealed class ServiceBay
{
    public Guid Id { get; set; }
    public Guid DealershipId { get; set; }
    public string Label { get; set; } = default!;
}
