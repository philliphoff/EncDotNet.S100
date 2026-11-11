using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Tests for <see cref="CoveredCellGate"/> (#1265).</summary>
public sealed class CoveredCellGateTests
{
    // View: 40..41 N, 75..74 W.
    private static readonly LoadWindow Window = LoadWindow.From(
        new MapViewportSnapshot
        {
            MinLatitude = 40,
            MinLongitude = -75,
            MaxLatitude = 41,
            MaxLongitude = -74,
            MercatorResolution = 5.0,
        },
        previous: null,
        aheadMarginFraction: 0.5);

    private static GeoPolygon Rect(double south, double west, double north, double east) => new(
    [
        new GeoPosition(south, west),
        new GeoPosition(south, east),
        new GeoPosition(north, east),
        new GeoPosition(north, west),
    ]);

    private static Geometry Coverage(params GeoPolygon[] polygons) =>
        CoveredCellGate.FromCatalogueCoverage(new GeoCoverage(polygons))
        ?? throw new InvalidOperationException("no coverage");

    private static BoundingBox Box(double south, double west, double north, double east) => new()
    {
        SouthBoundLatitude = south,
        WestBoundLongitude = west,
        NorthBoundLatitude = north,
        EastBoundLongitude = east,
    };

    [Fact]
    public void FromCatalogueCoverage_UsesLongitudeAsX()
    {
        var geometry = Coverage(Rect(40, -75, 41, -74));

        Assert.Equal(-75, geometry.EnvelopeInternal.MinX);
        Assert.Equal(40, geometry.EnvelopeInternal.MinY);
        Assert.Equal(1.0, geometry.Area, 9);
    }

    [Fact]
    public void FromCatalogueCoverage_KeepsHoles()
    {
        var withHole = new GeoPolygon(
            Rect(40, -75, 41, -74).Exterior,
            [Rect(40.25, -74.75, 40.75, -74.25).Exterior]);

        var geometry = CoveredCellGate.FromCatalogueCoverage(new GeoCoverage([withHole]));

        Assert.NotNull(geometry);
        Assert.Equal(0.75, geometry.Area, 9);
    }

    [Fact]
    public void FromCatalogueCoverage_RejectsRingsAcrossTheAntimeridian()
    {
        var crossing = Rect(-20, 179, -19, -179);

        Assert.Null(CoveredCellGate.FromCatalogueCoverage(new GeoCoverage([crossing])));
        Assert.Null(CoveredCellGate.FromCatalogueCoverage(null));
    }

    [Fact]
    public void TwoFinerCellsSideBySideCoverTheView()
    {
        var finer = new[] { Coverage(Rect(39, -76, 42, -74.5)), Coverage(Rect(39, -74.5, 42, -73)) };

        Assert.True(CoveredCellGate.IsCoveredInView(
            coverage: null, Box(30, -80, 50, -70), finer, Window, tolerance: 0));
    }

    [Fact]
    public void AnUncoveredCornerOfTheViewKeepsTheCellVisible()
    {
        // The finer cell stops short of the view's north-east corner, as finer
        // cells do along a coastline the coarse cell still charts.
        var finer = new[] { Coverage(Rect(39, -76, 40.8, -73)), Coverage(Rect(40.8, -76, 42, -74.2)) };

        Assert.False(CoveredCellGate.IsCoveredInView(
            coverage: null, Box(30, -80, 50, -70), finer, Window, tolerance: 0));
    }

    [Fact]
    public void OnlyTheCoarseCellsOwnCoverageHasToBeCovered()
    {
        // The coarse cell's coverage stops at 40.5 N; finer cells cover that
        // part of the view, so the uncovered north half doesn't matter.
        var coarse = Coverage(Rect(30, -80, 40.5, -70));
        var finer = new[] { Coverage(Rect(39, -76, 40.6, -73)) };

        Assert.True(CoveredCellGate.IsCoveredInView(coarse, Box(30, -80, 50, -70), finer, Window, tolerance: 0));
        Assert.False(CoveredCellGate.IsCoveredInView(null, Box(30, -80, 50, -70), finer, Window, tolerance: 0));
    }

    [Fact]
    public void ACoarseCellWhoseCoverageMissesTheViewIsCovered()
    {
        // The bounding box reaches the view but the coverage doesn't.
        var coarse = Coverage(Rect(30, -80, 39, -70));
        var finer = new[] { Coverage(Rect(40.2, -74.8, 40.4, -74.6)) };

        Assert.True(CoveredCellGate.IsCoveredInView(coarse, Box(30, -80, 50, -70), finer, Window, tolerance: 0));
    }

    [Fact]
    public void ASliverNarrowerThanTheToleranceIsIgnored()
    {
        // A 0.0001° gap between two finer cells.
        var finer = new[] { Coverage(Rect(39, -76, 42, -74.5)), Coverage(Rect(39, -74.4999, 42, -73)) };
        var box = Box(30, -80, 50, -70);

        Assert.False(CoveredCellGate.IsCoveredInView(null, box, finer, Window, tolerance: 0));
        Assert.False(CoveredCellGate.IsCoveredInView(null, box, finer, Window, tolerance: 0.00005));
        Assert.True(CoveredCellGate.IsCoveredInView(null, box, finer, Window, tolerance: 0.0002));
    }

    [Fact]
    public void NoFinerCellsMeansNotCovered()
    {
        Assert.False(CoveredCellGate.IsCoveredInView(null, Box(30, -80, 50, -70), [], Window, tolerance: 0));
        Assert.False(CoveredCellGate.IsCoveredInView(
            null, bounds: null, [Coverage(Rect(39, -76, 42, -73))], Window, tolerance: 0));
    }

    [Fact]
    public void ToleranceIsPixelsAtTheViewResolution()
    {
        // 1 px at 111.32 m/px (about 0.001° at the equator).
        var degrees = CoveredCellGate.ToleranceDegrees(6_378_137.0 * Math.PI / 180.0 / 1000, latitude: 0, pixels: 1);

        Assert.Equal(0.001, degrees, 9);
        Assert.Equal(0.0005, CoveredCellGate.ToleranceDegrees(6_378_137.0 * Math.PI / 180.0 / 1000, latitude: 60, pixels: 1), 9);
        Assert.Equal(0, CoveredCellGate.ToleranceDegrees(double.NaN, 0, 1));
    }
}
