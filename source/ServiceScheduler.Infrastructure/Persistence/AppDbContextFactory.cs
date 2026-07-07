using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServiceScheduler.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build an <see cref="AppDbContext"/> at design time without booting the API.
/// Override the connection via the <c>AppDb__ConnectionString</c> environment variable; the default
/// targets the local docker-compose Postgres. Used only by tooling — never at runtime.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AppDb__ConnectionString")
            ?? "Host=localhost;Port=5432;Database=servicescheduler;Username=servicescheduler;Password=servicescheduler";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
