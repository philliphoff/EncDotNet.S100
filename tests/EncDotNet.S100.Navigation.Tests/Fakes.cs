using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>A charted-depth source defined by a function of position.</summary>
internal sealed class FakeDepth(Func<double, double, ChartedDepth?> depthAt, double? gridSpacingMeters = null) : IChartedDepthSource
{
    public int Calls { get; private set; }

    public double? FinestGridSpacingMeters(double south, double west, double north, double east) => gridSpacingMeters;

    public ChartedDepth? Sample(double latitude, double longitude)
    {
        Calls++;
        return depthAt(latitude, longitude);
    }

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

    public IReadOnlyList<IWaterLevelSeries> Locate(double latitude, double longitude)
    {
        Calls++;
        return locate(latitude, longitude);
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
        new([Start, End], draft, speed, squat, required, Name: "Leg 1 → 2");

    public static PassagePlan Plan(PassageLeg? leg = null, DateTimeOffset? departure = null) =>
        new([leg ?? Leg()], departure ?? Departure);

    public static PassageCheckEngine Engine(IChartedDepthSource depth, IWaterLevelSource water) =>
        new(depth, water);
}
