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

    public static Technician Create(Guid id, Guid dealershipId, string name) =>
        new()
        {
            Id = Guard.NotEmpty(id, nameof(id)),
            DealershipId = Guard.NotEmpty(dealershipId, nameof(dealershipId), "A technician must belong to a dealership."),
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), "A technician must have a name."),
        };
}
