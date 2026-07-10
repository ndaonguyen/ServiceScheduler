namespace AppointmentScheduler.BuildingBlocks.SharedKernel;

/// <summary>
/// Small argument guards shared by the domain factories. Each returns the validated value so a
/// factory can guard and assign in one expression (<c>Name = Guard.NotNullOrWhiteSpace(name, nameof(name))</c>),
/// replacing the repeated <c>if (…) throw new ArgumentException(…)</c> boilerplate. Pass
/// <paramref name="message"/> when a domain-specific wording reads better than the default.
/// </summary>
public static class Guard
{
    /// <summary>Requires a non-empty <see cref="Guid"/> (a present reference / identity).</summary>
    public static Guid NotEmpty(Guid value, string paramName, string? message = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message ?? $"{paramName} is required.", paramName);
        }

        return value;
    }

    /// <summary>Requires a non-null, non-whitespace string. Returns it as non-nullable.</summary>
    public static string NotNullOrWhiteSpace(string? value, string paramName, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message ?? $"{paramName} is required.", paramName);
        }

        return value;
    }
}
