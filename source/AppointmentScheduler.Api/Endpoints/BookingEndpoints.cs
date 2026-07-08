using AppointmentScheduler.Application.Features.Booking;
using AppointmentScheduler.Application.Messaging;

namespace AppointmentScheduler.Api.Endpoints;

/// <summary>
/// Booking endpoints under <c>/api/appointments</c>. Any authenticated caller may request an
/// appointment; the owner is resolved server-side from the access-cookie principal
/// (<c>ICurrentUser</c>) inside the handler, never from the request body.
/// </summary>
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments").WithTags("Booking");

        // The Application request record IS the wire contract (vehicleId, dealershipId,
        // serviceTypeId, requestedStart) — no owner field — so it binds directly from the body.
        group.MapPost("", async (RequestAppointment body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(body, ct);
            return Results.Created($"/api/appointments/{result.AppointmentId}", result);
        })
        .WithName("RequestAppointment")
        .RequireAuthorization();

        return app;
    }
}
