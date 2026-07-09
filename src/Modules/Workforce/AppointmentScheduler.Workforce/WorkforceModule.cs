using System.Reflection;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Messaging;
using AppointmentScheduler.Infrastructure.Workforce;

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
