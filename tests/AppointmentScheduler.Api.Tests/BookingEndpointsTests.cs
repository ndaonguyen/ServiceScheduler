using System.Net;
using System.Net.Http.Json;
using AppointmentScheduler.Domain.Catalog;
using AppointmentScheduler.Domain.Fleet;
using AppointmentScheduler.Domain.Workforce;
using AppointmentScheduler.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointmentScheduler.Api.Tests;

public class BookingEndpointsTests
{
    private const string OwnerId = "test-user";

    [Fact]
    public async Task RequestAppointment_with_available_bay_and_qualified_technician_returns_201()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClientAs(OwnerId, "user");

        var (dealershipId, serviceTypeId, vehicleId) = await SeedReferenceDataAsync(factory);

        var requestedStart = DateTimeOffset.Parse("2026-08-01T09:00:00Z");
        var body = new
        {
            vehicleId,
            dealershipId,
            serviceTypeId,
            requestedStart,
        };

        var response = await client.PostAsJsonAsync("/api/appointments", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var result = await response.Content.ReadFromJsonAsync<RequestAppointmentResponse>();
        result!.Vehicle.Id.Should().Be(vehicleId);
        result.Dealership.Id.Should().Be(dealershipId);
        result.ServiceType.Id.Should().Be(serviceTypeId);
        result.ScheduledStart.Should().Be(requestedStart);
        result.ScheduledEnd.Should().Be(requestedStart + TimeSpan.FromMinutes(result.ServiceType.DurationMinutes));
        result.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task RequestAppointment_without_authentication_returns_401()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var body = new
        {
            vehicleId = Guid.NewGuid(),
            dealershipId = Guid.NewGuid(),
            serviceTypeId = Guid.NewGuid(),
            requestedStart = DateTimeOffset.UtcNow,
        };

        var response = await client.PostAsJsonAsync("/api/appointments", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<(Guid DealershipId, Guid ServiceTypeId, Guid VehicleId)> SeedReferenceDataAsync(
        TestWebAppFactory factory)
    {
        var dealershipId = Guid.NewGuid();
        var serviceTypeId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var bayId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Set<ServiceType>().Add(new ServiceType { Id = serviceTypeId, Name = "Oil change", Duration = TimeSpan.FromMinutes(45) });
        db.Set<Dealership>().Add(new Dealership { Id = dealershipId, Name = "Springfield Downtown", Address = "123 Main St" });
        db.Set<ServiceBay>().Add(new ServiceBay { Id = bayId, DealershipId = dealershipId, Label = "Bay 1" });
        db.Set<Technician>().Add(new Technician { Id = technicianId, DealershipId = dealershipId, Name = "Alex Chen" });
        db.Set<TechnicianQualification>().Add(new TechnicianQualification { TechnicianId = technicianId, ServiceTypeId = serviceTypeId });
        db.Set<Vehicle>().Add(new Vehicle
        {
            Id = vehicleId,
            OwnerId = OwnerId,
            Make = "Toyota",
            Model = "Corolla",
            Year = 2020,
            Vin = "JTDBR32E020000001",
        });

        await db.SaveChangesAsync();

        return (dealershipId, serviceTypeId, vehicleId);
    }

    private sealed record RequestAppointmentResponse(
        Guid AppointmentId,
        DealershipRef Dealership,
        ServiceTypeRef ServiceType,
        VehicleRef Vehicle,
        ServiceBayRef ServiceBay,
        TechnicianRef Technician,
        DateTimeOffset ScheduledStart,
        DateTimeOffset ScheduledEnd,
        string Status);

    private sealed record DealershipRef(Guid Id, string Name);
    private sealed record ServiceTypeRef(Guid Id, string Name, int DurationMinutes);
    private sealed record VehicleRef(Guid Id);
    private sealed record ServiceBayRef(Guid Id, string Label);
    private sealed record TechnicianRef(Guid Id, string Name);
}
