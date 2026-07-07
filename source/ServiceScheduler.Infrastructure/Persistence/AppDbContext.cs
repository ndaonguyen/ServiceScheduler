using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Domain.Widgets;

namespace ServiceScheduler.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work. Inherits the ASP.NET Core Identity schema and adds the application's own
/// aggregates. This template uses <b>role-based</b> authorization only, so the unused Identity
/// satellite tables (per-user/role claims, external logins, user tokens) are mapped out — leaving
/// just the user store (AspNetUsers), the role store (AspNetRoles), and the user↔role join
/// (AspNetUserRoles). Entity mappings live in <c>Persistence/Configurations</c> and are picked up
/// by reflection.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
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

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
