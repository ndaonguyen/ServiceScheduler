using AppointmentScheduler.Booking.Application.Abstractions;
using AppointmentScheduler.Booking.Domain;
using AppointmentScheduler.BuildingBlocks.Abstractions;
using AppointmentScheduler.BuildingBlocks.Messaging;
using AppointmentScheduler.Catalog.Contracts;
using AppointmentScheduler.Fleet.Contracts;
using AppointmentScheduler.Workforce.Contracts;
using FluentResults;

namespace AppointmentScheduler.Booking.Application.Features;

/// <summary>
/// Moves an existing appointment to a new start time on behalf of the authenticated caller. Keeps the
/// appointment's identity and confirmed status, re-resolves which bay/technician are free for the new
/// window (the same query ports and retry-once-on-conflict logic as <see cref="RequestAppointment"/>),
/// and re-stamps the aggregate in place. Duration is re-derived from the service type (BR-07); the
/// appointment's own current slot is excluded from the availability check so a small shift does not
/// conflict with itself. Reuses <see cref="RequestAppointmentResponse"/> as the success payload.
/// </summary>
public sealed record RescheduleAppointment(Guid AppointmentId, DateTimeOffset NewStart)
    : IRequest<Result<RequestAppointmentResponse>>;

internal sealed class RescheduleAppointmentHandler(
    ICurrentUser currentUser,
    IServiceTypeLookup serviceTypes,
    IServiceBayLookup serviceBays,
    IQualifiedTechnicianLookup qualifiedTechnicians,
    IAppointmentRepository appointments,
    TimeProvider clock) : IRequestHandler<RescheduleAppointment, Result<RequestAppointmentResponse>>
{
    public async Task<Result<RequestAppointmentResponse>> Handle(RescheduleAppointment request, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, cancellationToken);
        if (appointment is null)
            return BookingErrors.AppointmentNotFound;
        if (appointment.OwnerId != currentUser.UserId)
            return BookingErrors.AppointmentNotOwned;
        if (appointment.Status != AppointmentStatus.Confirmed)
            return BookingErrors.AppointmentAlreadyCancelled;
        if (request.NewStart <= clock.GetUtcNow()) // strictly in the future (VR-06 analogue)
            return BookingErrors.AppointmentInPast;

        // Duration is re-resolved from the service type, never taken from the caller (BR-07). The
        // service type was validated at booking time; guard defensively in case it has since gone.
        var serviceType = await serviceTypes.GetAsync(appointment.ServiceTypeId, cancellationToken);
        if (serviceType is null)
            return BookingErrors.ServiceTypeNotFound;

        var start = request.NewStart;
        var end = start + serviceType.Duration;

        var dealership = await serviceBays.ListByDealershipAsync(appointment.DealershipId, cancellationToken);
        if (dealership is null)
            return BookingErrors.DealershipNotFound;

        var technicians = await qualifiedTechnicians.ListAsync(appointment.DealershipId, appointment.ServiceTypeId, cancellationToken);

        // Narrow the dealership-scoped candidates to those free for the NEW window, excluding this
        // appointment's own current (old) slot so a shift that overlaps it is not a false conflict.
        var busy = await appointments.GetBusyResourcesAsync(
            dealership.Bays.Select(b => b.Id).ToList(),
            technicians.Select(t => t.Id).ToList(),
            start, end, appointment.Id, cancellationToken);

        var freeTechnicians = technicians.Where(t => !busy.BusyTechnicianIds.Contains(t.Id)).ToList();
        if (freeTechnicians.Count == 0) // BR-01
            return BookingErrors.NoQualifiedTechnician;

        var freeBays = dealership.Bays.Where(b => !busy.BusyBayIds.Contains(b.Id)).ToList();
        if (freeBays.Count == 0) // BR-02
            return BookingErrors.NoBayAvailable;

        var technicianIndex = 0;
        var bayIndex = 0;
        var lastConflict = BookingResource.ServiceBay;

        // Initial attempt + one retry, mirroring RequestAppointment: a concurrent booking can take the
        // chosen resource between our read and the update; the DB constraint rejects it and we advance
        // to the next free candidate in the losing dimension.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var technician = freeTechnicians[technicianIndex];
            var bay = freeBays[bayIndex];

            try
            {
                appointment.RescheduleTo(bay.Id, technician.Id, start, serviceType.Duration, clock.GetUtcNow());
                await appointments.UpdateAsync(appointment, cancellationToken);

                return Result.Ok(new RequestAppointmentResponse(
                    appointment.Id,
                    new DealershipRef(appointment.DealershipId, dealership.DealershipName),
                    new ServiceTypeRef(serviceType.Id, serviceType.Name, (int)serviceType.Duration.TotalMinutes),
                    new VehicleRef(appointment.VehicleId),
                    new ServiceBayRef(bay.Id, bay.Label),
                    new TechnicianRef(technician.Id, technician.Name),
                    start,
                    end,
                    appointment.Status.ToString()));
            }
            catch (AppointmentSlotConflictException conflict)
            {
                lastConflict = conflict.Resource;
                if (conflict.Resource == BookingResource.ServiceBay)
                {
                    if (++bayIndex >= freeBays.Count)
                        return BookingErrors.NoBayAvailable;
                }
                else if (++technicianIndex >= freeTechnicians.Count)
                {
                    return BookingErrors.NoQualifiedTechnician;
                }
            }
        }

        return lastConflict == BookingResource.ServiceBay
            ? BookingErrors.NoBayAvailable
            : BookingErrors.NoQualifiedTechnician;
    }
}
