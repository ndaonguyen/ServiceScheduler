using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Catalog.Domain;

/// <summary>
/// A bookable service with a fixed <see cref="Duration"/> (Catalog aggregate root). The duration
/// alone determines an appointment's length — never client input (PRD BR-07). Created through
/// <see cref="Create"/>, which guarantees a positive duration.
/// </summary>
public sealed class ServiceType : Entity<Guid>, IAggregateRoot
{
    private ServiceType() { }

    public string Name { get; private set; } = default!;
    public TimeSpan Duration { get; private set; }

    public static ServiceType Create(Guid id, string name, TimeSpan duration)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A service type must have a name.", nameof(name));
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("A service type must have a positive duration.", nameof(duration));
        }

        return new ServiceType { Id = id, Name = name, Duration = duration };
    }
}
