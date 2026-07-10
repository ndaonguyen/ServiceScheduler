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

    // Every aggregate root carries identity: root-ness (IAggregateRoot) is an id-agnostic marker,
    // but a root is always an entity, so it must derive from Entity<TId>. This is the mechanical
    // backstop for the "every root is an entity" invariant that is otherwise only a convention.
    [Fact]
    public void Aggregate_roots_are_entities()
    {
        var offenders =
            (from asm in ModuleAssemblies
             from type in asm.GetTypes()
             where typeof(IAggregateRoot).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false }
             where !InheritsEntity(type)
             select type.Name).ToList();

        offenders.Should().BeEmpty(
            "every aggregate root must carry identity via Entity<TId>: " + string.Join(", ", offenders));
    }

    // Value objects (IValueObject) are structural-equality records with no identity of their own —
    // they must never be modelled as entities.
    [Fact]
    public void Value_objects_are_not_entities()
    {
        var offenders =
            (from asm in ModuleAssemblies
             from type in asm.GetTypes()
             where typeof(IValueObject).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false }
             where InheritsEntity(type)
             select type.Name).ToList();

        offenders.Should().BeEmpty(
            "value objects must not be entities (model them as records, not Entity<TId>): "
            + string.Join(", ", offenders));
    }

    // Walks the base chain looking for the open generic Entity<> — handles any TId.
    private static bool InheritsEntity(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    private static string Offenders(IEnumerable<string>? names) =>
        names is null ? "none" : string.Join(", ", names);
}
