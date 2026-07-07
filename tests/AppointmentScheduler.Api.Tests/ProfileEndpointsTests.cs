using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace AppointmentScheduler.Api.Tests;

public class ProfileEndpointsTests
{
    [Fact]
    public async Task Me_returns_the_callers_email_and_default_role()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "profile@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        await client.PostAsJsonAsync("/api/auth/login", creds);

        var response = await client.GetAsync("/api/profile/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        me!.UserName.Should().Be("profile@example.com");
        me.Roles.Should().Contain("user");
    }

    private sealed record MeResponse(string? UserName, string[] Roles);
}
