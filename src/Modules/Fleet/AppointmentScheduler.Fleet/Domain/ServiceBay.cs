namespace AppointmentScheduler.Fleet.Domain;

/// <summary>A physical service bay belonging to a <see cref="Dealership"/>.</summary>
public sealed class ServiceBay
{
    private ServiceBay() { }

    public Guid Id { get; private set; }
    public Guid DealershipId { get; private set; }
    public string Label { get; private set; } = default!;

    public static ServiceBay Create(Guid id, Guid dealershipId, string label)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }

        if (dealershipId == Guid.Empty)
        {
            throw new ArgumentException("A service bay must belong to a dealership.", nameof(dealershipId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A service bay must have a label.", nameof(label));
        }

        return new ServiceBay { Id = id, DealershipId = dealershipId, Label = label };
    }
}
