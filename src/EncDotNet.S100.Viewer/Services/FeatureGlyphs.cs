using System;
using Icon = FluentIcons.Common.Icon;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Maps an S-100 feature-type code (e.g. <c>"BeaconLateral"</c>,
/// <c>"DepthArea"</c>) to a representative <see cref="FluentIcons.Common.Icon"/>
/// glyph for the Pick Report hit list and identity block. The mapping is
/// deliberately coarse — keyed off substrings of the class code so it works
/// across products without a per-feature lookup table — and falls back to a
/// generic shape glyph for unrecognised classes. It is a scanning aid, not a
/// portrayal-accurate symbol.
/// </summary>
internal static class FeatureGlyphs
{
    /// <summary>Generic glyph used when no category keyword matches.</summary>
    public const Icon Fallback = Icon.Shapes;

    /// <summary>
    /// Returns the glyph for the supplied feature-type code. Matching is
    /// case-insensitive and substring-based; the first matching category in
    /// priority order wins. Returns <see cref="Fallback"/> for null, empty,
    /// or unrecognised codes.
    /// </summary>
    /// <param name="featureType">The feature class/type code.</param>
    public static Icon ForFeatureType(string? featureType)
    {
        if (string.IsNullOrWhiteSpace(featureType))
            return Fallback;

        var t = featureType.ToLowerInvariant();

        // Order matters: check more specific keywords before broader ones
        // (e.g. "lighthouse" before "light", "landmark" before "land").
        if (Has(t, "lighthouse")) return Icon.BuildingLighthouse;
        if (Has(t, "light")) return Icon.WeatherSunny;
        if (Has(t, "beacon")) return Icon.Flag;
        if (Has(t, "buoy")) return Icon.MyLocation;
        if (Has(t, "depth", "sounding", "dredge")) return Icon.Water;
        if (Has(t, "seaarea", "sea area", "water", "lake", "river", "canal")) return Icon.Water;
        if (Has(t, "fairway", "route", "channel", "recommendedtrack", "navigationline")) return Icon.Channel;
        if (Has(t, "coastline", "shoreline", "landarea", "landregion", "land")) return Icon.Map;
        if (Has(t, "rock", "wreck", "obstruction", "obstacle", "hazard")) return Icon.Diamond;
        if (Has(t, "vessel", "ship", "ais")) return Icon.VehicleShip;
        if (Has(t, "area", "zone", "restricted", "anchorage", "region")) return Icon.LayerDiagonal;
        if (Has(t, "navigation", "navline", "navaid")) return Icon.Navigation;

        return Fallback;
    }

    private static bool Has(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
