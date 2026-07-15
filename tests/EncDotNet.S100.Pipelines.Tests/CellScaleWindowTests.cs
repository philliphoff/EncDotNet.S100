using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the hole-safe per-cell zoom-out visibility window
/// (<see cref="MapsuiDatasetRenderer.ApplyCellScaleWindow"/>, issue #438
/// Phase 1): a cell's layers stop drawing once zoomed out beyond the cell's
/// coarsest intended scale (<c>minimumDisplayScale</c>), converted to a Mapsui
/// resolution at the layer's extent-centre latitude, clamped so it only ever
/// tightens.
/// </summary>
public class CellScaleWindowTests
{
    private const double DenomToResolutionMetres = 0.00028;

    private static MemoryLayer LayerAt(double longitude, double latitude, double? maxVisible = null)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        var layer = new MemoryLayer { Features = new[] { new PointFeature(x, y) } };
        if (maxVisible is double m)
            layer.MaxVisible = m;
        return layer;
    }

    [Fact]
    public void Window_AtEquator_ClampsMaxVisibleToBand()
    {
        // 90000 * 0.00028 / cos(0) = 25.2
        var layer = LayerAt(0, 0);

        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)layer }, 90000);

        Assert.Equal(25.2, layer.MaxVisible, 3);
    }

    [Fact]
    public void Window_AppliesWebMercatorCosineCorrection()
    {
        // At 60°N, cos φ = 0.5, so the resolution doubles: 25.2 / 0.5 = 50.4.
        var layer = LayerAt(0, 60);

        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)layer }, 90000);

        Assert.Equal(50.4, layer.MaxVisible, 2);
    }

    [Fact]
    public void Window_TighterExistingMaxVisible_IsPreserved()
    {
        var layer = LayerAt(0, 0, maxVisible: 5.0);

        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)layer }, 90000);

        Assert.Equal(5.0, layer.MaxVisible, 3);
    }

    [Fact]
    public void Window_LooserExistingMaxVisible_IsTightened()
    {
        var layer = LayerAt(0, 0, maxVisible: 1000.0);

        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)layer }, 90000);

        Assert.Equal(25.2, layer.MaxVisible, 3);
    }

    [Fact]
    public void Window_NonPositiveDenominator_LeavesLayerUntouched()
    {
        var layer = LayerAt(0, 0, maxVisible: 1000.0);

        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)layer }, 0);

        Assert.Equal(1000.0, layer.MaxVisible, 3);
    }

    [Fact]
    public void Window_FinerCellDropsOutBeforeCoarserCell()
    {
        // A finer (harbour) cell has a smaller minimumDisplayScale than the
        // coarser (coastal) cell, so its MaxVisible resolution is smaller and
        // it stops drawing first as the viewport zooms out — the hole-safe
        // ordering that leaves the coarser cell underneath visible.
        var harbour = LayerAt(0, 0);
        var coastal = LayerAt(0, 0);

        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)harbour }, 8000);
        MapsuiDatasetRenderer.ApplyCellScaleWindow(new[] { (ILayer)coastal }, 90000);

        Assert.True(harbour.MaxVisible < coastal.MaxVisible);
        Assert.Equal(8000 * DenomToResolutionMetres, harbour.MaxVisible, 3);
        Assert.Equal(90000 * DenomToResolutionMetres, coastal.MaxVisible, 3);
    }
}
