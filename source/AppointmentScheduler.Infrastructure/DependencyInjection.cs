using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Infrastructure.Booking;
using AppointmentScheduler.Infrastructure.Catalog;
using AppointmentScheduler.Infrastructure.Fleet;
using AppointmentScheduler.Infrastructure.Persistence;
using AppointmentScheduler.Infrastructure.Security;
using AppointmentScheduler.Infrastructure.Workforce;

namespace AppointmentScheduler.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure adapters (port implementations) and the EF Core / PostgreSQL
    /// persistence stack. Call from the API composition root.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:AppDb'. Set it in appsettings or the environment.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // Booking module: cross-module query ports (implemented in the owning module) + repository.
        services.AddScoped<IServiceTypeLookup, ServiceTypeLookup>();          // Catalog
        services.AddScoped<IServiceBayLookup, ServiceBayLookup>();            // Fleet
        services.AddScoped<IVehicleOwnershipQuery, VehicleOwnershipQuery>();  // Fleet
        services.AddScoped<IQualifiedTechnicianLookup, QualifiedTechnicianLookup>(); // Workforce
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();  // Booking

        return services;
    }
}
