using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Tests;

public class CellUsageBandTests
{
    [Theory]
    [InlineData("US1EEZ1M", 1)]
    [InlineData("US2EEZ2M", 2)]
    [InlineData("US3GC09M", 3)]
    [InlineData("US4CA52M", 4)]
    [InlineData("US5NY1AM", 5)]
    [InlineData("US6CN01M", 6)]
    public void TryParse_ValidBand_ReturnsDigit(string name, int expected)
    {
        Assert.Equal(expected, CellUsageBand.TryParse(name));
    }

    [Theory]
    [InlineData("US1EEZ1M.000", 1)]
    [InlineData("US5NY1AM.001", 5)]
    [InlineData("US1EEZ1M/US1EEZ1M.000", 1)]
    [InlineData("subdir\\US6CN01M.000", 6)]
    public void TryParse_WithExtensionOrPath_StripsAndParses(string name, int expected)
    {
        Assert.Equal(expected, CellUsageBand.TryParse(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]           // too short
    [InlineData("USXEEZ1M")]     // non-digit band
    [InlineData("US0EEZ1M")]     // band 0 out of range
    [InlineData("US7EEZ1M")]     // band 7 out of range
    public void TryParse_Invalid_ReturnsNull(string? name)
    {
        Assert.Null(CellUsageBand.TryParse(name));
    }
}

public class LazyCellGateTests
{
    private static BoundingBox Box(double s, double w, double n, double e) => new()
    {
        SouthBoundLatitude = s,
        WestBoundLongitude = w,
        NorthBoundLatitude = n,
        EastBoundLongitude = e,
    };

    [Fact]
    public void IntersectsViewport_Overlapping_True()
    {
        var cell = Box(40, -74, 41, -73);
        Assert.True(LazyCellGate.IntersectsViewport(cell, 40.5, -73.5, 42, -72));
    }

    [Fact]
    public void IntersectsViewport_Disjoint_False()
    {
        var cell = Box(40, -74, 41, -73);
        Assert.False(LazyCellGate.IntersectsViewport(cell, 10, 10, 20, 20));
    }

    [Fact]
    public void IntersectsViewport_TouchingEdge_True()
    {
        var cell = Box(40, -74, 41, -73);
        // Viewport's south edge exactly on the cell's north edge.
        Assert.True(LazyCellGate.IntersectsViewport(cell, 41, -74, 42, -73));
    }

    [Fact]
    public void IntersectsViewport_NullFootprint_TreatedAsIntersecting()
    {
        Assert.True(LazyCellGate.IntersectsViewport(null, 10, 10, 20, 20));
    }

    [Fact]
    public void IntersectsViewport_SeamCrossingCell_ViewportInEasternSegment_True()
    {
        // Cell spans the ±180° seam: west 170°E .. east -170°W (west > east).
        var cell = Box(-10, 170, 10, -170);
        // Viewport at 175..179°E lies in the cell's [170, +180] segment.
        Assert.True(LazyCellGate.IntersectsViewport(cell, -5, 175, 5, 179));
    }

    [Fact]
    public void IntersectsViewport_SeamCrossingCell_ViewportInWesternSegment_True()
    {
        var cell = Box(-10, 170, 10, -170);
        // Viewport at -179..-175°W lies in the cell's [-180, -170] segment.
        Assert.True(LazyCellGate.IntersectsViewport(cell, -5, -179, 5, -175));
    }

    [Fact]
    public void IntersectsViewport_SeamCrossingCell_ViewportInGap_False()
    {
        var cell = Box(-10, 170, 10, -170);
        // Viewport at 0..10°E lies in the un-covered gap (-170 .. 170).
        Assert.False(LazyCellGate.IntersectsViewport(cell, -5, 0, 5, 10));
    }

    [Fact]
    public void IntersectsViewport_SeamCrossingViewport_NonWrappingCell_True()
    {
        // Non-wrapping cell at 175..179°E; viewport crosses the seam.
        var cell = Box(-10, 175, 10, 179);
        Assert.True(LazyCellGate.IntersectsViewport(cell, -5, 170, 5, -170));
    }

    [Fact]
    public void IsBandEligible_OverviewAlwaysEligible()
    {
        Assert.True(LazyCellGate.IsBandEligible(1, 50_000_000));
        Assert.True(LazyCellGate.IsBandEligible(1, 1_000));
    }

    [Fact]
    public void IsBandEligible_BerthingOnlyWhenZoomedIn()
    {
        Assert.False(LazyCellGate.IsBandEligible(6, 2_000_000)); // zoomed out
        Assert.True(LazyCellGate.IsBandEligible(6, 5_000));      // zoomed in
    }

    [Fact]
    public void IsBandEligible_UnknownBandOrScale_FailsOpen()
    {
        Assert.True(LazyCellGate.IsBandEligible(null, 2_000_000));
        Assert.True(LazyCellGate.IsBandEligible(5, double.NaN));
        Assert.True(LazyCellGate.IsBandEligible(5, 0));
    }

    [Fact]
    public void ShouldBeLoaded_RequiresBothIntersectionAndBand()
    {
        var cell = Box(40, -74, 41, -73);

        // In view but wrong band (harbour cell, zoomed way out).
        Assert.False(LazyCellGate.ShouldBeLoaded(cell, 6, 3_000_000, 39, -75, 42, -72));

        // In view and appropriate band.
        Assert.True(LazyCellGate.ShouldBeLoaded(cell, 6, 5_000, 39, -75, 42, -72));

        // Right band but out of view.
        Assert.False(LazyCellGate.ShouldBeLoaded(cell, 6, 5_000, 10, 10, 20, 20));
    }

    [Fact]
    public void ScaleDenominator_AtEquator_MatchesFormula()
    {
        // 50.4 m/px at the equator → 50.4 / 0.00028 = 180 000.
        var denom = LazyCellGate.ScaleDenominator(50.4, 0.0);
        Assert.InRange(denom, 179_900, 180_100);
    }

    [Fact]
    public void ScaleDenominator_NonPositive_ReturnsNaN()
    {
        Assert.True(double.IsNaN(LazyCellGate.ScaleDenominator(0, 0)));
        Assert.True(double.IsNaN(LazyCellGate.ScaleDenominator(-1, 0)));
    }
}
