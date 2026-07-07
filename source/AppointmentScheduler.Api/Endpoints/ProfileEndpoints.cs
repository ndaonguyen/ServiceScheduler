using System.Security.Claims;

namespace AppointmentScheduler.Api.Endpoints;

/// <summary>
/// Identity endpoint under <c>/api/profile</c>. Profile data is kept out of the access token (which
/// stays small and never carries stale name/avatar data) and fetched here instead — the browser
/// auto-attaches the access cookie. See authentication.md §7.
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").WithTags("Profile");

        // Current caller's name + roles, read from the validated JWT principal.
        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Email),
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
        }))
        .WithName("Me")
        .RequireAuthorization();

        return app;
    }
}
