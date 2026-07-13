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

    // Service types (8f21…) — five, with varied durations.
    private static readonly Guid OilChangeId = Guid.Parse("8f210000-0000-0000-0000-000000000001");
    private static readonly Guid TireRotationId = Guid.Parse("8f210000-0000-0000-0000-000000000002");
    private static readonly Guid BrakeInspectionId = Guid.Parse("8f210000-0000-0000-0000-000000000003");
    private static readonly Guid FullServiceId = Guid.Parse("8f210000-0000-0000-0000-000000000004");
    private static readonly Guid WheelAlignmentId = Guid.Parse("8f210000-0000-0000-0000-000000000005");

    // Dealerships (0e1c…) — two.
    private static readonly Guid Dealership1Id = Guid.Parse("0e1c0000-0000-0000-0000-000000000001");
    private static readonly Guid Dealership2Id = Guid.Parse("0e1c0000-0000-0000-0000-000000000002");

    // Service bays (9d31…) — three at dealership 1, two at dealership 2.
    private static readonly Guid D1Bay1Id = Guid.Parse("9d310000-0000-0000-0000-000000000001");
    private static readonly Guid D1Bay2Id = Guid.Parse("9d310000-0000-0000-0000-000000000002");
    private static readonly Guid D1Bay3Id = Guid.Parse("9d310000-0000-0000-0000-000000000003");
    private static readonly Guid D2Bay1Id = Guid.Parse("9d310000-0000-0000-0000-000000000004");
    private static readonly Guid D2Bay2Id = Guid.Parse("9d310000-0000-0000-0000-000000000005");

    // Technicians (aa87…) — three at dealership 1, two at dealership 2, with overlapping but
    // non-identical qualifications so shortages (no qualified technician) are reproducible.
    private static readonly Guid AlexChenId = Guid.Parse("aa870000-0000-0000-0000-000000000001");
    private static readonly Guid SamLeeId = Guid.Parse("aa870000-0000-0000-0000-000000000002");
    private static readonly Guid MariaGarciaId = Guid.Parse("aa870000-0000-0000-0000-000000000003");
    private static readonly Guid JohnSmithId = Guid.Parse("aa870000-0000-0000-0000-000000000004");
    private static readonly Guid EmilyDavisId = Guid.Parse("aa870000-0000-0000-0000-000000000005");

    // Vehicles (5b0a…) — three, all owned by the seeded dev customer.
    private static readonly Guid CorollaId = Guid.Parse("5b0a0000-0000-0000-0000-000000000001");
    private static readonly Guid CivicId = Guid.Parse("5b0a0000-0000-0000-0000-000000000002");
    private static readonly Guid F150Id = Guid.Parse("5b0a0000-0000-0000-0000-000000000003");

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
            ServiceType.Create(TireRotationId, "Tire rotation", TimeSpan.FromMinutes(30)),
            ServiceType.Create(BrakeInspectionId, "Brake inspection", TimeSpan.FromMinutes(60)),
            ServiceType.Create(FullServiceId, "Full service", TimeSpan.FromMinutes(120)),
            ServiceType.Create(WheelAlignmentId, "Wheel alignment", TimeSpan.FromMinutes(90)));

        db.Set<Dealership>().AddRange(
            Dealership.Create(Dealership1Id, "Springfield Downtown", "123 Main St, Springfield"),
            Dealership.Create(Dealership2Id, "Shelbyville North", "456 Oak Ave, Shelbyville"));

        db.Set<ServiceBay>().AddRange(
            ServiceBay.Create(D1Bay1Id, Dealership1Id, "Bay 1"),
            ServiceBay.Create(D1Bay2Id, Dealership1Id, "Bay 2"),
            ServiceBay.Create(D1Bay3Id, Dealership1Id, "Bay 3"),
            ServiceBay.Create(D2Bay1Id, Dealership2Id, "Bay 1"),
            ServiceBay.Create(D2Bay2Id, Dealership2Id, "Bay 2"));

        db.Set<Technician>().AddRange(
            Technician.Create(AlexChenId, Dealership1Id, "Alex Chen"),
            Technician.Create(SamLeeId, Dealership1Id, "Sam Lee"),
            Technician.Create(MariaGarciaId, Dealership1Id, "Maria Garcia"),
            Technician.Create(JohnSmithId, Dealership2Id, "John Smith"),
            Technician.Create(EmilyDavisId, Dealership2Id, "Emily Davis"));

        // Overlapping-but-distinct skills so "no qualified technician" is reproducible per service type.
        db.Set<TechnicianQualification>().AddRange(
            // Alex (D1): oil, tyres, brakes
            TechnicianQualification.Create(AlexChenId, OilChangeId),
            TechnicianQualification.Create(AlexChenId, TireRotationId),
            TechnicianQualification.Create(AlexChenId, BrakeInspectionId),
            // Sam (D1): oil, alignment
            TechnicianQualification.Create(SamLeeId, OilChangeId),
            TechnicianQualification.Create(SamLeeId, WheelAlignmentId),
            // Maria (D1): full service, brakes, alignment
            TechnicianQualification.Create(MariaGarciaId, FullServiceId),
            TechnicianQualification.Create(MariaGarciaId, BrakeInspectionId),
            TechnicianQualification.Create(MariaGarciaId, WheelAlignmentId),
            // John (D2): oil, tyres
            TechnicianQualification.Create(JohnSmithId, OilChangeId),
            TechnicianQualification.Create(JohnSmithId, TireRotationId),
            // Emily (D2): full service, alignment, brakes
            TechnicianQualification.Create(EmilyDavisId, FullServiceId),
            TechnicianQualification.Create(EmilyDavisId, WheelAlignmentId),
            TechnicianQualification.Create(EmilyDavisId, BrakeInspectionId));

        db.Set<Vehicle>().AddRange(
            Vehicle.Create(CorollaId, customer!.Id, "Toyota", "Corolla", 2020, "JTDBR32E020000001"),
            Vehicle.Create(CivicId, customer.Id, "Honda", "Civic", 2019, "2HGFG12678H000042"),
            Vehicle.Create(F150Id, customer.Id, "Ford", "F-150", 2021, "1FTFW1ET5DFC00099"));

        await db.SaveChangesAsync(cancellationToken);
    }
}
