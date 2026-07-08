using AppointmentScheduler.Application.Abstractions;
using AppointmentScheduler.Application.Messaging;
using AppointmentScheduler.Domain.Booking;

namespace AppointmentScheduler.Application.Features.Booking;

/// <summary>
/// Books an appointment for the authenticated caller. This is the walking-skeleton happy path: it
/// resolves the service duration, the dealership's bays, ownership, and the qualified technicians,
/// then naively takes the <b>first</b> candidate bay and technician (no overlap/conflict checking —
/// that is #5/#6/#7). The owner is taken from <see cref="ICurrentUser"/>, never the request body.
/// </summary>
public sealed record RequestAppointment(
    Guid VehicleId,
    Guid DealershipId,
    Guid ServiceTypeId,
    DateTimeOffset RequestedStart) : IRequest<RequestAppointmentResponse>;

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
    TimeProvider clock) : IRequestHandler<RequestAppointment, RequestAppointmentResponse>
{
    public async Task<RequestAppointmentResponse> Handle(RequestAppointment request, CancellationToken cancellationToken)
    {
        // The endpoint requires authorization, so a caller id is always present here.
        var ownerId = currentUser.UserId!;

        var serviceType = await serviceTypes.GetAsync(request.ServiceTypeId, cancellationToken);
        var dealership = await serviceBays.ListByDealershipAsync(request.DealershipId, cancellationToken);
        // Called for sequence-diagram fidelity and to exercise the port; the Owned/NotOwned/NotFound
        // branch (403/404) is added in #4. This slice assumes the happy-path Owned result.
        _ = await vehicleOwnership.CheckAsync(request.VehicleId, ownerId, cancellationToken);
        var technicians = await qualifiedTechnicians.ListAsync(request.DealershipId, request.ServiceTypeId, cancellationToken);

        // Naive "first candidate" selection — seed data guarantees at least one of each in this slice.
        var bay = dealership!.Bays[0];
        var technician = technicians[0];

        var start = request.RequestedStart;
        var end = start + serviceType!.Duration; // BR-07: duration comes from the service type, not the client.

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

        return new RequestAppointmentResponse(
            appointment.Id,
            new DealershipRef(request.DealershipId, dealership.DealershipName),
            new ServiceTypeRef(serviceType.Id, serviceType.Name, (int)serviceType.Duration.TotalMinutes),
            new VehicleRef(request.VehicleId),
            new ServiceBayRef(bay.Id, bay.Label),
            new TechnicianRef(technician.Id, technician.Name),
            start,
            end,
            appointment.Status.ToString());
    }
}
