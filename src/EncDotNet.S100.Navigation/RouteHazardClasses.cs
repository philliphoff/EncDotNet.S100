namespace EncDotNet.S100.Navigation;

/// <summary>
/// The hazard class of each S-101 feature type whose portrayal can raise the
/// <c>NavHazard</c> alert (#1127), from the route-findings design
/// (<c>docs/design/route-findings-volume.md</c> §4.1).
/// </summary>
public static class RouteHazardClasses
{
    private static readonly Dictionary<string, RouteHazardClass> ByType = new(StringComparer.Ordinal)
    {
        ["Wreck"] = RouteHazardClass.Danger,
        ["Obstruction"] = RouteHazardClass.Danger,
        ["UnderwaterAwashRock"] = RouteHazardClass.Danger,
        ["Sounding"] = RouteHazardClass.Danger,

        ["LateralBuoy"] = RouteHazardClass.AidToNavigation,
        ["CardinalBuoy"] = RouteHazardClass.AidToNavigation,
        ["IsolatedDangerBuoy"] = RouteHazardClass.AidToNavigation,
        ["SafeWaterBuoy"] = RouteHazardClass.AidToNavigation,
        ["SpecialPurposeGeneralBuoy"] = RouteHazardClass.AidToNavigation,
        ["InstallationBuoy"] = RouteHazardClass.AidToNavigation,
        ["MooringBuoy"] = RouteHazardClass.AidToNavigation,
        ["EmergencyWreckMarkingBuoy"] = RouteHazardClass.AidToNavigation,
        ["LateralBeacon"] = RouteHazardClass.AidToNavigation,
        ["CardinalBeacon"] = RouteHazardClass.AidToNavigation,
        ["IsolatedDangerBeacon"] = RouteHazardClass.AidToNavigation,
        ["SafeWaterBeacon"] = RouteHazardClass.AidToNavigation,
        ["SpecialPurposeGeneralBeacon"] = RouteHazardClass.AidToNavigation,
        ["Daymark"] = RouteHazardClass.AidToNavigation,
        ["LightFloat"] = RouteHazardClass.AidToNavigation,
        ["LightVessel"] = RouteHazardClass.AidToNavigation,
        ["PhysicalAISAidToNavigation"] = RouteHazardClass.AidToNavigation,
        ["VirtualAISAidToNavigation"] = RouteHazardClass.AidToNavigation,

        ["Bridge"] = RouteHazardClass.Overhead,
        ["SpanFixed"] = RouteHazardClass.Overhead,
        ["SpanOpening"] = RouteHazardClass.Overhead,
        ["CableOverhead"] = RouteHazardClass.Overhead,
        ["PipelineOverhead"] = RouteHazardClass.Overhead,
        ["Conveyor"] = RouteHazardClass.Overhead,
    };

    /// <summary>The hazard class of a feature type; <see cref="RouteHazardClass.Structure"/> for any type not listed.</summary>
    /// <param name="featureType">The S-101 feature type, such as <c>LateralBuoy</c>.</param>
    /// <returns>The class.</returns>
    public static RouteHazardClass Of(string featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        return ByType.TryGetValue(featureType, out var hazardClass) ? hazardClass : RouteHazardClass.Structure;
    }
}
