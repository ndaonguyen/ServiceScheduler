namespace AppointmentScheduler.Domain.Fleet;

/// <summary>A dealership location that owns service bays and employs technicians.</summary>
public sealed class Dealership
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Address { get; set; } = default!;
}
