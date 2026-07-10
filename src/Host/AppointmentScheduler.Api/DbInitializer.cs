using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AppointmentScheduler.BuildingBlocks.Persistence;
using AppointmentScheduler.Booking.Domain;
using AppointmentScheduler.Catalog.Domain;
using AppointmentScheduler.Fleet.Domain;
using AppointmentScheduler.Workforce.Domain;

namespace AppointmentScheduler.Api;

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

        await SeedReferenceDataAsync(sp, cancellationToken);
    }

    // --- Development reference data ---------------------------------------------------------------
    // Fixed ids so the seeded rows are stable across restarts and can be referenced by hand when
    // exercising POST /api/appointments. Called only from MigrateAndSeedAsync, which Program.cs
    // guards to Development.

    private const string DevCustomerEmail = "customer@example.com";
    private const string DevCustomerPassword = "Passw0rd!$";

    private static readonly Guid OilChangeId = Guid.Parse("8f210000-0000-0000-0000-000000000001");
    private static readonly Guid TireRotationId = Guid.Parse("8f210000-0000-0000-0000-000000000002");
    private static readonly Guid DealershipId = Guid.Parse("0e1c0000-0000-0000-0000-000000000001");
    private static readonly Guid Bay1Id = Guid.Parse("9d310000-0000-0000-0000-000000000001");
    private static readonly Guid Bay2Id = Guid.Parse("9d310000-0000-0000-0000-000000000002");
    private static readonly Guid TechnicianId = Guid.Parse("aa870000-0000-0000-0000-000000000001");
    private static readonly Guid VehicleId = Guid.Parse("5b0a0000-0000-0000-0000-000000000001");

    private static async Task SeedReferenceDataAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        // Ensure a development customer exists to own the seeded vehicle.
        var userManager = sp.GetRequiredService<UserManager<AppUser>>();
        var customer = await userManager.FindByEmailAsync(DevCustomerEmail);
        if (customer is null)
        {
            customer = new AppUser { UserName = DevCustomerEmail, Email = DevCustomerEmail, EmailConfirmed = true };
            var created = await userManager.CreateAsync(customer, DevCustomerPassword);
            if (created.Succeeded)
            {
                await userManager.AddToRoleAsync(customer, "user");
            }
        }

        // Idempotent: reference rows are seeded once. Guard on the dealership set.
        if (await db.Set<Dealership>().AnyAsync(cancellationToken))
        {
            return;
        }

        db.Set<ServiceType>().AddRange(
            ServiceType.Create(OilChangeId, "Oil change", TimeSpan.FromMinutes(45)),
            ServiceType.Create(TireRotationId, "Tire rotation", TimeSpan.FromMinutes(30)));

        db.Set<Dealership>().Add(Dealership.Create(DealershipId, "Springfield Downtown", "123 Main St, Springfield"));

        db.Set<ServiceBay>().AddRange(
            ServiceBay.Create(Bay1Id, DealershipId, "Bay 1"),
            ServiceBay.Create(Bay2Id, DealershipId, "Bay 2"));

        db.Set<Technician>().Add(Technician.Create(TechnicianId, DealershipId, "Alex Chen"));
        db.Set<TechnicianQualification>().AddRange(
            TechnicianQualification.Create(TechnicianId, OilChangeId),
            TechnicianQualification.Create(TechnicianId, TireRotationId));

        db.Set<Vehicle>().Add(
            Vehicle.Create(VehicleId, customer!.Id, "Toyota", "Corolla", 2020, "JTDBR32E020000001"));

        await db.SaveChangesAsync(cancellationToken);
    }
}
