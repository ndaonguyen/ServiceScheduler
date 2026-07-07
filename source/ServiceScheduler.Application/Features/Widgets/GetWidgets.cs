using ServiceScheduler.Application.Abstractions;
using ServiceScheduler.Application.Messaging;

namespace ServiceScheduler.Application.Features.Widgets;

/// <summary>Vertical slice: list all widgets. Query + handler colocated.</summary>
public static class GetWidgets
{
    public sealed record Query : IRequest<IReadOnlyList<Result>>;

    public sealed record Result(Guid Id, string Name, DateTimeOffset CreatedAt);

    internal sealed class Handler(IWidgetRepository repository) : IRequestHandler<Query, IReadOnlyList<Result>>
    {
        public async Task<IReadOnlyList<Result>> Handle(Query request, CancellationToken cancellationToken)
        {
            var widgets = await repository.GetAllAsync(cancellationToken);

            return widgets
                .Select(w => new Result(w.Id, w.Name, w.CreatedAt))
                .ToList();
        }
    }
}
