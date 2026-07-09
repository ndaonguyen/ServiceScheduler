using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace AppointmentScheduler.ArchitectureTests;

/// <summary>
/// Compiler-enforced module boundaries are backed up by these rules so a stray cross-module type
/// reference fails the build. A module may depend on another module's <c>Contracts</c> (its ports
/// live in the shared <c>AppointmentScheduler.Application.Abstractions</c> namespace) but never on
/// another module's Domain or Infrastructure.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly System.Reflection.Assembly Booking = typeof(AppointmentScheduler.Booking.Domain.Appointment).Assembly;
    private static readonly System.Reflection.Assembly Fleet = typeof(AppointmentScheduler.Fleet.Domain.Vehicle).Assembly;
    private static readonly System.Reflection.Assembly Workforce = typeof(AppointmentScheduler.Workforce.Domain.Technician).Assembly;
    private static readonly System.Reflection.Assembly Catalog = typeof(AppointmentScheduler.Catalog.Domain.ServiceType).Assembly;

    public static readonly TheoryData<string, System.Reflection.Assembly> Modules = new()
    {
        { "Booking", Booking },
        { "Fleet", Fleet },
        { "Workforce", Workforce },
        { "Catalog", Catalog },
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_has_no_persistence_or_web_dependencies(string name, System.Reflection.Assembly module)
    {
        _ = name;
        var result = Types.InAssembly(module)
            .That().ResideInNamespaceStartingWith("AppointmentScheduler.Domain")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"{name} Domain must stay free of persistence/web frameworks; offenders: {Offenders(result)}");
    }

    [Fact]
    public void Booking_does_not_depend_on_other_modules_internals()
    {
        var result = Types.InAssembly(Booking)
            .ShouldNot().HaveDependencyOnAny(
                "AppointmentScheduler.Fleet.Domain",
                "AppointmentScheduler.Workforce.Domain",
                "AppointmentScheduler.Catalog.Domain",
                "AppointmentScheduler.Fleet.Infrastructure",
                "AppointmentScheduler.Workforce.Infrastructure",
                "AppointmentScheduler.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Booking may use other modules' Contracts, never their Domain/Infrastructure; offenders: {Offenders(result)}");
    }

    private static string Offenders(TestResult result) =>
        result.FailingTypeNames is null ? "none" : string.Join(", ", result.FailingTypeNames);
}
