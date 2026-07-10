using Microsoft.Extensions.Logging;

namespace AppointmentScheduler.BuildingBlocks.Messaging;

/// <summary>AppointmentScheduler pipeline behavior: logs every request and its elapsed time.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", name);
        var startTimestamp = TimeProvider.System.GetTimestamp();

        try
        {
            var response = await next();

            var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
            logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", name, elapsed.TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
            logger.LogError(ex, "Failed {RequestName} after {ElapsedMs}ms", name, elapsed.TotalMilliseconds);
            throw;
        }
    }
}
