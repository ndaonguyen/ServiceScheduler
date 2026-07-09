using System.Security.Claims;
using AppointmentScheduler.Application.Abstractions;

namespace AppointmentScheduler.Api.Security;

/// <summary>
/// Reads the authenticated principal from the current request. Registered scoped so handlers
/// can depend on <see cref="ICurrentUser"/> without touching HttpContext directly.
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    // JsonWebTokenHandler (the .NET default) leaves "sub" as-is rather than mapping it to
    // NameIdentifier — check both so this works regardless of the IdP's claim shape.
    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
