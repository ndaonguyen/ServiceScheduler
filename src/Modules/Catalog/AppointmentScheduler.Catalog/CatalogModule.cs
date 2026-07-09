using System.Reflection;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.BuildingBlocks.Messaging;
using AppointmentScheduler.Catalog.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Catalog module composition: its request handlers and cross-module port implementations.</summary>
public static class CatalogModule
{
    public static readonly Assembly Assembly = typeof(CatalogModule).Assembly;

    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddRequestHandlers(Assembly);
        services.AddScoped<IServiceTypeLookup, ServiceTypeLookup>();
        return services;
    }
}
