namespace EncDotNet.S100.Navigation;

/// <summary>
/// The kinds of prohibited area and area with special conditions in the
/// S-101 alert catalogue (#1127): the viewing groups of its <c>ProhAre</c>
/// highlight, which each area's <c>AlertReference</c> names, and the names
/// MCP and <c>s100 route check</c> use for them.
/// </summary>
public static class RouteAreaKinds
{
    /// <summary>Traffic separation zone.</summary>
    public const int TrafficSeparationZone = 53012;

    /// <summary>Inshore traffic zone.</summary>
    public const int InshoreTrafficZone = 53013;

    /// <summary>Restricted area.</summary>
    public const int RestrictedArea = 53014;

    /// <summary>Area to be avoided.</summary>
    public const int AreaToBeAvoided = 53015;

    /// <summary>Particularly sensitive sea area (PSSA).</summary>
    public const int ParticularlySensitiveSeaArea = 53016;

    /// <summary>Caution area.</summary>
    public const int CautionArea = 53017;

    /// <summary>Offshore production area.</summary>
    public const int OffshoreProductionArea = 53018;

    /// <summary>User-defined area to be avoided.</summary>
    public const int UserAreaToBeAvoided = 53019;

    /// <summary>Military practice area.</summary>
    public const int MilitaryPracticeArea = 53020;

    /// <summary>Seaplane landing area.</summary>
    public const int SeaplaneLandingArea = 53021;

    /// <summary>Submarine transit lane.</summary>
    public const int SubmarineTransitLane = 53022;

    /// <summary>Anchorage area.</summary>
    public const int AnchorageArea = 53023;

    /// <summary>Marine farm or aquaculture.</summary>
    public const int MarineFarm = 53024;

    /// <summary>Every kind with its name, in viewing-group order.</summary>
    public static IReadOnlyDictionary<int, string> Names { get; } = new SortedDictionary<int, string>
    {
        [TrafficSeparationZone] = "traffic_separation_zone",
        [InshoreTrafficZone] = "inshore_traffic_zone",
        [RestrictedArea] = "restricted_area",
        [AreaToBeAvoided] = "area_to_be_avoided",
        [ParticularlySensitiveSeaArea] = "particularly_sensitive_sea_area",
        [CautionArea] = "caution_area",
        [OffshoreProductionArea] = "offshore_production_area",
        [UserAreaToBeAvoided] = "user_area_to_be_avoided",
        [MilitaryPracticeArea] = "military_practice_area",
        [SeaplaneLandingArea] = "seaplane_landing_area",
        [SubmarineTransitLane] = "submarine_transit_lane",
        [AnchorageArea] = "anchorage_area",
        [MarineFarm] = "marine_farm",
    };

    /// <summary>The kind with a name, or <c>null</c>.</summary>
    /// <param name="name">A name from <see cref="Names"/>, such as <c>anchorage_area</c>.</param>
    /// <returns>The kind, or <c>null</c> when the name isn't one.</returns>
    public static int? Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var (kind, kindName) in Names)
        {
            if (string.Equals(kindName, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// The kind an S-101 area's portrayal gives it, as the S-101 rules decide
    /// it, for a source that can't run them; <c>null</c> for a type that
    /// raises no <c>ProhAre</c> alert.
    /// </summary>
    /// <param name="featureType">The feature type.</param>
    /// <param name="restrictions">Its <c>restriction</c> codes.</param>
    /// <param name="categories">Its category codes; for a restricted area, <c>categoryOfRestrictedArea</c>.</param>
    /// <returns>The kind, or <c>null</c>.</returns>
    public static int? Of(string featureType, IReadOnlyCollection<int> restrictions, IReadOnlyCollection<int> categories) => featureType switch
    {
        "SeparationZoneOrLine" or "TrafficSeparationZone" => TrafficSeparationZone,
        "InshoreTrafficZone" => InshoreTrafficZone,
        "RestrictedArea" or "RestrictedAreaNavigational" or "RestrictedAreaRegulatory" =>
            restrictions.Contains(14) ? AreaToBeAvoided : categories.Contains(28) ? ParticularlySensitiveSeaArea : RestrictedArea,
        "CautionArea" or "DiscolouredWater" => CautionArea,
        "OffshoreProductionArea" => OffshoreProductionArea,
        "MilitaryPracticeArea" => MilitaryPracticeArea,
        "SeaplaneLandingArea" => SeaplaneLandingArea,
        "SubmarineTransitLane" => SubmarineTransitLane,
        "AnchorageArea" or "MooringArea" => AnchorageArea,
        "MarineFarmCulture" => MarineFarm,
        _ => null,
    };
}
