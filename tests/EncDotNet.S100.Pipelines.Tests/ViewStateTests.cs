using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using Mapsui.Extensions;
using MapsuiViewport = Mapsui.Viewport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The engine's <see cref="ViewState"/> must project exactly as Mapsui's
/// viewport does, or the composite and the coverage clips would move off the
/// pixels the host draws its other layers on (map rendering slice 7).
/// </summary>
public sealed class ViewStateTests
{
    public static TheoryData<double> Rotations => [0.0, 1e-17, 3.5, 45.0, -30.0, 90.0, 180.0, 359.0, 360.0, 725.0];

    [Theory]
    [MemberData(nameof(Rotations))]
    public void WorldToScreenXY_MatchesMapsui(double rotation)
    {
        var mapsui = new MapsuiViewport(-1_234_567.8, 6_543_210.9, 12.5, rotation, 1280, 720);
        var view = MapsuiChartLayers.ToViewState(mapsui);

        foreach (var (x, y) in new[] { (mapsui.CenterX, mapsui.CenterY), (-1_230_000.0, 6_540_000.0), (-1_250_000.0, 6_560_000.0) })
        {
            var expected = mapsui.WorldToScreenXY(x, y);
            var actual = view.WorldToScreenXY(x, y);
            Assert.Equal(expected.screenX, actual.ScreenX);
            Assert.Equal(expected.screenY, actual.ScreenY);
        }
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void ToExtent_MatchesMapsui(double rotation)
    {
        var mapsui = new MapsuiViewport(812_345.6, -4_321_000.1, 3.25, rotation, 1024, 768);
        var view = MapsuiChartLayers.ToViewState(mapsui);

        var expected = mapsui.ToExtent();
        var actual = view.ToExtent();
        Assert.Equal(expected.MinX, actual.MinX);
        Assert.Equal(expected.MinY, actual.MinY);
        Assert.Equal(expected.MaxX, actual.MaxX);
        Assert.Equal(expected.MaxY, actual.MaxY);
        Assert.Equal(mapsui.IsRotated(), view.IsRotated);
    }

    [Fact]
    public void HasSize_MatchesMapsui()
    {
        Assert.False(MapsuiChartLayers.ToViewState(new MapsuiViewport(0, 0, 1, 0, 0, 100)).HasSize);
        Assert.False(MapsuiChartLayers.ToViewState(new MapsuiViewport(0, 0, 1, 0, 100, 0)).HasSize);
        Assert.True(MapsuiChartLayers.ToViewState(new MapsuiViewport(0, 0, 1, 0, 100, 100)).HasSize);
    }

    [Fact]
    public void ToFrame_CarriesTheLayerAsOwner()
    {
        var layer = new Mapsui.Layers.MemoryLayer("cell") { Opacity = 0.5 };

        var frame = MapsuiChartLayers.ToFrame(layer);

        Assert.Same(layer, frame.Owner);
        Assert.Equal("cell", frame.Name);
        Assert.Equal(0.5, frame.Opacity);
    }
}
