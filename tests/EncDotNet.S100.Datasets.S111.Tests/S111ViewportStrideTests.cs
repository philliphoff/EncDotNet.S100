using EncDotNet.S100.Renderers.Mapsui;
using PipelineViewport = EncDotNet.S100.Pipelines.Viewport;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Unit tests for <see cref="MapsuiCoverageArrowRenderer.ViewportStride"/>,
/// the viewport-aware decimation that keeps dense current grids from
/// emitting overlapping, illegible arrows that are also costly to redraw
/// on every pan frame.
/// </summary>
public sealed class S111ViewportStrideTests
{
    private static PipelineViewport Viewport(double lonSpan, double latSpan, int w, int h) => new()
    {
        MinLongitude = 0,
        MaxLongitude = lonSpan,
        MinLatitude = 0,
        MaxLatitude = latSpan,
        WidthPixels = w,
        HeightPixels = h,
        ScaleDenominator = 1,
    };

    [Fact]
    public void WideExtent_TightCells_IncreasesStride()
    {
        // 10° across 1000 px → 100 px/deg. Cells 0.01° apart → 1 px on each
        // axis, i.e. √2 ≈ 1.414 px diagonally. To reach 14 px spacing the
        // stride must be ceil(14 / 1.414) = 10.
        var vp = Viewport(10, 10, 1000, 1000);
        int stride = MapsuiCoverageArrowRenderer.ViewportStride(vp, 0.01, 0.01, 14.0);
        Assert.Equal(10, stride);
    }

    [Fact]
    public void ZoomedIn_WideCells_NoExtraDecimation()
    {
        // 0.1° across 1000 px → 10000 px/deg. Cells 0.01° apart → 100 px.
        // Already well beyond the 14 px floor, so stride stays 1.
        var vp = Viewport(0.1, 0.1, 1000, 1000);
        int stride = MapsuiCoverageArrowRenderer.ViewportStride(vp, 0.01, 0.01, 14.0);
        Assert.Equal(1, stride);
    }

    [Fact]
    public void DisabledFloor_ReturnsOne()
    {
        var vp = Viewport(10, 10, 1000, 1000);
        int stride = MapsuiCoverageArrowRenderer.ViewportStride(vp, 0.01, 0.01, 0.0);
        Assert.Equal(1, stride);
    }

    [Fact]
    public void DegenerateViewport_ReturnsOne()
    {
        var vp = Viewport(0, 0, 0, 0);
        int stride = MapsuiCoverageArrowRenderer.ViewportStride(vp, 0.01, 0.01, 14.0);
        Assert.Equal(1, stride);
    }
}
