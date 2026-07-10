using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppointmentScheduler.BuildingBlocks.Persistence;

public static class PersistenceRegistration
{
    /// <summary>
    /// Registers the EF Core / PostgreSQL <see cref="AppDbContext"/>. The caller must also register
    /// a <see cref="ModuleConfigurations"/> describing which module assemblies carry EF mappings.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:AppDb'. Set it in appsettings or the environment.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
