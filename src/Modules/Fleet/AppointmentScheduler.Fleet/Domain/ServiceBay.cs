using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Fleet.Domain;

/// <summary>A physical service bay belonging to a <see cref="Dealership"/>.</summary>
public sealed class ServiceBay : Entity<Guid>, IAggregateRoot
{
    private ServiceBay() { }

    public Guid DealershipId { get; private set; }
    public string Label { get; private set; } = default!;

    public static ServiceBay Create(Guid id, Guid dealershipId, string label) =>
        new()
        {
            Id = Guard.NotEmpty(id, nameof(id)),
            DealershipId = Guard.NotEmpty(dealershipId, nameof(dealershipId), "A service bay must belong to a dealership."),
            Label = Guard.NotNullOrWhiteSpace(label, nameof(label), "A service bay must have a label."),
        };
}
