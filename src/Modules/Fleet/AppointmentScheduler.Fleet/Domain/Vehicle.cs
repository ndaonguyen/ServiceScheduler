using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Fleet.Domain;

/// <summary>
/// A customer-owned vehicle (Fleet aggregate root). Ownership is modelled here as
/// <see cref="OwnerId"/> → <c>AppUser.Id</c>; there is no separate Customer aggregate (PRD AS-02).
/// Created through <see cref="Create"/>, which enforces the invariants; setters are private.
/// </summary>
public sealed class Vehicle : Entity<Guid>, IAggregateRoot
{
    private Vehicle() { }

    /// <summary>Owning customer's <c>AppUser.Id</c> (opaque string).</summary>
    public string OwnerId { get; private set; } = default!;

    public string Make { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public int Year { get; private set; }
    public Vin Vin { get; private set; } = default!;

    public static Vehicle Create(Guid id, string ownerId, string make, string model, int year, string vin)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("A vehicle must have an owner.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(make))
        {
            throw new ArgumentException("Make is required.", nameof(make));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required.", nameof(model));
        }

        if (year is < 1900 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Model year is out of range.");
        }

        return new Vehicle
        {
            Id = id,
            OwnerId = ownerId,
            Make = make,
            Model = model,
            Year = year,
            Vin = Vin.Create(vin),
        };
    }

    /// <summary>Domain rule: is this vehicle owned by the given caller?</summary>
    public bool IsOwnedBy(string ownerId) => OwnerId == ownerId;
}
