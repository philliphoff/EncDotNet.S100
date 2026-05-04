using System.Collections.Generic;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>Distance unit system shown by the map scale bar.</summary>
internal enum DistanceUnit
{
    /// <summary>Metric system: kilometers down to ~100 m, then meters.</summary>
    Metric,

    /// <summary>Statute miles down to ~1000 ft, then feet.</summary>
    Miles,

    /// <summary>Nautical miles only.</summary>
    NauticalMiles,
}

/// <summary>One concrete unit (e.g. km or m) within a <see cref="DistanceUnit"/> system.</summary>
internal readonly record struct ScaleBarUnit(double MetersPerUnit, string Abbreviation, double SwitchBelowMeters);

internal static class DistanceUnitExtensions
{
    /// <summary>Human-readable display name for use in settings UIs.</summary>
    public static string DisplayName(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Metric => Strings.DistanceUnit_Metric,
        DistanceUnit.Miles => Strings.DistanceUnit_Miles,
        DistanceUnit.NauticalMiles => Strings.DistanceUnit_NauticalMiles,
        _ => unit.ToString(),
    };

    /// <summary>
    /// Concrete units that the scale bar may use for the given system, ordered
    /// from largest to smallest. Each entry's <c>SwitchBelowMeters</c> threshold
    /// causes the bar to drop to the next entry once the total bar length falls
    /// at or below it. The final entry's threshold is ignored.
    /// </summary>
    public static IReadOnlyList<ScaleBarUnit> GetUnits(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Metric =>
        [
            new(MetersPerUnit: 1000.0,            Abbreviation: "km", SwitchBelowMeters: 100.0),
            new(MetersPerUnit: 1.0,               Abbreviation: "m",  SwitchBelowMeters: 0.0),
        ],
        DistanceUnit.Miles =>
        [
            // 1000 ft ≈ 304.8 m.
            new(MetersPerUnit: 1609.344,          Abbreviation: "mi", SwitchBelowMeters: 304.8),
            new(MetersPerUnit: 0.3048,            Abbreviation: "ft", SwitchBelowMeters: 0.0),
        ],
        DistanceUnit.NauticalMiles =>
        [
            new(MetersPerUnit: 1852.0,            Abbreviation: "nm", SwitchBelowMeters: 0.0),
        ],
        _ =>
        [
            new(MetersPerUnit: 1852.0,            Abbreviation: "nm", SwitchBelowMeters: 0.0),
        ],
    };
}
