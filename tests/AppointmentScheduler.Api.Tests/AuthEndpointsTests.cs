using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointmentScheduler.Api.Tests;

/// <summary>
/// Exercises the real JWT pipeline end-to-end (no test-auth shortcut): register → login → use the
/// httpOnly access cookie on a protected endpoint, rotate via the refresh cookie, and log out. The
/// test client's cookie container plays the browser's role and attaches cookies automatically.
/// </summary>
public class AuthEndpointsTests
{
    [Fact]
    public async Task Register_then_login_sets_cookies_that_authenticate()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "jwt@example.com", password = "Passw0rd!$" };

        var register = await client.PostAsJsonAsync("/api/auth/register", creds);
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await client.PostAsJsonAsync("/api/auth/login", creds);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var setCookies = login.Headers.GetValues("Set-Cookie").ToList();
        setCookies.Should().Contain(c => c.StartsWith("access_token="));
        setCookies.Should().Contain(c => c.StartsWith("refresh_token="));

        // The access cookie is attached automatically by the client's cookie container.
        var me = await client.GetAsync("/api/profile/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        await client.PostAsJsonAsync("/api/auth/register", new { email = "jwt2@example.com", password = "Passw0rd!$" });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "jwt2@example.com", password = "wrong" });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repeated_bad_passwords_lock_the_account()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var email = "lockout@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!$" });

        // Exhaust the 5-attempt lockout threshold with wrong passwords.
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
        }

        // The account is now locked — even the correct password is refused.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!$" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_without_cookie_is_unauthorized()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/widgets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_the_old_one_is_rejected_as_reuse()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "refresh@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        var login = await client.PostAsJsonAsync("/api/auth/login", creds);
        var originalRefresh = ReadSetCookie(login, "refresh_token");

        // The cookie container sends the refresh cookie automatically; rotation issues a new pair.
        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadSetCookie(refresh, "refresh_token").Should().NotBe(originalRefresh);

        // The rotated access cookie still authenticates.
        (await client.GetAsync("/api/profile/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Replay the original (already-rotated) refresh token by hand — reuse detection rejects it.
        using var raw = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.TryAddWithoutValidation("Cookie", $"refresh_token={originalRefresh}");
        (await raw.SendAsync(replay)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "logout@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        await client.PostAsJsonAsync("/api/auth/login", creds);

        // Logout is authorized via the access cookie and revokes the refresh token.
        var logout = await client.PostAsync("/api/auth/logout", content: null);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_lets_the_new_password_log_in_and_rejects_the_old()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var email = "changepw@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!$" });
        await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!$" });

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "Passw0rd!$", newPassword = "NewPassw0rd!$" });
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        // A fresh client (no cookies) can log in with the new password but not the old one.
        using var fresh = factory.CreateClient();
        (await fresh.PostAsJsonAsync("/api/auth/login", new { email, password = "NewPassw0rd!$" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await fresh.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!$" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_password_is_rejected()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var email = "changepw-wrong@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!$" });
        await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!$" });

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "wrong", newPassword = "NewPassw0rd!$" });

        change.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_password_revokes_other_devices_but_keeps_the_current_one()
    {
        using var factory = new JwtTestFactory();
        await factory.SeedRolesAsync();

        var email = "changepw-devices@example.com";
        // Two independent cookie containers = two logged-in devices for the same account.
        using var deviceA = factory.CreateClient();
        using var deviceB = factory.CreateClient();
        await deviceA.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!$" });
        await deviceA.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!$" });
        await deviceB.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!$" });

        // Device A changes the password.
        (await deviceA.PostAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "Passw0rd!$", newPassword = "NewPassw0rd!$" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Device A stays logged in (it received a fresh cookie pair on the change-password response).
        (await deviceA.PostAsync("/api/auth/refresh", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Device B's refresh token was revoked — it can no longer refresh.
        (await deviceB.PostAsync("/api/auth/refresh", content: null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_sets_httponly_samesite_strict_cookies_with_correct_paths()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "cookieattrs@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);

        var login = await client.PostAsJsonAsync("/api/auth/login", creds);
        var setCookies = login.Headers.GetValues("Set-Cookie").ToList();

        var access = setCookies.Single(c => c.StartsWith("access_token=")).ToLowerInvariant();
        access.Should().Contain("httponly");
        access.Should().Contain("samesite=strict");
        access.Should().Contain("path=/;");

        var refresh = setCookies.Single(c => c.StartsWith("refresh_token=")).ToLowerInvariant();
        refresh.Should().Contain("httponly");
        refresh.Should().Contain("samesite=strict");
        refresh.Should().Contain("path=/api/auth/refresh");
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "expired@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        await client.PostAsJsonAsync("/api/auth/login", creds);

        // Jump past the 7-day refresh TTL without ever presenting the token.
        factory.Clock.Advance(TimeSpan.FromDays(8));

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_no_cookie_present_is_unauthorized()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();

        // No register/login happened, so the client's cookie container has nothing to send.
        var refresh = await client.PostAsync("/api/auth/refresh", content: null);

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rotated_refresh_token_keeps_the_original_login_ttl()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "fixedttl@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        await client.PostAsJsonAsync("/api/auth/login", creds);

        // Rotate partway through the 7-day window from login.
        factory.Clock.Advance(TimeSpan.FromDays(6));
        var rotated = await client.PostAsync("/api/auth/refresh", content: null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);

        // Only 2 days after rotation, but 8 days after the original login: the rotated token
        // must still expire on schedule rather than getting a fresh 7-day clock of its own.
        factory.Clock.Advance(TimeSpan.FromDays(2));
        var secondRefresh = await client.PostAsync("/api/auth/refresh", content: null);

        secondRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_without_auth_is_unauthorized()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "Passw0rd!$", newPassword = "NewPassw0rd!$" });

        change.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Pull a cookie's value out of the response's Set-Cookie headers (value up to the first ';').
    private static string ReadSetCookie(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{name}="));
        var value = header[(name.Length + 1)..];
        var end = value.IndexOf(';');
        return end >= 0 ? value[..end] : value;
    }
}
