using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// An area portrayed only by a pattern fill is a pick target like any other
/// painted area (issue #604): a click inside it resolves its feature reference.
/// </summary>
public class MapsuiPatternAreaPickTests
{
    private const string Pattern = "DIAMOND1";

    // A 1° square pattern-only area, centred on (0, 0).
    private sealed class SquareProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => new()
        {
            Type = GeometryType.Surface,
            Coordinates =
            [
                new GeoPosition(-0.5, -0.5),
                new GeoPosition(-0.5, 0.5),
                new GeoPosition(0.5, 0.5),
                new GeoPosition(0.5, -0.5),
                new GeoPosition(-0.5, -0.5),
            ],
        };
    }

    private static ILayer Render() => new MapsuiDisplayListRenderer
    {
        Palette = ColorPalette.Default,
        SymbolProvider = static _ =>
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="2" fill="black"/></svg>""",
        AreaFillProvider = static name => new AreaFill
        {
            Name = name,
            PatternSymbol = name,
            V1X = 4.0,
            V2Y = 4.0,
        },
    }.Render(
        [new AreaInstruction { FeatureReference = "P1", AreaFillReference = Pattern }],
        new SquareProvider());

    [Fact]
    public void PatternOnlyArea_HasAPickTarget()
    {
        var layer = Assert.IsAssignableFrom<MemoryLayer>(Render());

        var feature = Assert.Single(layer.Features);
        Assert.Equal("P1", feature[MapsuiDisplayListRenderer.FeatureRefKey]);
        var style = Assert.IsType<VectorStyle>(Assert.Single(feature.Styles));
        Assert.True(style.Fill?.Color?.A > 0, "Mapsui's pixel hit test ignores fully transparent fills");
    }

    [Fact]
    public void ClickInsidePatternOnlyArea_ResolvesTheFeature()
    {
        var layer = Render();
        // 1° spans about 111 km; 1 km per pixel puts the square well inside a
        // 256 px view centred on it.
        var viewport = new Mapsui.Viewport(0, 0, 1000, 0, 256, 256);

        var mapInfo = new MapRenderer().GetMapInfo(
            new ScreenPosition(128, 128), viewport, [layer], new RenderService(), margin: 0);

        var record = Assert.Single(mapInfo.MapInfoRecords);
        Assert.Equal("P1", record.Feature[MapsuiDisplayListRenderer.FeatureRefKey]);
    }
}
