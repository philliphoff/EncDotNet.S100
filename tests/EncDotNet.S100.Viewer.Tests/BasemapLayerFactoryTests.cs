using System;
using System.Linq;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Viewer;
using Mapsui.Layers;
using Mapsui.Nts;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #295: the basemap factory builds the bundled offline Natural
/// Earth land layer, returns nothing for <see cref="BasemapMode.None"/>,
/// and an online tile layer for <see cref="BasemapMode.Online"/>. The
/// offline layer additionally repeats its land across adjacent world
/// copies so datasets kept in a continuous longitude frame across the
/// ±180° antimeridian (e.g. the US NWS S-411 sea-ice product) have land
/// beneath them.
/// </summary>
public class BasemapLayerFactoryTests
{
    private const double Extent = WebMercator.Circumference / 2.0;

    [Fact]
    public void None_ReturnsNull()
    {
        Assert.Null(BasemapLayerFactory.Create(BasemapMode.None));
    }

    [Fact]
    public void Offline_BuildsMemoryLayerWithLandFeatures()
    {
        var layer = BasemapLayerFactory.Create(BasemapMode.Offline);

        var memory = Assert.IsAssignableFrom<MemoryLayer>(layer);
        Assert.NotEmpty(memory.Features);
    }

    [Fact]
    public void Offline_RepeatsLandAcrossAdjacentWorldCopies()
    {
        var layer = Assert.IsAssignableFrom<MemoryLayer>(
            BasemapLayerFactory.Create(BasemapMode.Offline));

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        foreach (var feature in layer.Features.OfType<GeometryFeature>())
        {
            if (feature.Geometry is null)
                continue;

            var env = feature.Geometry.EnvelopeInternal;
            minX = Math.Min(minX, env.MinX);
            maxX = Math.Max(maxX, env.MaxX);
        }

        // The eastern (+1) copy must reach beyond +180° so continuous-frame
        // S-411 ice east of the antimeridian has land under it; the western
        // (-1) copy likewise reaches below -180°.
        Assert.True(maxX > Extent, $"expected land east of +Extent, got maxX={maxX}");
        Assert.True(minX < -Extent, $"expected land west of -Extent, got minX={minX}");
    }

    [Fact]
    public void Offline_ReportsBoundedSingleWorldExtent()
    {
        var extent = Assert.IsAssignableFrom<MemoryLayer>(
            BasemapLayerFactory.Create(BasemapMode.Offline)).Extent;

        // Even though the geometry spans world copies, the layer must report
        // only the canonical single world so the copies never inflate
        // Map.Extent (which drives "zoom to extent").
        Assert.NotNull(extent);
        Assert.Equal(-Extent, extent!.MinX, 3);
        Assert.Equal(-Extent, extent.MinY, 3);
        Assert.Equal(Extent, extent.MaxX, 3);
        Assert.Equal(Extent, extent.MaxY, 3);
    }

    [Fact]
    public void Online_BuildsTileLayer()
    {
        var layer = BasemapLayerFactory.Create(BasemapMode.Online);

        Assert.NotNull(layer);
        Assert.IsType<Mapsui.Tiling.Layers.TileLayer>(layer);
    }
}
