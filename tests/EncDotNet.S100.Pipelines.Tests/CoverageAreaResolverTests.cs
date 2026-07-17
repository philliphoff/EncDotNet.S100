using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies coverage-polygon extraction for cross-cell scale-band overlap
/// suppression (<see cref="CoverageAreaResolver"/>, issue #438 Phase 2): only
/// <c>DataCoverage</c> surface features contribute, no-coverage
/// (<c>categoryOfCoverage = 2</c>) meta-objects are excluded, and interior
/// rings (no-coverage holes) are preserved.
/// </summary>
public class CoverageAreaResolverTests
{
    private static Feature Coverage(
        IReadOnlyList<GeoPosition> exterior,
        IReadOnlyList<IReadOnlyList<GeoPosition>>? holes = null,
        object? categoryOfCoverage = null)
    {
        var attributes = new Dictionary<string, object?>();
        if (categoryOfCoverage is not null)
            attributes["categoryOfCoverage"] = categoryOfCoverage;

        return new Feature
        {
            Id = 1,
            FeatureType = "DataCoverage",
            GeometryType = GeometryType.Surface,
            Coordinates = exterior,
            InteriorRings = holes ?? [],
            Attributes = attributes,
        };
    }

    private static IReadOnlyList<GeoPosition> Square(double lat0, double lon0, double size) =>
    [
        new GeoPosition(lat0, lon0),
        new GeoPosition(lat0, lon0 + size),
        new GeoPosition(lat0 + size, lon0 + size),
        new GeoPosition(lat0 + size, lon0),
        new GeoPosition(lat0, lon0),
    ];

    [Fact]
    public void Resolve_SingleDataCoverageSurface_YieldsOneArea()
    {
        var features = new[] { Coverage(Square(48.5, -123.0, 0.1)) };

        var areas = CoverageAreaResolver.Resolve(features);

        Assert.Single(areas);
        Assert.Equal(5, areas[0].ExteriorRing.Count);
        Assert.Empty(areas[0].InteriorRings);
    }

    [Fact]
    public void Resolve_NoCoverageCategory_IsExcluded()
    {
        var features = new[]
        {
            Coverage(Square(48.5, -123.0, 0.1), categoryOfCoverage: 1),
            Coverage(Square(49.0, -123.0, 0.1), categoryOfCoverage: 2),
        };

        var areas = CoverageAreaResolver.Resolve(features);

        Assert.Single(areas);
    }

    [Fact]
    public void Resolve_NoCoverageCategoryAsEnumLabel_IsExcluded()
    {
        var features = new[]
        {
            Coverage(Square(48.5, -123.0, 0.1), categoryOfCoverage: "noCoverageAvailable"),
        };

        var areas = CoverageAreaResolver.Resolve(features);

        Assert.Empty(areas);
    }

    [Fact]
    public void Resolve_PreservesInteriorRings()
    {
        var holes = new IReadOnlyList<GeoPosition>[] { Square(48.52, -122.98, 0.02) };
        var features = new[] { Coverage(Square(48.5, -123.0, 0.1), holes) };

        var areas = CoverageAreaResolver.Resolve(features);

        Assert.Single(areas);
        Assert.Single(areas[0].InteriorRings);
    }

    [Fact]
    public void Resolve_NonSurfaceOrNonCoverageFeatures_AreIgnored()
    {
        var features = new[]
        {
            new Feature
            {
                Id = 2,
                FeatureType = "DepthArea",
                GeometryType = GeometryType.Surface,
                Coordinates = Square(48.5, -123.0, 0.1),
                InteriorRings = [],
                Attributes = new Dictionary<string, object?>(),
            },
            new Feature
            {
                Id = 3,
                FeatureType = "DataCoverage",
                GeometryType = GeometryType.Curve,
                Coordinates = Square(48.5, -123.0, 0.1),
                InteriorRings = [],
                Attributes = new Dictionary<string, object?>(),
            },
        };

        var areas = CoverageAreaResolver.Resolve(features);

        Assert.Empty(areas);
    }

    [Fact]
    public void Resolve_DegenerateRing_IsSkipped()
    {
        var features = new[]
        {
            Coverage([new GeoPosition(48.5, -123.0), new GeoPosition(48.6, -123.0)]),
        };

        var areas = CoverageAreaResolver.Resolve(features);

        Assert.Empty(areas);
    }
}
