using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>Terse builders for <see cref="PassageLeg"/> and <see cref="RouteCheckSettings"/> in metres and knots.</summary>
internal static class PassageLegs
{
    /// <summary>A leg from plain numbers: metres, knots and an optional half-width on both sides.</summary>
    public static PassageLeg Leg(
        IReadOnlyList<GeoPosition> points,
        double? draft = null,
        double? speedKnots = null,
        double squat = 0,
        double? ukc = null,
        double? halfWidth = null,
        LegGeometry geometry = LegGeometry.Rhumb) => new()
        {
            Points = points,
            Geometry = geometry,
            PortLimit = halfWidth is { } port ? Length.FromMetres(port) : null,
            StarboardLimit = halfWidth is { } starboard ? Length.FromMetres(starboard) : null,
            Speed = speedKnots is { } knots ? Speed.FromKnots(knots) : null,
            Draft = draft is { } d ? Depth.FromMetres(d) : null,
            Squat = Depth.FromMetres(squat),
            RequiredUkc = ukc is { } u ? Depth.FromMetres(u) : null,
        };

    /// <summary>A copy of <paramref name="leg"/> with another speed.</summary>
    public static PassageLeg WithSpeed(this PassageLeg leg, double? speedKnots) =>
        Copy(leg, speedKnots is { } knots ? Speed.FromKnots(knots) : null, leg.PortLimit, leg.StarboardLimit);

    /// <summary>A copy of <paramref name="leg"/> with the same half-width on both sides.</summary>
    public static PassageLeg WithHalfWidth(this PassageLeg leg, double halfWidth) =>
        Copy(leg, leg.Speed, Length.FromMetres(halfWidth), Length.FromMetres(halfWidth));

    /// <summary>Settings that judge at <paramref name="at"/>, with optional safety contour and depth in metres.</summary>
    public static RouteCheckSettings Settings(DateTimeOffset at, double? contour = null, double? safetyDepth = null) => new()
    {
        At = at,
        SafetyContour = contour is { } c ? Depth.FromMetres(c) : null,
        SafetyDepth = safetyDepth is { } s ? Depth.FromMetres(s) : null,
    };

    private static PassageLeg Copy(PassageLeg leg, Speed? speed, Length? port, Length? starboard) => new()
    {
        Points = leg.Points,
        Geometry = leg.Geometry,
        PortLimit = port,
        StarboardLimit = starboard,
        Speed = speed,
        Draft = leg.Draft,
        Squat = leg.Squat,
        RequiredUkc = leg.RequiredUkc,
        Name = leg.Name,
    };
}
