using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>
/// One point of an advisory's forecast: where the storm is expected to be at
/// <see cref="ValidTime"/>, and how strong.
/// </summary>
/// <remarks>
/// The point at <see cref="Tau"/> 0 is the storm's position and intensity at
/// the advisory's synoptic time, a few hours before it was issued. Values NHC
/// leaves out (it writes 9999, for example the pressure after tau 0) are
/// <see langword="null"/>.
/// </remarks>
public sealed record TropicalCycloneForecastPoint
{
    /// <summary>Hours after the advisory's synoptic time: 0, 12, 24, 36, 48, 60, 72, 96, 120.</summary>
    public required int Tau { get; init; }

    /// <summary>The time the point is valid, in UTC.</summary>
    public required DateTime ValidTime { get; init; }

    /// <summary>The storm's centre, in WGS-84 degrees.</summary>
    public required GeoPosition Position { get; init; }

    /// <summary>The maximum sustained (1-minute) wind, in knots.</summary>
    public int? MaxWindKnots { get; init; }

    /// <summary>The peak gust, in knots.</summary>
    public int? GustKnots { get; init; }

    /// <summary>The minimum central pressure, in hectopascals; NHC gives it at tau 0 only.</summary>
    public int? PressureHectopascals { get; init; }

    /// <summary>The storm's class at this point.</summary>
    public required TropicalCycloneClass Classification { get; init; }

    /// <summary>The storm type code NHC wrote, for example <c>"MH"</c>.</summary>
    public required string ClassCode { get; init; }

    /// <summary>
    /// The one-letter class NHC labels the point with on its graphics
    /// (<c>D</c>, <c>S</c>, <c>H</c>, <c>M</c>, …), or empty.
    /// </summary>
    public string ClassLetter { get; init; } = string.Empty;

    /// <summary>NHC's words for the class at this point, for example <c>"Major Hurricane"</c>, or empty.</summary>
    public string ClassDescription { get; init; } = string.Empty;

    /// <summary>The Saffir-Simpson category, 1 to 5, or <see langword="null"/> below hurricane strength.</summary>
    public int? SaffirSimpsonCategory { get; init; }

    /// <summary>The direction the storm is moving towards, in degrees true; NHC gives it at tau 0 only.</summary>
    public double? MotionDirectionDegrees { get; init; }

    /// <summary>The storm's forward speed, in knots; NHC gives it at tau 0 only.</summary>
    public double? MotionSpeedKnots { get; init; }

    /// <summary>The forecast 34, 50 and 64 kt wind radii at this point, smallest threshold first; empty when there are none.</summary>
    public IReadOnlyList<TropicalCycloneWindRadii> Radii { get; init; } = [];
}

/// <summary>One point of a storm's best track: where it was and how strong.</summary>
/// <remarks>
/// NHC's best track is the operational track up to the latest advisory, and
/// after the season the reanalysed one. Points are usually at the synoptic
/// hours (00, 06, 12 and 18 UTC), plus landfalls and peaks.
/// </remarks>
public sealed record TropicalCycloneTrackPoint
{
    /// <summary>The time of the point, in UTC.</summary>
    public required DateTime Time { get; init; }

    /// <summary>The storm's centre, in WGS-84 degrees.</summary>
    public required GeoPosition Position { get; init; }

    /// <summary>The maximum sustained (1-minute) wind, in knots.</summary>
    public int? MaxWindKnots { get; init; }

    /// <summary>The minimum central pressure, in hectopascals.</summary>
    public int? PressureHectopascals { get; init; }

    /// <summary>The storm's class at this point.</summary>
    public required TropicalCycloneClass Classification { get; init; }

    /// <summary>The storm type code NHC wrote, for example <c>"HU"</c>.</summary>
    public required string ClassCode { get; init; }

    /// <summary>The Saffir-Simpson category, 1 to 5, or <see langword="null"/> below hurricane strength.</summary>
    public int? SaffirSimpsonCategory { get; init; }

    /// <summary>The storm's name at this point, as the best track writes it (<c>"INVEST"</c>, <c>"NINE"</c>, <c>"IAN"</c>), or empty.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The observed 34, 50 and 64 kt wind radii at this point, smallest threshold first; empty when there are none.</summary>
    public IReadOnlyList<TropicalCycloneWindRadii> Radii { get; init; } = [];
}
