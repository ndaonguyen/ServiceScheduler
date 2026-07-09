using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppointmentScheduler.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work. Inherits the ASP.NET Core Identity schema and adds the application's own
/// aggregates. This service uses <b>role-based</b> authorization only, so the unused Identity
/// satellite tables (per-user/role claims, external logins, user tokens) are mapped out — leaving
/// just the user store (AspNetUsers), the role store (AspNetRoles), and the user↔role join
/// (AspNetUserRoles).
///
/// The context is shared across modules but owns no module's entities: module aggregates are reached
/// via <c>Set&lt;T&gt;()</c> and their mappings are contributed by each module's
/// <c>IEntityTypeConfiguration&lt;&gt;</c>, discovered by scanning the assemblies named in
/// <see cref="ModuleConfigurations"/>. This keeps Persistence free of any compile-time reference to a
/// module (each module owns a Postgres schema; extraction moves the schema with the module).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ModuleConfigurations modules)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

        // Auth/identity mappings owned here (e.g. RefreshTokenConfiguration).
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Each module contributes its own aggregate mappings from its own assembly.
        foreach (var assembly in modules.Assemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }
}
