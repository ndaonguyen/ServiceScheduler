using ServiceScheduler.Application.Abstractions;
using ServiceScheduler.Application.Messaging;

namespace ServiceScheduler.Application.Features.Widgets;

/// <summary>Vertical slice: fetch a widget by id. Query + handler colocated.</summary>
public static class GetWidget
{
    public sealed record Query(Guid Id) : IRequest<Result?>;

    public sealed record Result(Guid Id, string Name, DateTimeOffset CreatedAt);

    internal sealed class Handler(IWidgetRepository repository) : IRequestHandler<Query, Result?>
    {
        public async Task<Result?> Handle(Query request, CancellationToken cancellationToken)
        {
            var widget = await repository.GetByIdAsync(request.Id, cancellationToken);

            return widget is null
                ? null
                : new Result(widget.Id, widget.Name, widget.CreatedAt);
        }
    }
}
