using System.Reflection;

namespace AppointmentScheduler.BuildingBlocks.Persistence;

/// <summary>
/// The set of module assemblies whose <c>IEntityTypeConfiguration&lt;&gt;</c> implementations the
/// shared <see cref="AppDbContext"/> applies. Supplied by the composition root (and the design-time
/// factory) so Persistence never references a module project directly — keeping the dependency
/// direction BuildingBlocks &lt;- Modules &lt;- Host intact.
/// </summary>
public sealed class ModuleConfigurations(IEnumerable<Assembly> assemblies)
{
    public IReadOnlyList<Assembly> Assemblies { get; } = assemblies.ToArray();
}
