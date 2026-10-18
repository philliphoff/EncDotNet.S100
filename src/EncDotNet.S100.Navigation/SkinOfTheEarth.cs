namespace EncDotNet.S100.Navigation;

/// <summary>What an S-101 skin-of-the-earth (group 1) area says about the ground under it.</summary>
public enum SkinOfTheEarthClass
{
    /// <summary>Not a skin-of-the-earth area this classifier knows.</summary>
    None,

    /// <summary>
    /// Water: a <c>DepthArea</c>, <c>DredgedArea</c> or <c>UnsurveyedArea</c>.
    /// A drying area is a <c>DepthArea</c> too, so it classifies as water;
    /// <see cref="SkinOfTheEarth.IsDrying"/> tells it apart.
    /// </summary>
    Water,

    /// <summary>Land: a <c>LandArea</c>.</summary>
    Land,
}

/// <summary>
/// Classifies S-101 skin-of-the-earth areas as water or land, and depth
/// areas as drying. Shared by the viewer's pick-time water/land decision and
/// the route check (#954).
/// </summary>
/// <remarks>
/// S-101 tiles every cell with group-1 (skin of the earth) areas: land is a
/// <c>LandArea</c>, and water is a <c>DepthArea</c>, <c>DredgedArea</c> or
/// <c>UnsurveyedArea</c>. Ground that covers and uncovers is a
/// <c>DepthArea</c> whose minimum depth (<c>depthRangeMinimumValue</c>,
/// DRVAL1) is negative, a drying height.
/// </remarks>
public static class SkinOfTheEarth
{
    private static readonly string[] MaximumDepthAttributeCodes = ["depthRangeMaximumValue", "DRVAL2"];

    /// <summary>The feature type of an S-101 land area.</summary>
    public const string LandArea = "LandArea";

    /// <summary>The feature type of an S-101 depth area.</summary>
    public const string DepthArea = "DepthArea";

    /// <summary>The feature type of an S-101 coastline.</summary>
    public const string Coastline = "Coastline";

    /// <summary>The feature types of S-101 group-1 areas that denote water.</summary>
    public static IReadOnlySet<string> WaterAreaTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DepthArea,
        "DredgedArea",
        "UnsurveyedArea",
    };

    /// <summary>The feature types of S-101 group-1 areas that denote land.</summary>
    public static IReadOnlySet<string> LandAreaTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        LandArea,
    };

    /// <summary>Classifies a feature type.</summary>
    /// <param name="featureType">The S-101 feature type, such as <c>"DepthArea"</c>.</param>
    /// <returns>The class, or <see cref="SkinOfTheEarthClass.None"/>.</returns>
    public static SkinOfTheEarthClass Classify(string featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        return WaterAreaTypes.Contains(featureType) ? SkinOfTheEarthClass.Water
            : LandAreaTypes.Contains(featureType) ? SkinOfTheEarthClass.Land
            : SkinOfTheEarthClass.None;
    }

    /// <summary>
    /// Whether a <c>DepthArea</c> dries: its minimum depth (DRVAL1) is below
    /// the vertical datum, or, without a minimum, its maximum (DRVAL2) is at
    /// or above it.
    /// </summary>
    /// <param name="featureType">The feature type; anything but a <c>DepthArea</c> never dries.</param>
    /// <param name="attributes">The feature's attributes, keyed by code (S-101 camel case or the S-57 acronym).</param>
    /// <returns><c>true</c> for drying ground.</returns>
    public static bool IsDrying(string featureType, IReadOnlyDictionary<string, object?> attributes)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        ArgumentNullException.ThrowIfNull(attributes);
        if (!string.Equals(featureType, DepthArea, StringComparison.Ordinal))
        {
            return false;
        }

        if (Read(attributes, ChartedDepthResolver.MinimumDepthAttributeCodes) is { } minimum)
        {
            return minimum < 0;
        }

        return Read(attributes, MaximumDepthAttributeCodes) is { } maximum && maximum <= 0;
    }

    private static double? Read(IReadOnlyDictionary<string, object?> attributes, IReadOnlyList<string> codes)
    {
        foreach (var code in codes)
        {
            if (attributes.TryGetValue(code, out var raw) && ChartedDepthResolver.TryReadMeters(raw, out var metres))
            {
                return metres;
            }
        }

        return null;
    }
}
