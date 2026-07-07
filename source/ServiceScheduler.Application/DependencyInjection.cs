using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ServiceScheduler.Application.Messaging;

namespace ServiceScheduler.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the mediator, every <c>IRequestHandler&lt;,&gt;</c> in this assembly, and the
    /// pipeline behaviors. Call from the API composition root.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISender, Mediator>();

        var assembly = Assembly.GetExecutingAssembly();
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

        // Pipeline behaviors run outermost-first in registration order.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
