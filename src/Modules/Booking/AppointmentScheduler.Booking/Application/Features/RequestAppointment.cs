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
/// Books an appointment for the authenticated caller. Validates the request references and timing
/// (returning a <see cref="BookingError"/> with the PRD §8 code on failure), then resolves the
/// service duration, the dealership's bays, ownership, and the qualified technicians, and naively
/// takes the <b>first</b> candidate bay and technician (no overlap/conflict checking — that is
/// #5/#6/#7). The owner is taken from <see cref="ICurrentUser"/>, never the request body.
/// </summary>
public sealed record RequestAppointment(
    Guid VehicleId,
    Guid DealershipId,
    Guid ServiceTypeId,
    DateTimeOffset RequestedStart) : IRequest<Result<RequestAppointmentResponse>>;

public sealed record RequestAppointmentResponse(
    Guid AppointmentId,
    DealershipRef Dealership,
    ServiceTypeRef ServiceType,
    VehicleRef Vehicle,
    ServiceBayRef ServiceBay,
    TechnicianRef Technician,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    string Status);

public sealed record DealershipRef(Guid Id, string Name);
public sealed record ServiceTypeRef(Guid Id, string Name, int DurationMinutes);
public sealed record VehicleRef(Guid Id);
public sealed record ServiceBayRef(Guid Id, string Label);
public sealed record TechnicianRef(Guid Id, string Name);

internal sealed class RequestAppointmentHandler(
    ICurrentUser currentUser,
    IServiceTypeLookup serviceTypes,
    IServiceBayLookup serviceBays,
    IVehicleOwnershipQuery vehicleOwnership,
    IQualifiedTechnicianLookup qualifiedTechnicians,
    IAppointmentRepository appointments,
    TimeProvider clock) : IRequestHandler<RequestAppointment, Result<RequestAppointmentResponse>>
{
    public async Task<Result<RequestAppointmentResponse>> Handle(RequestAppointment request, CancellationToken cancellationToken)
    {
        // The endpoint requires authorization, so a caller id is always present here.
        var ownerId = currentUser.UserId!;

        // Guard clauses follow the PRD §10 sequence order. Each maps a failure signal the query ports
        // already return to a PRD §8 error; on failure nothing is persisted.
        var serviceType = await serviceTypes.GetAsync(request.ServiceTypeId, cancellationToken);
        if (serviceType is null) // VR-05 / AT-05
            return BookingErrors.ServiceTypeNotFound;

        if (request.RequestedStart <= clock.GetUtcNow()) // VR-06 / AT-06 (strictly in the future)
            return BookingErrors.RequestedStartInPast;

        var dealership = await serviceBays.ListByDealershipAsync(request.DealershipId, cancellationToken);
        if (dealership is null) // VR-04 / AT-04
            return BookingErrors.DealershipNotFound;

        var ownership = await vehicleOwnership.CheckAsync(request.VehicleId, ownerId, cancellationToken);
        if (ownership == VehicleOwnership.NotFound) // VR-02 / AT-02
            return BookingErrors.VehicleNotFound;
        if (ownership == VehicleOwnership.NotOwned) // VR-03 / AT-03
            return BookingErrors.VehicleNotOwned;

        var technicians = await qualifiedTechnicians.ListAsync(request.DealershipId, request.ServiceTypeId, cancellationToken);

        var start = request.RequestedStart;
        var end = start + serviceType.Duration; // BR-07: duration comes from the service type, not the client.

        // Narrow the dealership-scoped candidates to those free for [start, end). Passing only these
        // candidate ids keeps other dealerships' resources out of consideration (BR-05/BR-06).
        var candidateBayIds = dealership.Bays.Select(b => b.Id).ToList();
        var candidateTechnicianIds = technicians.Select(t => t.Id).ToList();
        var busy = await appointments.GetBusyResourcesAsync(
            candidateBayIds, candidateTechnicianIds, start, end, ct: cancellationToken);

        // Technician first, then bay (PRD §8/§10 order). Keep the whole ordered free list of each so a
        // race loss on insert (#6 EXCLUDE constraint) can retry with the next free candidate.
        var freeTechnicians = technicians.Where(t => !busy.BusyTechnicianIds.Contains(t.Id)).ToList();
        if (freeTechnicians.Count == 0) // BR-01 / AT-08
            return BookingErrors.NoQualifiedTechnician;

        var freeBays = dealership.Bays.Where(b => !busy.BusyBayIds.Contains(b.Id)).ToList();
        if (freeBays.Count == 0) // BR-02 / AT-09
            return BookingErrors.NoBayAvailable;

        var technicianIndex = 0;
        var bayIndex = 0;
        var lastConflict = BookingResource.ServiceBay;

        // Initial attempt + one retry (PRD §10 "retry once with next candidate"). A concurrent booking
        // can take the chosen bay/technician between our availability read and the insert; the DB
        // constraint rejects it and we advance to the next free candidate in the losing dimension.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var technician = freeTechnicians[technicianIndex];
            var bay = freeBays[bayIndex];

            // BR-07: the window is [start, start + duration); the aggregate enforces its invariants.
            var appointment = Appointment.Schedule(
                ownerId,
                request.VehicleId,
                request.DealershipId,
                request.ServiceTypeId,
                bay.Id,
                technician.Id,
                start,
                serviceType.Duration,
                clock.GetUtcNow());

            try
            {
                await appointments.AddAsync(appointment, cancellationToken);

                return Result.Ok(new RequestAppointmentResponse(
                    appointment.Id,
                    new DealershipRef(request.DealershipId, dealership.DealershipName),
                    new ServiceTypeRef(serviceType.Id, serviceType.Name, (int)serviceType.Duration.TotalMinutes),
                    new VehicleRef(request.VehicleId),
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

        // The single retry was spent and still conflicted (a next candidate existed): stop and return
        // the 409 for the dimension that last lost the race.
        return lastConflict == BookingResource.ServiceBay
            ? BookingErrors.NoBayAvailable
            : BookingErrors.NoQualifiedTechnician;
    }
}
