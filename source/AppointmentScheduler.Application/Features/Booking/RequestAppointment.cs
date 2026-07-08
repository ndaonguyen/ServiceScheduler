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

        // Naive "first candidate" selection — seed data guarantees at least one of each in this slice.
        // Empty-list handling (→ 409 NO_BAY_AVAILABLE / NO_QUALIFIED_TECHNICIAN) is #5, not this slice.
        var bay = dealership.Bays[0];
        var technician = technicians[0];

        var start = request.RequestedStart;
        var end = start + serviceType.Duration; // BR-07: duration comes from the service type, not the client.

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
