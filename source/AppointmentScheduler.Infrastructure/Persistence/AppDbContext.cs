using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AppointmentScheduler.Domain.Booking;
using AppointmentScheduler.Domain.Catalog;
using AppointmentScheduler.Domain.Fleet;
using AppointmentScheduler.Domain.Workforce;

namespace AppointmentScheduler.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work. Inherits the ASP.NET Core Identity schema and adds the application's own
/// aggregates. This service uses <b>role-based</b> authorization only, so the unused Identity
/// satellite tables (per-user/role claims, external logins, user tokens) are mapped out — leaving
/// just the user store (AspNetUsers), the role store (AspNetRoles), and the user↔role join
/// (AspNetUserRoles). Entity mappings live in <c>Persistence/Configurations</c> and are picked up
/// by reflection.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Booking module
    public DbSet<Appointment> Appointments => Set<Appointment>();

    // Fleet module
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Dealership> Dealerships => Set<Dealership>();
    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();

    // Workforce module
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<TechnicianQualification> TechnicianQualifications => Set<TechnicianQualification>();

    // Catalog module
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // configures the Identity tables

        // RBAC-only: drop the Identity tables this template never uses. Claims-based authz,
        // external/social logins, and the user-token store (2FA / reset / email-confirm) are all
        // out of scope — see docs/authentication.md.
        modelBuilder.Ignore<IdentityUserClaim<string>>();
        modelBuilder.Ignore<IdentityRoleClaim<string>>();
        modelBuilder.Ignore<IdentityUserLogin<string>>();
        modelBuilder.Ignore<IdentityUserToken<string>>();

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
