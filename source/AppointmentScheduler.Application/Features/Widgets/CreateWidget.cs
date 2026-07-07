using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Messaging;
using AppointmentScheduler.Domain.Widgets;

namespace AppointmentScheduler.Application.Features.Widgets;

/// <summary>Vertical slice: create a widget. Command + handler colocated.</summary>
public static class CreateWidget
{
    public sealed record Command(string Name) : IRequest<Result>;

    public sealed record Result(Guid Id, string Name, DateTimeOffset CreatedAt);

    internal sealed class Handler(IWidgetRepository repository, TimeProvider timeProvider)
        : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ArgumentException("Name is required.", nameof(request));
            }

            var widget = Widget.Create(request.Name, timeProvider.GetUtcNow());
            await repository.AddAsync(widget, cancellationToken);

            return new Result(widget.Id, widget.Name, widget.CreatedAt);
        }
    }
}
