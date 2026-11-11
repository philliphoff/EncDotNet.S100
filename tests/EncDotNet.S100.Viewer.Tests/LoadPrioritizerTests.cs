using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LoadPrioritizerTests
{
    private static BoundingBox Box(double south, double west, double north, double east) => new()
    {
        SouthBoundLatitude = south,
        WestBoundLongitude = west,
        NorthBoundLatitude = north,
        EastBoundLongitude = east,
    };

    private static MapViewportSnapshot View(double south, double west, double north, double east) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = north,
        MaxLongitude = east,
        MercatorResolution = 5.0,
    };

    private static LoadTier? Classify(BoundingBox? cell, LoadWindow window) =>
        LoadPrioritizer.Classify(cell, band: null, minimumDisplayScale: null, scaleDenominator: 10_000, window);

    [Fact]
    public void CellInViewIsVisible()
    {
        var window = LoadWindow.From(View(40, -75, 41, -74), null, 0.5);

        Assert.Equal(LoadTier.Visible, Classify(Box(40.2, -74.8, 40.4, -74.6), window));
    }

    [Fact]
    public void CellWithinTheMarginIsAhead()
    {
        var window = LoadWindow.From(View(40, -75, 41, -74), null, 0.5);

        // Just east of the view, inside the half-span margin.
        Assert.Equal(LoadTier.Ahead, Classify(Box(40.2, -73.8, 40.4, -73.6), window));
    }

    [Fact]
    public void CellBeyondTheMarginHasNoTier()
    {
        var window = LoadWindow.From(View(40, -75, 41, -74), null, 0.5);

        Assert.Null(Classify(Box(40.2, -72, 40.4, -71), window));
    }

    [Fact]
    public void CellWithoutFootprintIsUnbounded()
    {
        var window = LoadWindow.From(View(40, -75, 41, -74), null, 0.5);

        Assert.Equal(LoadTier.Unbounded, Classify(null, window));
    }

    [Fact]
    public void CellOutOfScaleHasNoTierEvenInView()
    {
        var window = LoadWindow.From(View(40, -75, 41, -74), null, 0.5);

        // Declares 1:20 000 as its coarsest scale; the view is at 1:200 000.
        Assert.Null(LoadPrioritizer.Classify(Box(40.2, -74.8, 40.4, -74.6), null, 20_000, 200_000, window));
        Assert.Null(LoadPrioritizer.Classify(null, null, 20_000, 200_000, window));
    }

    [Fact]
    public void AheadWindowWrapsAcrossTheAntimeridian()
    {
        var window = LoadWindow.From(View(-20, 178, -18, 179.5), null, 0.5);

        // 179.9°W is just across the seam from the view's east edge.
        Assert.Equal(LoadTier.Ahead, Classify(Box(-19.5, -179.9, -19, -179.8), window));
    }

    [Fact]
    public void VisibleCellsRankByDistanceFromCentre()
    {
        var window = LoadWindow.From(View(40, -75, 41, -74), null, 0.5);
        var middle = Box(40.4, -74.6, 40.6, -74.4);
        var corner = Box(40.0, -75.0, 40.1, -74.9);

        Assert.True(
            LoadPrioritizer.Rank(middle, LoadTier.Visible, window)
            < LoadPrioritizer.Rank(corner, LoadTier.Visible, window));
    }

    [Fact]
    public void AheadCellsInTheDirectionOfTravelRankFirst()
    {
        // The view moved one degree east since the last evaluation.
        var window = LoadWindow.From(View(40, -75, 41, -74), View(40, -76, 41, -75), 0.5);
        var east = Box(40.4, -73.8, 40.6, -73.6);
        var west = Box(40.4, -75.4, 40.6, -75.2);

        Assert.True(
            LoadPrioritizer.Rank(east, LoadTier.Ahead, window)
            < LoadPrioritizer.Rank(west, LoadTier.Ahead, window));
    }
}
