namespace AppointmentScheduler.Fleet.Domain;

/// <summary>A dealership location that owns service bays and employs technicians.</summary>
public sealed class Dealership
{
    private Dealership() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Address { get; private set; } = default!;

    public static Dealership Create(Guid id, string name, string address)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A dealership must have a name.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("A dealership must have an address.", nameof(address));
        }

        return new Dealership { Id = id, Name = name, Address = address };
    }
}
