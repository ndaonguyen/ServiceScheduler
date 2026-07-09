using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using AppointmentScheduler.BuildingBlocks.Persistence;

namespace AppointmentScheduler.Api;

/// <summary>The modules composed into this host, and the EF configuration assemblies they contribute.</summary>
internal static class HostModules
{
    public static readonly Assembly[] Assemblies =
    [
        BookingModule.Assembly,
        FleetModule.Assembly,
        WorkforceModule.Assembly,
        CatalogModule.Assembly,
    ];

    public static ModuleConfigurations Configurations => new(Assemblies);
}
