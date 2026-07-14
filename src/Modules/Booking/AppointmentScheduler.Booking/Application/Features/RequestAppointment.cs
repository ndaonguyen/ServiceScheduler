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
/// (returning a <see cref="BookingError"/> with the PRD §8 code on failure), resolves the service
/// duration, the dealership's bays, ownership, and the qualified technicians, then narrows to the
/// bays/technicians free for the requested window and books the first free pair. If a concurrent
/// booking wins the slot (the DB <c>EXCLUDE</c> constraint rejects the insert), it retries once with
/// the next free candidate in the losing dimension. The owner is taken from
/// <see cref="ICurrentUser"/>, never the request body.
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

        var validation = await ValidateAsync(request, ownerId, cancellationToken);
        if (validation.IsFailed)
            return Result.Fail<RequestAppointmentResponse>(validation.Errors);
        var context = validation.Value;

        var candidates = await SelectFreeCandidatesAsync(context, cancellationToken);
        if (candidates.IsFailed)
            return Result.Fail<RequestAppointmentResponse>(candidates.Errors);

        var booking = await TryBookAsync(request, ownerId, context, candidates.Value, cancellationToken);
        if (booking.IsFailed)
            return Result.Fail<RequestAppointmentResponse>(booking.Errors);

        return Result.Ok(BuildResponse(request, context, booking.Value));
    }

    /// <summary>
    /// Guard clauses in PRD §10 sequence order. Each maps a failure signal the query ports already
    /// return to a PRD §8 error; on failure nothing is persisted. On success, resolves the qualified
    /// technicians and the <c>[start, end)</c> window into a <see cref="BookingContext"/>.
    /// </summary>
    private async Task<Result<BookingContext>> ValidateAsync(
        RequestAppointment request, string ownerId, CancellationToken cancellationToken)
    {
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

        return new BookingContext(serviceType, dealership, technicians, start, end);
    }

    /// <summary>
    /// Narrows the dealership-scoped bays/technicians to those free for the requested window and
    /// returns them in preference order (technician first, then bay — PRD §8/§10). The whole ordered
    /// free list is kept so a race loss on insert can retry with the next candidate. Fails with the
    /// §8 shortage code when a dimension has no free resource (BR-01 / BR-02).
    /// </summary>
    private async Task<Result<FreeCandidates>> SelectFreeCandidatesAsync(
        BookingContext context, CancellationToken cancellationToken)
    {
        // Passing only the dealership's candidate ids keeps other dealerships' resources out of
        // consideration (BR-05/BR-06).
        var candidateBayIds = context.Dealership.Bays.Select(b => b.Id).ToList();
        var candidateTechnicianIds = context.QualifiedTechnicians.Select(t => t.Id).ToList();
        var busy = await appointments.GetBusyResourcesAsync(
            candidateBayIds, candidateTechnicianIds, context.Start, context.End, ct: cancellationToken);

        var freeTechnicians = context.QualifiedTechnicians.Where(t => !busy.BusyTechnicianIds.Contains(t.Id)).ToList();
        if (freeTechnicians.Count == 0) // BR-01 / AT-08
            return BookingErrors.NoQualifiedTechnician;

        var freeBays = context.Dealership.Bays.Where(b => !busy.BusyBayIds.Contains(b.Id)).ToList();
        if (freeBays.Count == 0) // BR-02 / AT-09
            return BookingErrors.NoBayAvailable;

        return new FreeCandidates(freeBays, freeTechnicians);
    }

    /// <summary>
    /// Initial attempt + one retry (PRD §10 "retry once with next candidate"). A concurrent booking
    /// can take the chosen bay/technician between the availability read and the insert; the DB
    /// <c>EXCLUDE</c> constraint rejects it and we advance to the next free candidate in the losing
    /// dimension. If the single retry is spent (or the losing dimension has no next candidate), fails
    /// with the §8 shortage code for the dimension that last lost the race.
    /// </summary>
    private async Task<Result<PlacedBooking>> TryBookAsync(
        RequestAppointment request, string ownerId, BookingContext context, FreeCandidates candidates,
        CancellationToken cancellationToken)
    {
        var technicianIndex = 0;
        var bayIndex = 0;
        var lastConflict = BookingResource.ServiceBay;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var technician = candidates.Technicians[technicianIndex];
            var bay = candidates.Bays[bayIndex];

            // BR-07: the window is [start, start + duration); the aggregate enforces its invariants.
            var appointment = Appointment.Schedule(
                ownerId,
                request.VehicleId,
                request.DealershipId,
                request.ServiceTypeId,
                bay.Id,
                technician.Id,
                context.Start,
                context.ServiceType.Duration,
                clock.GetUtcNow());

            try
            {
                await appointments.AddAsync(appointment, cancellationToken);
                return new PlacedBooking(appointment, bay, technician);
            }
            catch (AppointmentSlotConflictException conflict)
            {
                lastConflict = conflict.Resource;
                if (conflict.Resource == BookingResource.ServiceBay)
                {
                    if (++bayIndex >= candidates.Bays.Count)
                        return BookingErrors.NoBayAvailable;
                }
                else if (++technicianIndex >= candidates.Technicians.Count)
                {
                    return BookingErrors.NoQualifiedTechnician;
                }
            }
        }

        return lastConflict == BookingResource.ServiceBay
            ? BookingErrors.NoBayAvailable
            : BookingErrors.NoQualifiedTechnician;
    }

    private static RequestAppointmentResponse BuildResponse(
        RequestAppointment request, BookingContext context, PlacedBooking booking) =>
        new(
            booking.Appointment.Id,
            new DealershipRef(request.DealershipId, context.Dealership.DealershipName),
            new ServiceTypeRef(context.ServiceType.Id, context.ServiceType.Name, (int)context.ServiceType.Duration.TotalMinutes),
            new VehicleRef(request.VehicleId),
            new ServiceBayRef(booking.Bay.Id, booking.Bay.Label),
            new TechnicianRef(booking.Technician.Id, booking.Technician.Name),
            context.Start,
            context.End,
            booking.Appointment.Status.ToString());

    // Validated request references + resolved timing, produced by ValidateAsync.
    private sealed record BookingContext(
        ServiceTypeInfo ServiceType,
        DealershipBays Dealership,
        IReadOnlyList<TechnicianInfo> QualifiedTechnicians,
        DateTimeOffset Start,
        DateTimeOffset End);

    // Dealership resources free for the requested window, in preference order.
    private sealed record FreeCandidates(
        IReadOnlyList<BayInfo> Bays,
        IReadOnlyList<TechnicianInfo> Technicians);

    // The persisted appointment plus the bay/technician it landed on.
    private sealed record PlacedBooking(
        Appointment Appointment,
        BayInfo Bay,
        TechnicianInfo Technician);
}
