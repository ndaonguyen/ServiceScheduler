using System.Collections.Concurrent;
using AwesomeAssertions;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Features.Widgets;
using AppointmentScheduler.Domain.Widgets;
using Xunit;

namespace AppointmentScheduler.Application.Tests.Widgets;

public class CreateWidgetTests
{
    [Fact]
    public async Task Handle_persists_widget_and_returns_result()
    {
        var repository = new FakeWidgetRepository();
        var handler = new CreateWidget.Handler(repository, TimeProvider.System);

        var result = await handler.Handle(new CreateWidget.Command("gizmo"), CancellationToken.None);

        result.Name.Should().Be("gizmo");
        result.Id.Should().NotBeEmpty();

        var stored = await repository.GetByIdAsync(result.Id, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("gizmo");
    }

    [Fact]
    public async Task Handle_rejects_blank_name()
    {
        var handler = new CreateWidget.Handler(new FakeWidgetRepository(), TimeProvider.System);

        var act = async () => await handler.Handle(new CreateWidget.Command("  "), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeWidgetRepository : IWidgetRepository
    {
        private readonly ConcurrentDictionary<Guid, Widget> _store = new();

        public Task AddAsync(Widget widget, CancellationToken cancellationToken)
        {
            _store[widget.Id] = widget;
            return Task.CompletedTask;
        }

        public Task<Widget?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            _store.TryGetValue(id, out var widget);
            return Task.FromResult(widget);
        }

        public Task<IReadOnlyList<Widget>> GetAllAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<Widget> widgets = _store.Values.ToList();
            return Task.FromResult(widgets);
        }
    }
}
