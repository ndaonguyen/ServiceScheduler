using AppointmentScheduler.BuildingBlocks.SharedKernel;

namespace AppointmentScheduler.Fleet.Domain;

/// <summary>
/// A Vehicle Identification Number — a value object that guarantees a well-formed VIN. An invalid
/// VIN cannot exist: construction goes through <see cref="Create"/>, so the format rules (the
/// "is this valid?" logic) live here in the domain rather than in a service or handler.
/// </summary>
public sealed record Vin : IValueObject
{
    public string Value { get; }

    private Vin(string value) => Value = value;

    public static Vin Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A VIN is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 17)
        {
            throw new ArgumentException($"A VIN must be 17 characters (got {normalized.Length}).", nameof(value));
        }

        // A VIN uses digits and letters except I, O, and Q (excluded to avoid confusion with 1 / 0).
        if (!normalized.All(c => char.IsAsciiLetterOrDigit(c) && c is not ('I' or 'O' or 'Q')))
        {
            throw new ArgumentException("A VIN may contain only letters (excluding I, O, Q) and digits.", nameof(value));
        }

        return new Vin(normalized);
    }

    public override string ToString() => Value;
}
