using Microsoft.Extensions.DependencyInjection;

namespace AppointmentScheduler.Application.Messaging;

/// <summary>
/// Minimal in-process mediator. Resolves the single <see cref="IRequestHandler{TRequest,TResponse}"/>
/// for a request, wraps it with any registered <see cref="IPipelineBehavior{TRequest,TResponse}"/>,
/// and invokes it. No external dependency.
/// </summary>
internal sealed class Mediator(IServiceProvider provider) : ISender
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var responseType = typeof(TResponse);

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
        var handler = provider.GetRequiredService(handlerType);
        var handleMethod = handlerType.GetMethod("Handle")!;

        RequestHandlerDelegate<TResponse> pipeline = () =>
            (Task<TResponse>)handleMethod.Invoke(handler, [request, cancellationToken])!;

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);
        var behaviorHandle = behaviorType.GetMethod("Handle")!;
        var behaviors = ((IEnumerable<object?>)provider.GetServices(behaviorType)).Reverse();

        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            pipeline = () => (Task<TResponse>)behaviorHandle.Invoke(behavior, [request, next, cancellationToken])!;
        }

        return await pipeline();
    }
}
