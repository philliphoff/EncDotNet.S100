using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Validates the palette- and value-independent projection layout cache in
/// <see cref="MapsuiCoverageRenderer"/>: the cached node→pixel mapping must be
/// reused across renders that change only the colour palette or coverage
/// values, without altering the rendered output.
/// </summary>
public class MapsuiCoverageRendererCacheTests
{
    private static readonly Viewport DefaultViewport = new()
    {
        MinLatitude = 0,
        MaxLatitude = 1,
        MinLongitude = 0,
        MaxLongitude = 1,
        WidthPixels = 100,
        HeightPixels = 100,
        ScaleDenominator = 50_000,
    };

    [Fact]
    public void Render_RepeatedSameGeometry_ReusesLayout()
    {
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory());
        var layer = MakeStyledLayer(SchemeA, new float[,] { { 5f, 25f }, { 1f, 50f } });

        var first = GetPng(renderer.Render(layer, DefaultViewport));
        var second = GetPng(renderer.Render(layer, DefaultViewport));

        Assert.Equal(1, renderer.LayoutCacheMisses);
        Assert.Equal(1, renderer.LayoutCacheHits);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Render_PaletteChange_ReusesLayout_ButChangesOutput()
    {
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory());
        var depths = new float[,] { { 5f, 25f }, { 1f, 50f } };

        var withA = GetPng(renderer.Render(MakeStyledLayer(SchemeA, depths), DefaultViewport));
        var withB = GetPng(renderer.Render(MakeStyledLayer(SchemeB, depths), DefaultViewport));

        // Geometry unchanged → one build, one reuse.
        Assert.Equal(1, renderer.LayoutCacheMisses);
        Assert.Equal(1, renderer.LayoutCacheHits);

        // Different palette → different pixels → different PNG bytes.
        Assert.NotEqual(withA, withB);

        // A palette switch on the cached renderer must match a fresh renderer
        // that never cached the first palette's geometry.
        var fresh = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory());
        var freshB = GetPng(fresh.Render(MakeStyledLayer(SchemeB, depths), DefaultViewport));
        Assert.Equal(freshB, withB);
    }

    [Fact]
    public void Render_DifferentGeometry_RebuildsLayout()
    {
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory());

        renderer.Render(MakeStyledLayer(SchemeA, new float[,] { { 5f, 25f }, { 1f, 50f } }), DefaultViewport);
        // Different origin → different geometry → cache miss.
        renderer.Render(MakeStyledLayer(SchemeA, new float[,] { { 5f, 25f }, { 1f, 50f } }, originLat: 10.0), DefaultViewport);

        Assert.Equal(2, renderer.LayoutCacheMisses);
        Assert.Equal(0, renderer.LayoutCacheHits);
    }

    [Fact]
    public void Render_ValueChangeSameGeometry_ReusesLayout()
    {
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory());

        // Simulates a time-step change: identical grid geometry, new values.
        var step1 = GetPng(renderer.Render(MakeStyledLayer(SchemeA, new float[,] { { 5f, 25f }, { 1f, 50f } }), DefaultViewport));
        var step2 = GetPng(renderer.Render(MakeStyledLayer(SchemeA, new float[,] { { 50f, 1f }, { 25f, 5f } }), DefaultViewport));

        Assert.Equal(1, renderer.LayoutCacheMisses);
        Assert.Equal(1, renderer.LayoutCacheHits);
        Assert.NotEqual(step1, step2);
    }

    #region Helpers

    private static readonly CoverageColorScheme SchemeA = new()
    {
        FieldName = "depth",
        Bands =
        [
            new ColorBand { MinValue = 0f, MaxValue = 3f, Color = "#ADE3FF" },
            new ColorBand { MinValue = 3f, MaxValue = 10f, Color = "#6BC5FF" },
            new ColorBand { MinValue = 10f, MaxValue = 30f, Color = "#2196F3" },
            new ColorBand { MinValue = 30f, MaxValue = 100f, Color = "#0D47A1" },
        ],
    };

    private static readonly CoverageColorScheme SchemeB = new()
    {
        FieldName = "depth",
        Bands =
        [
            new ColorBand { MinValue = 0f, MaxValue = 3f, Color = "#FF0000" },
            new ColorBand { MinValue = 3f, MaxValue = 10f, Color = "#00FF00" },
            new ColorBand { MinValue = 10f, MaxValue = 30f, Color = "#0000FF" },
            new ColorBand { MinValue = 30f, MaxValue = 100f, Color = "#FFFF00" },
        ],
    };

    private static StyledCoverageLayer MakeStyledLayer(
        CoverageColorScheme scheme,
        float[,] depths,
        double originLat = 0.0)
    {
        var gridMeta = new GridMetadata
        {
            NumRows = depths.GetLength(0),
            NumColumns = depths.GetLength(1),
            OriginLatitude = originLat,
            OriginLongitude = 0,
            SpacingLatitudinal = 0.01,
            SpacingLongitudinal = 0.01,
        };

        return new()
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = gridMeta,
                Values = new Dictionary<string, float[]> { ["depth"] = Flatten(depths) },
            },
            ColorScheme = scheme,
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(gridMeta, "EPSG:4326"),
        };
    }

    private static float[] Flatten(float[,] src)
    {
        int rows = src.GetLength(0), cols = src.GetLength(1);
        var flat = new float[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                flat[r * cols + c] = src[r, c];
        return flat;
    }

    private static byte[] GetPng(ILayer layer)
    {
        var memoryLayer = Assert.IsType<MemoryLayer>(layer);
        var feature = memoryLayer.Features.OfType<RasterFeature>().First();
        return feature.Raster!.Data;
    }

    #endregion
}
