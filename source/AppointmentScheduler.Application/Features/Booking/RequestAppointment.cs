using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Messaging;
using AppointmentScheduler.Domain.Booking;
using FluentResults;

namespace AppointmentScheduler.Application.Features.Booking;

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
            candidateBayIds, candidateTechnicianIds, start, end, cancellationToken);

        // Technician first, then bay (PRD §8/§10 order). FirstOrDefault covers both "none qualified"
        // and "all qualified busy" for the technician, and "no bay" / "all bays busy" for the bay.
        var technician = technicians.FirstOrDefault(t => !busy.BusyTechnicianIds.Contains(t.Id));
        if (technician is null) // BR-01 / AT-08
            return BookingErrors.NoQualifiedTechnician;

        var bay = dealership.Bays.FirstOrDefault(b => !busy.BusyBayIds.Contains(b.Id));
        if (bay is null) // BR-02 / AT-09
            return BookingErrors.NoBayAvailable;

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            VehicleId = request.VehicleId,
            DealershipId = request.DealershipId,
            ServiceTypeId = request.ServiceTypeId,
            ServiceBayId = bay.Id,
            TechnicianId = technician.Id,
            ScheduledStart = start,
            ScheduledEnd = end,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = clock.GetUtcNow(),
        };

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
}
