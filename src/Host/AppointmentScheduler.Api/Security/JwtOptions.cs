namespace AppointmentScheduler.Api.Security;

/// <summary>
/// JWT + refresh-token settings, bound from the <c>Jwt</c> configuration section. Every duration
/// is configurable here. The signing key must be supplied per environment (dev value in
/// appsettings; production via secret / env var <c>Jwt__SigningKey</c>) and be at least 32 bytes
/// for HS256.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "AppointmentScheduler";
    public string Audience { get; init; } = "AppointmentScheduler";
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Access-token lifetime in minutes.</summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; init; } = 7;
}
