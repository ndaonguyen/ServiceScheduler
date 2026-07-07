using Microsoft.EntityFrameworkCore;
using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Domain.Widgets;
using AppointmentScheduler.Infrastructure.Persistence;

namespace AppointmentScheduler.Infrastructure.Widgets;

/// <summary>
/// EF Core adapter for <see cref="IWidgetRepository"/>. Backed by <see cref="AppDbContext"/>
/// (PostgreSQL in production). Swap providers via the connection string / DI registration.
/// </summary>
internal sealed class EfWidgetRepository(AppDbContext db) : IWidgetRepository
{
    public async Task AddAsync(Widget widget, CancellationToken cancellationToken)
    {
        await db.Widgets.AddAsync(widget, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Widget?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Widgets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Widget>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Widgets
            .AsNoTracking()
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
}
