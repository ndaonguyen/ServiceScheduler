using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using AppointmentScheduler.Api.Security;
using AppointmentScheduler.Infrastructure.Persistence;
using AppointmentScheduler.Infrastructure.Security;

namespace AppointmentScheduler.Api.Endpoints;

/// <summary>
/// JWT auth endpoints under <c>/api/auth</c>: register, exchange credentials for an access + refresh
/// token, rotate an expired access token via the refresh token, and log out. Both tokens are
/// delivered exclusively as <c>httpOnly</c> cookies — no token is ever returned in the response
/// body, so JavaScript cannot read them. Access tokens are stateless; the refresh token is the
/// revocable handle. Identity is read separately via <c>/api/profile/me</c> (see ProfileEndpoints).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // Create an account (assigned the "user" role). Open to anonymous callers.
        group.MapPost("/register", async (RegisterRequest body, UserManager<AppUser> users) =>
        {
            var user = new AppUser { UserName = body.Email, Email = body.Email };
            var result = await users.CreateAsync(user, body.Password);
            if (!result.Succeeded)
            {
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }

            await users.AddToRoleAsync(user, "user");
            return Results.Ok();
        })
        .WithName("Register")
        .AllowAnonymous();

        // Exchange email + password for an access + refresh token, both set as httpOnly cookies.
        group.MapPost("/login", async (
            LoginRequest body,
            HttpResponse response,
            UserManager<AppUser> users,
            ITokenService tokens,
            IRefreshTokenService refreshTokens) =>
        {
            var user = await users.FindByEmailAsync(body.Email);
            if (user is null)
            {
                return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            // Brute-force mitigation: refuse a locked account, count failures (locks at the
            // threshold), and reset the counter on success. Lockout policy is set in Program.cs.
            if (await users.IsLockedOutAsync(user))
            {
                return Results.Problem(
                    "Account temporarily locked due to too many failed attempts. Try again later.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!await users.CheckPasswordAsync(user, body.Password))
            {
                await users.AccessFailedAsync(user);
                return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            await users.ResetAccessFailedCountAsync(user);

            var roles = await users.GetRolesAsync(user);
            var access = tokens.CreateToken(user, roles);
            var refresh = await refreshTokens.IssueAsync(user.Id);
            AuthCookies.WriteAccess(response, access);
            AuthCookies.WriteRefresh(response, refresh);
            return Results.Ok();
        })
        .WithName("Login")
        .AllowAnonymous();

        // Rotate the refresh-token cookie for a fresh access + refresh cookie pair. The browser only
        // sends the refresh cookie to this path. On any failure clear both cookies and return 401.
        group.MapPost("/refresh", async (
            HttpRequest request,
            HttpResponse response,
            UserManager<AppUser> users,
            ITokenService tokens,
            IRefreshTokenService refreshTokens) =>
        {
            if (!request.Cookies.TryGetValue(AuthCookies.RefreshCookieName, out var rawToken)
                || await refreshTokens.RotateAsync(rawToken) is not { } rotation
                || await users.FindByIdAsync(rotation.UserId) is not { } user)
            {
                AuthCookies.ClearAccess(response);
                AuthCookies.ClearRefresh(response);
                return Results.Problem("Invalid or expired refresh token.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var roles = await users.GetRolesAsync(user);
            var access = tokens.CreateToken(user, roles);
            AuthCookies.WriteAccess(response, access);
            AuthCookies.WriteRefresh(response, rotation.NewToken);
            return Results.Ok();
        })
        .WithName("Refresh")
        .AllowAnonymous();

        // Change the caller's password, then invalidate every existing session and re-issue a fresh
        // cookie pair for *this* device. Because access tokens are stateless (no per-request DB check),
        // "log out everywhere on password change" is enforced by revoking all refresh tokens — not by
        // ASP.NET Identity's SecurityStamp. Other devices can no longer refresh; their access tokens
        // age out within the 15-minute TTL. The current caller stays logged in via the new pair.
        group.MapPost("/change-password", async (
            ChangePasswordRequest body,
            ClaimsPrincipal principal,
            HttpResponse response,
            UserManager<AppUser> users,
            ITokenService tokens,
            IRefreshTokenService refreshTokens) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
            {
                return Results.Problem("Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await users.ChangePasswordAsync(user, body.CurrentPassword, body.NewPassword);
            if (!result.Succeeded)
            {
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }

            // Kill every active refresh token (all devices), then mint a new pair for the caller so
            // they are not logged out of the device they just changed the password on.
            await refreshTokens.RevokeAllAsync(user.Id);
            var roles = await users.GetRolesAsync(user);
            var access = tokens.CreateToken(user, roles);
            var refresh = await refreshTokens.IssueAsync(user.Id);
            AuthCookies.WriteAccess(response, access);
            AuthCookies.WriteRefresh(response, refresh);
            return Results.Ok();
        })
        .WithName("ChangePassword")
        .RequireAuthorization();

        // Log out: revoke every active refresh token for the caller (identified by the access
        // cookie) and clear both cookies. The current access token stays valid until its short TTL.
        group.MapPost("/logout", async (
            ClaimsPrincipal principal,
            HttpResponse response,
            IRefreshTokenService refreshTokens) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            if (userId is not null)
            {
                await refreshTokens.RevokeAllAsync(userId);
            }

            AuthCookies.ClearAccess(response);
            AuthCookies.ClearRefresh(response);
            return Results.Ok();
        })
        .WithName("Logout")
        .RequireAuthorization();

        return app;
    }

    public sealed record RegisterRequest(string Email, string Password);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
