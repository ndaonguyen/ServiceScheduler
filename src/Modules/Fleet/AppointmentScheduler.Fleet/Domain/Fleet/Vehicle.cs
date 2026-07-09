namespace AppointmentScheduler.Domain.Fleet;

/// <summary>
/// A customer-owned vehicle. Ownership is modelled here (Fleet) as <see cref="OwnerId"/> →
/// <c>AppUser.Id</c>; there is no separate Customer aggregate (PRD AS-02).
/// </summary>
public sealed class Vehicle
{
    public Guid Id { get; set; }

    /// <summary>Owning customer's <c>AppUser.Id</c> (opaque string).</summary>
    public string OwnerId { get; set; } = default!;

    public string Make { get; set; } = default!;
    public string Model { get; set; } = default!;
    public int Year { get; set; }
    public string Vin { get; set; } = default!;
}
