using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace ServiceScheduler.Api.Tests;

public class WidgetEndpointsTests
{
    [Fact]
    public async Task Create_then_get_widget_round_trips()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClientAs("admin-user", "admin");

        var create = await client.PostAsJsonAsync("/api/widgets", new { name = "gizmo" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<WidgetResponse>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("gizmo");

        var get = await client.GetAsync($"/api/widgets/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await get.Content.ReadFromJsonAsync<WidgetResponse>();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Get_unknown_widget_returns_not_found()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClientAs("any-user");

        var response = await client.GetAsync($"/api/widgets/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Widgets_require_authentication()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient(); // no auth headers

        var response = await client.GetAsync("/api/widgets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_widget_without_admin_role_is_forbidden()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClientAs("plain-user", "user");

        var response = await client.PostAsJsonAsync("/api/widgets", new { name = "gizmo" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_widget_as_real_jwt_user_without_admin_role_is_forbidden()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "rbac-user@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        await client.PostAsJsonAsync("/api/auth/login", creds);

        var response = await client.PostAsJsonAsync("/api/widgets", new { name = "gizmo" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_widget_as_real_jwt_admin_succeeds()
    {
        using var factory = new JwtTestFactory();
        using var client = factory.CreateClient();
        await factory.SeedRolesAsync();

        var creds = new { email = "rbac-admin@example.com", password = "Passw0rd!$" };
        await client.PostAsJsonAsync("/api/auth/register", creds);
        await factory.GrantRoleAsync(creds.email, "admin");
        await client.PostAsJsonAsync("/api/auth/login", creds);

        var response = await client.PostAsJsonAsync("/api/widgets", new { name = "gizmo" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed record WidgetResponse(Guid Id, string Name, DateTimeOffset CreatedAt);
}
