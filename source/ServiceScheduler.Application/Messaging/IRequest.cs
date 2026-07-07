namespace ServiceScheduler.Application.Messaging;

/// <summary>A request (command or query) that produces a <typeparamref name="TResponse"/>.</summary>
public interface IRequest<out TResponse>;

/// <summary>The terminal handler invocation in a pipeline.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting behavior wrapped around handler execution (logging, validation, transactions...).
/// Registered open-generic; runs in registration order, outermost first.
/// </summary>
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
