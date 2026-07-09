using System.Reflection;
using AppointmentScheduler.BuildingBlocks.SharedKernel;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace AppointmentScheduler.ArchitectureTests;

/// <summary>
/// Tactical-DDD rules. Value objects are modelled as records (structural equality) and must be
/// immutable; entities carry identity via <see cref="Entity{TId}"/> and the aggregate boundary is
/// marked by <see cref="IAggregateRoot"/>. These tests keep those guarantees from eroding.
/// </summary>
public class AggregateRulesTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(AppointmentScheduler.Booking.Domain.Appointment).Assembly,
        typeof(AppointmentScheduler.Fleet.Domain.Vehicle).Assembly,
        typeof(AppointmentScheduler.Workforce.Domain.Technician).Assembly,
        typeof(AppointmentScheduler.Catalog.Domain.ServiceType).Assembly,
    ];

    public static readonly TheoryData<string, Assembly> Modules = new()
    {
        { "Booking", ModuleAssemblies[0] },
        { "Fleet", ModuleAssemblies[1] },
        { "Workforce", ModuleAssemblies[2] },
        { "Catalog", ModuleAssemblies[3] },
    };

    // Domain types (entities and value objects) are leaf types — sealed, never a base for someone
    // else's inheritance. (Entity<TId> itself is abstract and lives in the SharedKernel, not here.)
    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_types_are_sealed(string name, Assembly module)
    {
        var result = Types.InAssembly(module)
            .That().AreClasses().And().AreNotAbstract().And().ResideInNamespaceContaining(".Domain")
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"{name} domain classes must be sealed; offenders: {Offenders(result.FailingTypeNames)}");
    }

    // Aggregate roots are fully encapsulated: state changes go through the constructor/factory and
    // behaviour methods, never a public setter.
    [Fact]
    public void Aggregate_roots_expose_no_public_setters()
    {
        var offenders =
            (from asm in ModuleAssemblies
             from type in asm.GetTypes()
             where typeof(IAggregateRoot).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false }
             from prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             where prop.GetSetMethod(nonPublic: false) is not null
             select $"{type.Name}.{prop.Name}").ToList();

        offenders.Should().BeEmpty(
            "aggregate roots must not expose public setters (mutate through factories / behaviour): "
            + string.Join(", ", offenders));
    }

    private static string Offenders(IEnumerable<string>? names) =>
        names is null ? "none" : string.Join(", ", names);
}
