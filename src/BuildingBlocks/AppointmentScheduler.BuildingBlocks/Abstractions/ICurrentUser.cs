namespace AppointmentScheduler.Application.Abstractions;

/// <summary>
/// The authenticated caller, surfaced to the Application layer without a dependency on
/// HttpContext. Implemented in the API (composition root) over <c>HttpContext.User</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Stable subject id from the token (<c>sub</c> / NameIdentifier), or null if anonymous.</summary>
    string? UserId { get; }

    /// <summary>True when the request carries a valid token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>True when the caller has the given role claim.</summary>
    bool IsInRole(string role);
}
