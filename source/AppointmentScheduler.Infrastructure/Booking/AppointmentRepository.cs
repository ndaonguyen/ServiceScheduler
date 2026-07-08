using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Domain.Booking;
using AppointmentScheduler.Infrastructure.Persistence;

namespace AppointmentScheduler.Infrastructure.Booking;

/// <summary>Booking's implementation of <see cref="IAppointmentRepository"/> over <see cref="AppDbContext"/>.</summary>
internal sealed class AppointmentRepository(AppDbContext db) : IAppointmentRepository
{
    public async Task AddAsync(Appointment appointment, CancellationToken ct = default)
    {
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);
    }
}
