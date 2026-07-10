using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Fleet.Domain;

/// <summary>A dealership location that owns service bays and employs technicians.</summary>
public sealed class Dealership : Entity<Guid>, IAggregateRoot
{
    private Dealership() { }

    public string Name { get; private set; } = default!;
    public string Address { get; private set; } = default!;

    public static Dealership Create(Guid id, string name, string address) =>
        new()
        {
            Id = Guard.NotEmpty(id, nameof(id)),
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), "A dealership must have a name."),
            Address = Guard.NotNullOrWhiteSpace(address, nameof(address), "A dealership must have an address."),
        };
}
