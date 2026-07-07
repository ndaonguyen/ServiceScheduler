namespace ServiceScheduler.Application.Messaging;

/// <summary>Dispatches a request to its handler through the behavior pipeline.</summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
