using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Core.Tests.Validation;

public class GridGeoreferencingTests
{
    [Theory]
    [InlineData(null, HorizontalCrsKind.Geographic)]
    [InlineData(4326, HorizontalCrsKind.Geographic)]
    [InlineData(4269, HorizontalCrsKind.Geographic)]
    [InlineData(32610, HorizontalCrsKind.Projected)]
    [InlineData(32760, HorizontalCrsKind.Projected)]
    [InlineData(26918, HorizontalCrsKind.Projected)]
    [InlineData(3857, HorizontalCrsKind.Projected)]
    [InlineData(12345, HorizontalCrsKind.Unknown)]
    public void Classify_Recognises_Geographic_And_Projected_Codes(int? epsg, HorizontalCrsKind expected)
        => Assert.Equal(expected, GridGeoreferencing.Classify(epsg));

    [Fact]
    public void Geographic_Position_Is_Range_Checked_In_Degrees()
    {
        var georef = GridGeoreferencing.For(4326, null);

        Assert.Empty(georef.CheckPosition(-122.4, 47.55, "lon", "lat"));
        var problems = georef.CheckPosition(545_082.9, 5_266_464.6, "lon", "lat", longitudeNote: "(note)");
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("lat 5266464.6 outside [-90, 90]", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.EndsWith("outside [-180, 180] (note)", StringComparison.Ordinal));
    }

    [Fact]
    public void Utm_Position_Is_Range_Checked_In_Metres()
    {
        var georef = GridGeoreferencing.For(32610, null);

        Assert.Empty(georef.CheckPosition(545_082.9, 5_266_464.6, "x", "y"));
        var problems = georef.CheckPosition(-122.4, 10_500_000, "x", "y");
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("easting", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("northing", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_Crs_Is_Not_Checked()
    {
        var georef = GridGeoreferencing.For(12345, null);

        Assert.Empty(georef.CheckPosition(1e9, -1e9, "x", "y"));
        Assert.False(georef.TryGetGeographicBounds(0, 0, 1, 1, out var bounds));
        Assert.Null(bounds);
    }

    [Fact]
    public void Projected_Position_Must_Reproject_To_A_Valid_Wgs84_Position_When_A_Transform_Is_Available()
    {
        // A transform that lands out of range stands in for a corrupt projected position.
        var context = new ValidationContext { CrsTransformFactory = new FixedTransformFactory(lon: 10, lat: 95) };
        var georef = GridGeoreferencing.For(3857, context);

        var problem = Assert.Single(georef.CheckPosition(1_000, 2_000, "x", "y"));
        Assert.Contains("does not reproject to a valid WGS 84 position", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_Bounds_Are_Reprojected_Only_With_A_Transform()
    {
        Assert.False(GridGeoreferencing.For(32610, null).TryGetGeographicBounds(545_000, 5_266_000, 550_000, 5_275_000, out _));

        var context = new ValidationContext { CrsTransformFactory = new FixedTransformFactory(lon: -122.4, lat: 47.6) };
        Assert.True(GridGeoreferencing.For(32610, context).TryGetGeographicBounds(545_000, 5_266_000, 550_000, 5_275_000, out var bounds));
        Assert.Equal(new BoundingBox(47.6, -122.4, 47.6, -122.4), bounds);
    }

    [Fact]
    public void Unsupported_Transform_Falls_Back_To_Native_Checks()
    {
        var context = new ValidationContext { CrsTransformFactory = new ThrowingTransformFactory() };
        var georef = GridGeoreferencing.For(32610, context);

        Assert.Empty(georef.CheckPosition(545_082.9, 5_266_464.6, "x", "y"));
        Assert.False(georef.TryGetGeographicBounds(545_000, 5_266_000, 550_000, 5_275_000, out _));
    }

    private sealed class FixedTransformFactory(double lon, double lat) : ICrsTransformFactory, ICrsTransform
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) => this;
        public (double X, double Y) Transform(double x, double y) => (lon, lat);
        public bool IsIdentity => false;
    }

    private sealed class ThrowingTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) => throw new NotSupportedException(sourceCrs);
    }
}
