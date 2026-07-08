using FluentResults;

namespace AppointmentScheduler.Application.Features.Booking;

/// <summary>
/// A <see cref="FluentResults"/> error that carries the PRD §8 stable machine-readable
/// <see cref="Code"/> and the <see cref="HttpStatus"/> the API layer renders, alongside the
/// human-readable message. Keeping the status on the error means the endpoint needs no
/// code→status mapping table.
/// </summary>
public sealed class BookingError(string code, int httpStatus, string message) : Error(message)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
}

/// <summary>
/// Single source of truth for the five request-validation errors (PRD §8 / VR-02..VR-06). Each
/// property returns a fresh instance because FluentResults' <see cref="Error"/> is mutable
/// (Reasons/Metadata), so we never share a singleton downstream code could accidentally mutate.
/// </summary>
internal static class BookingErrors
{
    // HTTP status codes are plain ints so the Application layer stays free of any web-framework
    // reference; the Api layer renders them (PRD §8).
    public static BookingError ServiceTypeNotFound =>
        new("SERVICE_TYPE_NOT_FOUND", 404, "The specified service type does not exist.");

    public static BookingError RequestedStartInPast =>
        new("REQUESTED_START_IN_PAST", 400, "requestedStart must be in the future.");

    public static BookingError DealershipNotFound =>
        new("DEALERSHIP_NOT_FOUND", 404, "The specified dealership does not exist.");

    public static BookingError VehicleNotFound =>
        new("VEHICLE_NOT_FOUND", 404, "The specified vehicle does not exist.");

    public static BookingError VehicleNotOwned =>
        new("VEHICLE_NOT_OWNED_BY_CALLER", 403, "The specified vehicle is not owned by the caller.");

    // Availability failures (#5 / VR are not involved — these are 409 conflicts, PRD §8).
    public static BookingError NoQualifiedTechnician =>
        new("NO_QUALIFIED_TECHNICIAN", 409, "No qualified technician is available at the dealership for the requested time.");

    public static BookingError NoBayAvailable =>
        new("NO_BAY_AVAILABLE", 409, "No service bay is available at the dealership for the requested time.");
}
