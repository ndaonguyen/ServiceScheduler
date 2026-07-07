using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppointmentScheduler.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations and seeds RBAC roles plus an optional development admin
/// user. Call once on startup (typically guarded to Development). Safe to run repeatedly.
/// </summary>
public static class DbInitializer
{
    /// <summary>Roles the app knows about. Extend as needed; referenced by RequireRole(...).</summary>
    public static readonly string[] Roles = ["admin", "user"];

    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Optional dev admin: set Seed:Admin:Email + Seed:Admin:Password in configuration
        // (e.g. user-secrets / appsettings.Development.json) to auto-create an admin login.
        var config = sp.GetRequiredService<IConfiguration>();
        var email = config["Seed:Admin:Email"];
        var password = config["Seed:Admin:Password"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            var userManager = sp.GetRequiredService<UserManager<AppUser>>();
            if (await userManager.FindByEmailAsync(email) is null)
            {
                var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
                var result = await userManager.CreateAsync(admin, password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "admin");
                }
            }
        }
    }
}
