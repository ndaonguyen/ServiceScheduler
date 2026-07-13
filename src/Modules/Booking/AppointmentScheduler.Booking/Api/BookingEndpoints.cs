using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using AppointmentScheduler.Booking.Application.Features;
using AppointmentScheduler.BuildingBlocks.Messaging;
using FluentResults;

namespace AppointmentScheduler.Booking.Api.Endpoints;

/// <summary>
/// Booking endpoints under <c>/api/appointments</c>. Any authenticated caller may request an
/// appointment; the owner is resolved server-side from the access-cookie principal
/// (<c>ICurrentUser</c>) inside the handler, never from the request body. Cancel and reschedule are
/// modelled as commands (a soft-cancel and an action sub-resource), not raw CRUD.
/// </summary>
public static class BookingEndpoints
{
    /// <summary>Body for <c>POST /api/appointments/{id}/reschedule</c> (the id comes from the route).</summary>
    public sealed record RescheduleAppointmentBody(DateTimeOffset NewStart);

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments").WithTags("Booking");

        // The Application request record IS the wire contract (vehicleId, dealershipId,
        // serviceTypeId, requestedStart) — no owner field — so it binds directly from the body.
        group.MapPost("", async (RequestAppointment body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(body, ct);
            return result.IsSuccess
                ? Results.Created($"/api/appointments/{result.Value.AppointmentId}", result.Value)
                : ToProblem(result);
        })
        .WithName("RequestAppointment")
        .RequireAuthorization();

        // D — DELETE reads as "cancel", but it is a soft state transition (status = Cancelled),
        // not a row removal. 204 on success.
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CancelAppointment(id), ct);
            return result.IsSuccess ? Results.NoContent() : ToProblem(result);
        })
        .WithName("CancelAppointment")
        .RequireAuthorization();

        // U — a POST *action* sub-resource, not PUT/PATCH: reschedule re-runs availability, may
        // reassign bay/technician, and can be rejected (409). It is not an idempotent field replace.
        group.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleAppointmentBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RescheduleAppointment(id, body.NewStart), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        })
        .WithName("RescheduleAppointment")
        .RequireAuthorization();

        return app;
    }

    // Every failure a Booking handler produces is a BookingError carrying the PRD §8 stable code and
    // HTTP status; render the { code, message } body from it (PRD §8 error contract).
    private static IResult ToProblem(ResultBase result)
    {
        var error = result.Errors.OfType<BookingError>().First();
        return Results.Json(new { code = error.Code, message = error.Message }, statusCode: error.HttpStatus);
    }
}
