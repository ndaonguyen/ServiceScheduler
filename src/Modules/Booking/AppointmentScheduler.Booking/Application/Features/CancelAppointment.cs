using AppointmentScheduler.Booking.Application.Abstractions;
using AppointmentScheduler.Booking.Domain;
using AppointmentScheduler.BuildingBlocks.Abstractions;
using AppointmentScheduler.BuildingBlocks.Messaging;
using FluentResults;

namespace AppointmentScheduler.Booking.Application.Features;

/// <summary>
/// Cancels an existing appointment on behalf of the authenticated caller. Cancellation is a soft
/// state transition (<see cref="AppointmentStatus.Cancelled"/>), never a row delete — the row stays
/// for history and simply drops out of the partial <c>EXCLUDE</c> constraint and the availability
/// query, freeing the slot. The caller must own the appointment (resolved from <see cref="ICurrentUser"/>,
/// never the request body).
/// </summary>
public sealed record CancelAppointment(Guid AppointmentId) : IRequest<Result>;

internal sealed class CancelAppointmentHandler(
    ICurrentUser currentUser,
    IAppointmentRepository appointments,
    TimeProvider clock) : IRequestHandler<CancelAppointment, Result>
{
    public async Task<Result> Handle(CancelAppointment request, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, cancellationToken);
        if (appointment is null)
            return BookingErrors.AppointmentNotFound;
        if (appointment.OwnerId != currentUser.UserId)
            return BookingErrors.AppointmentNotOwned;

        try
        {
            appointment.Cancel(clock.GetUtcNow());
        }
        catch (AppointmentAlreadyCancelledException)
        {
            return BookingErrors.AppointmentAlreadyCancelled;
        }
        catch (AppointmentInPastException)
        {
            return BookingErrors.AppointmentInPast;
        }

        await appointments.UpdateAsync(appointment, cancellationToken);
        return Result.Ok();
    }
}
