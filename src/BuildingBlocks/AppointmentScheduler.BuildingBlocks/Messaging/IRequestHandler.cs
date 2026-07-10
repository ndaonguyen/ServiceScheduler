namespace AppointmentScheduler.BuildingBlocks.Messaging;

/// <summary>Handles a single <typeparamref name="TRequest"/> slice.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
