using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceScheduler.Infrastructure.Persistence;

namespace ServiceScheduler.Api.Tests;

/// <summary>
/// A TimeProvider whose "now" only moves when the test tells it to, so expiry/TTL behavior can be
/// exercised without waiting real days.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>
/// Factory variant that keeps Program's real JWT scheme instead of the header-driven test scheme,
/// for tests that need to exercise the actual login/cookie/RBAC pipeline end-to-end.
/// </summary>
internal sealed class JwtTestFactory : TestWebAppFactory
{
    protected override bool UseTestAuthentication => false;

    public ManualTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    // Development seeds RBAC roles via DbInitializer; the in-memory test host does not, so seed
    // the roles register() assigns. The in-memory store is shared across scopes by name.
    public async Task SeedRolesAsync()
    {
        using var scope = Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "admin", "user" })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>Grants an already-registered user an additional role (e.g. "admin").</summary>
    public async Task GrantRoleAsync(string email, string role)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"User '{email}' not found.");
        await users.AddToRoleAsync(user, role);
    }
}
