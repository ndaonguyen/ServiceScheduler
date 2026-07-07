using AppointmentScheduler.Domain.Widgets;

namespace AppointmentScheduler.Application.Abstractions;

/// <summary>
/// Port for widget persistence. Implemented in the Infrastructure layer
/// (dependency rule: Application defines the interface, Infrastructure depends on it).
/// </summary>
public interface IWidgetRepository
{
    Task AddAsync(Widget widget, CancellationToken cancellationToken);

    Task<Widget?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Widget>> GetAllAsync(CancellationToken cancellationToken);
}
