using System.Reflection;
using AppointmentScheduler.Workforce.Contracts;
using AppointmentScheduler.BuildingBlocks.Messaging;
using AppointmentScheduler.Workforce.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Workforce module composition: its request handlers and cross-module port implementations.</summary>
public static class WorkforceModule
{
    public static readonly Assembly Assembly = typeof(WorkforceModule).Assembly;

    public static IServiceCollection AddWorkforceModule(this IServiceCollection services)
    {
        services.AddRequestHandlers(Assembly);
        services.AddScoped<IQualifiedTechnicianLookup, QualifiedTechnicianLookup>();
        return services;
    }
}
