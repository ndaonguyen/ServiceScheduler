using System.Reflection;
using AppointmentScheduler.Booking.Application.Abstractions;
using AppointmentScheduler.BuildingBlocks.Messaging;
using AppointmentScheduler.Booking.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Booking module composition: its request handlers and its own persistence port.</summary>
public static class BookingModule
{
    public static readonly Assembly Assembly = typeof(BookingModule).Assembly;

    public static IServiceCollection AddBookingModule(this IServiceCollection services)
    {
        services.AddRequestHandlers(Assembly);
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        return services;
    }
}
