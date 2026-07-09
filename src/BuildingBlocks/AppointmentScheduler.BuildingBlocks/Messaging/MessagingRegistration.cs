using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace AppointmentScheduler.BuildingBlocks.Messaging;

/// <summary>
/// Composition helpers for the in-process mediator. <see cref="AddMessaging"/> registers the shared
/// mediator and pipeline behaviors (call once from the host); <see cref="AddRequestHandlers"/> scans
/// a single module's assembly for its <see cref="IRequestHandler{TRequest,TResponse}"/> implementations
/// (each module calls it for its own assembly, so handler registration stays module-owned).
/// </summary>
public static class MessagingRegistration
{
    /// <summary>Registers the mediator (<see cref="ISender"/>) and the pipeline behaviors.</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<ISender, Mediator>();

        // Pipeline behaviors run outermost-first in registration order.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }

    /// <summary>Registers every <c>IRequestHandler&lt;,&gt;</c> declared in <paramref name="assembly"/>.</summary>
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, Assembly assembly)
    {
        var openHandler = typeof(IRequestHandler<,>);

        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openHandler);

            foreach (var handlerInterface in handlerInterfaces)
            {
                services.AddTransient(handlerInterface, type);
            }
        }

        return services;
    }
}
