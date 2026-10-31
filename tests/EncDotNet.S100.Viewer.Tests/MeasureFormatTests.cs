using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;

namespace EncDotNet.S100.Viewer.Tests;

public class MeasureFormatTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(33193.554, nameof(DistanceUnit.NauticalMiles), "17.92 NM", "33.194 km")]
    [InlineData(33193.554, nameof(DistanceUnit.Metric), "33.194 km", "17.92 NM")]
    [InlineData(33193.554, nameof(DistanceUnit.Miles), "20.63 mi", "33.194 km")]
    [InlineData(950.4, nameof(DistanceUnit.Metric), "950 m", "0.51 NM")]
    [InlineData(999.6, nameof(DistanceUnit.Metric), "1.000 km", "0.54 NM")]
    [InlineData(2_000_000, nameof(DistanceUnit.Metric), "2,000.000 km", "1,079.91 NM")]
    public void Distance_shows_the_primary_unit_and_the_other_system(
        double metres, string unitName, string primary, string other)
    {
        var unit = Enum.Parse<DistanceUnit>(unitName);
        Assert.Equal(primary, MeasureFormat.Distance(metres, unit, Invariant));
        Assert.Equal(other, MeasureFormat.OtherDistance(metres, unit, Invariant));
    }

    [Theory]
    [InlineData(295.614, "295.6°")]
    [InlineData(295.65, "295.7°")]
    [InlineData(5, "005.0°")]
    [InlineData(359.96, "000.0°")]
    [InlineData(-90, "270.0°")]
    public void Bearing_shows_tenths_and_never_360(double degrees, string expected) =>
        Assert.Equal(expected, MeasureFormat.Bearing(degrees, Invariant));

    [Theory]
    [InlineData("2000000 m", nameof(DistanceUnit.NauticalMiles), 2_000_000)]
    [InlineData("2,000 km", nameof(DistanceUnit.NauticalMiles), 2_000_000)]
    [InlineData("2000KM", nameof(DistanceUnit.NauticalMiles), 2_000_000)]
    [InlineData("1079.9 NM", nameof(DistanceUnit.Metric), 1079.9 * 1852)]
    [InlineData("1079.9 nm", nameof(DistanceUnit.Metric), 1079.9 * 1852)]
    [InlineData("1.2 mi", nameof(DistanceUnit.NauticalMiles), 1.2 * 1609.344)]
    [InlineData("17.9231", nameof(DistanceUnit.NauticalMiles), 17.9231 * 1852)]
    [InlineData("33.193554", nameof(DistanceUnit.Metric), 33193.554)]
    [InlineData(" 2 ", nameof(DistanceUnit.Miles), 2 * 1609.344)]
    public void Range_reads_a_number_in_the_primary_unit_or_its_suffix(string text, string unitName, double metres)
    {
        var unit = Enum.Parse<DistanceUnit>(unitName);
        Assert.True(MeasureFormat.TryParseRange(text, unit, out var parsed, out var error));
        Assert.Equal(RangeParseError.None, error);
        Assert.Equal(metres, parsed, 6);
    }

    [Theory]
    [InlineData("", nameof(RangeParseError.Empty))]
    [InlineData("   ", nameof(RangeParseError.Empty))]
    [InlineData(null, nameof(RangeParseError.Empty))]
    [InlineData("2 M", nameof(RangeParseError.AmbiguousM))]
    [InlineData("2M", nameof(RangeParseError.AmbiguousM))]
    [InlineData("2 furlongs", nameof(RangeParseError.NotARange))]
    [InlineData("km", nameof(RangeParseError.NotARange))]
    [InlineData("two km", nameof(RangeParseError.NotARange))]
    [InlineData("0 m", nameof(RangeParseError.NotPositive))]
    [InlineData("-5 NM", nameof(RangeParseError.NotPositive))]
    public void Range_refuses_what_it_cannot_read(string? text, string expectedName)
    {
        var expected = Enum.Parse<RangeParseError>(expectedName);
        Assert.False(MeasureFormat.TryParseRange(text, DistanceUnit.NauticalMiles, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("295.614", 295.614)]
    [InlineData("295.614°", 295.614)]
    [InlineData("295.614°T", 295.614)]
    [InlineData(" 38.7 ° t ", 38.7)]
    [InlineData("0", 0)]
    [InlineData("360", 0)]
    public void Bearing_reads_degrees_with_an_optional_degree_sign_and_T(string text, double expected)
    {
        Assert.True(MeasureFormat.TryParseBearing(text, out var degrees));
        Assert.Equal(expected, degrees, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("361")]
    [InlineData("-1")]
    [InlineData("north")]
    public void Bearing_refuses_what_it_cannot_read(string text) =>
        Assert.False(MeasureFormat.TryParseBearing(text, out _));
}

public class MeasureDensifyTests
{
    [Fact]
    public void Great_circle_is_split_into_pieces_no_longer_than_the_limit()
    {
        var from = new GeoPosition(30, -60);
        var to = new GeoPosition(60, -30);

        var points = MarineGeodesy.DensifyGreatCircle(from, to);

        Assert.Equal(from, points[0]);
        Assert.Equal(to, points[^1]);
        var length = MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, from, to);
        Assert.Equal((int)Math.Ceiling(length / MarineGeodesy.MaxSegmentMetres) + 1, points.Count);
        for (var i = 1; i < points.Count; i++)
        {
            Assert.True(MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, points[i - 1], points[i]) <= MarineGeodesy.MaxSegmentMetres + 1e-6);
        }
    }

    [Fact]
    public void A_short_great_circle_is_one_piece()
    {
        var points = MarineGeodesy.DensifyGreatCircle(new GeoPosition(50, -1), new GeoPosition(50.1, -1.1));

        Assert.Equal(2, points.Count);
    }

    [Fact]
    public void Geodesic_circle_points_all_lie_at_the_radius()
    {
        var centre = new GeoPosition(45, -45);
        const double radius = 2_000_000;

        var points = MarineGeodesy.GeodesicCircle(centre, radius);

        Assert.Equal(points[0], points[^1]);
        Assert.True(points.Count - 1 >= MarineGeodesy.MinCircleSegments);
        foreach (var point in points)
        {
            Assert.Equal(radius, MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, centre, point), 3);
        }

        for (var i = 1; i < points.Count; i++)
        {
            Assert.True(MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, points[i - 1], points[i]) <= MarineGeodesy.MaxSegmentMetres);
        }
    }

    [Fact]
    public void Geodesic_circle_starts_due_north_of_the_centre()
    {
        var centre = new GeoPosition(45, -45);

        var north = MarineGeodesy.GeodesicCircle(centre, 2_000_000)[0];

        Assert.Equal(-45, north.Longitude, 9);
        Assert.Equal(45 + 2_000_000 / GeodesicHelper.EarthRadiusMetres * 180 / Math.PI, north.Latitude, 9);
    }

    [Fact]
    public void A_small_circle_still_has_the_minimum_pieces()
    {
        var points = MarineGeodesy.GeodesicCircle(new GeoPosition(50, -1), 500);

        Assert.Equal(MarineGeodesy.MinCircleSegments + 1, points.Count);
    }

    [Fact]
    public void A_circle_round_the_pole_splits_at_the_antimeridian_into_open_lines()
    {
        var centre = new GeoPosition(80, 0);

        var points = MarineGeodesy.GeodesicCircle(centre, 2_000_000);
        var parts = MarineGeodesy.SplitAtAntimeridian(points);

        Assert.Equal(2, parts.Count);
        Assert.Equal(points.Count, parts.Sum(p => p.Count));
    }
}
