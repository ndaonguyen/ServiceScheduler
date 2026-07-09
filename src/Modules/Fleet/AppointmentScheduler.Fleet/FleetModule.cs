using System.Reflection;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Messaging;
using AppointmentScheduler.Infrastructure.Fleet;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Fleet module composition: its request handlers and cross-module port implementations.</summary>
public static class FleetModule
{
    public static readonly Assembly Assembly = typeof(FleetModule).Assembly;

    public static IServiceCollection AddFleetModule(this IServiceCollection services)
    {
        services.AddRequestHandlers(Assembly);
        services.AddScoped<IServiceBayLookup, ServiceBayLookup>();
        services.AddScoped<IVehicleOwnershipQuery, VehicleOwnershipQuery>();
        return services;
    }
}
