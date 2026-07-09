using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using AppointmentScheduler.Infrastructure.Persistence;

namespace AppointmentScheduler.Api;

/// <summary>
/// Lets <c>dotnet ef</c> build an <see cref="AppDbContext"/> at design time without booting the API.
/// Override the connection via the <c>AppDb__ConnectionString</c> environment variable; the default
/// targets the local docker-compose Postgres. Lives in the host because it must enumerate the module
/// assemblies (which the host references) to build the full model. Used only by tooling.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AppDb__ConnectionString")
            ?? "Host=localhost;Port=5432;Database=appointmentscheduler;Username=appointmentscheduler;Password=appointmentscheduler";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options, HostModules.Configurations);
    }
}
