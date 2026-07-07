using AppointmentScheduler.Application.Features.Widgets;
using AppointmentScheduler.Application.Messaging;

namespace AppointmentScheduler.Api.Endpoints;

/// <summary>
/// Thin HTTP layer over the Widgets slices. Endpoints only translate HTTP &lt;-&gt; requests;
/// all behavior lives in the Application handlers.
/// </summary>
public static class WidgetEndpoints
{
    public static IEndpointRouteBuilder MapWidgetEndpoints(this IEndpointRouteBuilder app)
    {
        // Every widget endpoint requires an authenticated caller.
        var group = app.MapGroup("/api/widgets").WithTags("Widgets").RequireAuthorization();

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWidgets.Query(), ct);
            return Results.Ok(result);
        })
        .WithName("GetWidgets");

        group.MapPost("", async (CreateWidgetRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateWidget.Command(body.Name), ct);
            return Results.CreatedAtRoute("GetWidget", new { id = result.Id }, result);
        })
        .WithName("CreateWidget")
        // Creating a widget additionally requires the "admin" role.
        .RequireAuthorization(policy => policy.RequireRole("admin"));

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWidget.Query(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetWidget");

        return app;
    }

    public sealed record CreateWidgetRequest(string Name);
}
