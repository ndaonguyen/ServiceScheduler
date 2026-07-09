using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Workforce.Domain;

/// <summary>
/// A technician employed at a dealership (Workforce aggregate root). Skills are recorded via
/// <see cref="TechnicianQualification"/>. Created through <see cref="Create"/>.
/// </summary>
public sealed class Technician : Entity<Guid>, IAggregateRoot
{
    private Technician() { }

    public Guid DealershipId { get; private set; }
    public string Name { get; private set; } = default!;

    public static Technician Create(Guid id, Guid dealershipId, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }

        if (dealershipId == Guid.Empty)
        {
            throw new ArgumentException("A technician must belong to a dealership.", nameof(dealershipId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A technician must have a name.", nameof(name));
        }

        return new Technician { Id = id, DealershipId = dealershipId, Name = name };
    }
}
