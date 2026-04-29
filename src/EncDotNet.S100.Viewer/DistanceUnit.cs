namespace EncDotNet.S100.Viewer;

/// <summary>Distance unit for the map scale bar.</summary>
internal enum DistanceUnit
{
    Meters,
    Kilometers,
    Miles,
    NauticalMiles,
}

internal static class DistanceUnitExtensions
{
    /// <summary>Number of meters in one of the unit.</summary>
    public static double MetersPerUnit(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Meters => 1.0,
        DistanceUnit.Kilometers => 1000.0,
        DistanceUnit.Miles => 1609.344,
        DistanceUnit.NauticalMiles => 1852.0,
        _ => 1852.0,
    };

    /// <summary>Lower-case abbreviation suitable for display on the scale bar.</summary>
    public static string Abbreviation(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Meters => "m",
        DistanceUnit.Kilometers => "km",
        DistanceUnit.Miles => "mi",
        DistanceUnit.NauticalMiles => "nm",
        _ => "nm",
    };

    /// <summary>Human-readable display name for use in settings UIs.</summary>
    public static string DisplayName(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Meters => "Meters (m)",
        DistanceUnit.Kilometers => "Kilometers (km)",
        DistanceUnit.Miles => "Miles (mi)",
        DistanceUnit.NauticalMiles => "Nautical miles (nm)",
        _ => unit.ToString(),
    };
}
