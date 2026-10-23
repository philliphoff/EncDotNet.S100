using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>A charted-depth source defined by a function of position.</summary>
internal sealed class FakeDepth(Func<double, double, ChartedDepth?> depthAt, double? gridSpacingMeters = null) : IChartedDepthSource
{
    public int Calls { get; private set; }

    public double? FinestGridSpacingMeters(BoundingBox extent) => gridSpacingMeters;

    public ChartedDepth? Sample(GeoPosition position)
    {
        Calls++;
        return depthAt(position.Latitude, position.Longitude);
    }

    public ChartedDepth? SampleBathymetry(GeoPosition position) =>
        Sample(position) is { Kind: ChartedDepthKind.Bathymetry } depth ? depth : null;

    public static ChartedDepth Bathymetry(double depth, int? datum = Fixture.Lat) =>
        new(depth, ChartedDepthKind.Bathymetry, 0.3, datum, "102TEST", "0,0");

    public static ChartedDepth DepthArea(double depth, int? datum = Fixture.Lat) =>
        new(depth, ChartedDepthKind.DepthArea, null, datum, "101TEST", "42");
}

/// <summary>A water-level series defined by a function of time.</summary>
internal sealed class FakeSeries(DateTime start, DateTime end, Func<DateTime, double?> heightAt, int? datum = Fixture.Lat, string id = "104TEST") : IWaterLevelSeries
{
    public WaterLevelSourceInfo Source { get; } = new(id, WaterLevelSourceKind.Station, datum, "STN1", 100);

    public DateTime Start { get; } = start;

    public DateTime End { get; } = end;

    public WaterLevelReading? ValueAt(DateTime utc) =>
        heightAt(utc) is { } h ? new WaterLevelReading(h, utc) : null;
}

/// <summary>A water-level source defined by a function of position.</summary>
internal sealed class FakeWater(Func<double, double, IReadOnlyList<IWaterLevelSeries>> locate) : IWaterLevelSource
{
    public int Calls { get; private set; }

    public IReadOnlyList<IWaterLevelSeries> Locate(GeoPosition position)
    {
        Calls++;
        return locate(position.Latitude, position.Longitude);
    }

    /// <summary>One series everywhere, constant height, covering the whole fixture day.</summary>
    public static FakeWater Constant(double height, int? datum = Fixture.Lat) =>
        new((_, _) => [new FakeSeries(Fixture.Departure.UtcDateTime.AddHours(-12), Fixture.Departure.UtcDateTime.AddHours(12), _ => height, datum)]);
}

/// <summary>
/// The synthetic passage: one rhumb leg due east along the equator from
/// 0° to 0,1° E (about 6 NM), run at 6 kn, so it takes about an hour.
/// </summary>
internal static class Fixture
{
    /// <summary>S-100 vertical datum code for Lowest Astronomical Tide.</summary>
    public const int Lat = 23;

    /// <summary>S-100 vertical datum code for Mean Lower Low Water.</summary>
    public const int Mllw = 12;

    public static readonly DateTimeOffset Departure = new(2024, 6, 1, 6, 0, 0, TimeSpan.Zero);

    public static readonly GeoPosition Start = new(0, 0);

    public static readonly GeoPosition End = new(0, 0.1);

    public static PassageLeg Leg(double draft = 10, double? speed = 6, double squat = 0, double? required = 2) =>
        Leg([Start, End], draft, speed, squat, required, name: "Leg 1 → 2");

    /// <summary>A leg with its values in metres and knots; <c>null</c> leaves a value unset.</summary>
    public static PassageLeg Leg(
        IReadOnlyList<GeoPosition> points,
        double? draft = null,
        double? speed = null,
        double squat = 0,
        double? required = null,
        LegGeometry geometry = LegGeometry.Rhumb,
        double? port = null,
        double? starboard = null,
        string? name = null) => new()
        {
            Points = points,
            Geometry = geometry,
            PortLimit = port is { } p ? Length.FromMetres(p) : null,
            StarboardLimit = starboard is { } s ? Length.FromMetres(s) : null,
            Speed = speed is { } knots ? Speed.FromKnots(knots) : null,
            Draft = draft is { } d ? Depth.FromMetres(d) : null,
            Squat = Depth.FromMetres(squat),
            RequiredUkc = required is { } r ? Depth.FromMetres(r) : null,
            Name = name,
        };

    public static PassagePlan Plan(PassageLeg? leg = null, DateTimeOffset? departure = null) =>
        new([leg ?? Leg()], departure ?? Departure);

    /// <summary>Settings that judge at <paramref name="at"/> (default <see cref="Departure"/>), without depth checks.</summary>
    public static RouteCheckSettings Settings(DateTimeOffset? at = null) => new() { At = at ?? Departure };

    public static PassageCheckEngine Engine(IChartedDepthSource depth, IWaterLevelSource water) =>
        new(depth, water);
}
