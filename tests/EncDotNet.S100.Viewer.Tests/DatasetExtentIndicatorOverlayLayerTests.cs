using EncDotNet.S100.Viewer.Tools;
using Mapsui;
using Mapsui.Styles;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for <see cref="DatasetExtentIndicatorOverlayLayer"/>, the pure
/// Mapsui overlay builder for the out-of-scale dataset extent borders
/// (issue #446). No Avalonia / UI thread is involved.
/// </summary>
public class DatasetExtentIndicatorOverlayLayerTests
{
    private static VectorStyle? StyleOf(IFeature feature) =>
        feature.Styles.OfType<VectorStyle>().FirstOrDefault();

    [Fact]
    public void Update_WithNoIndicators_ClearsFeatures()
    {
        var layer = DatasetExtentIndicatorOverlayLayer.Create();

        DatasetExtentIndicatorOverlayLayer.Update(
            layer,
            new List<DatasetExtentIndicator>(),
            DatasetExtentIndicatorOverlayLayer.DefaultAccent);

        Assert.Empty(layer.Features);
    }

    [Fact]
    public void Update_ProducesOneOutlineFeaturePerIndicator()
    {
        var layer = DatasetExtentIndicatorOverlayLayer.Create();
        var indicators = new List<DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 100, 100), MinVisibleResolution: 10.0),
            new(new MRect(200, 200, 300, 300), MinVisibleResolution: 25.0),
        };

        DatasetExtentIndicatorOverlayLayer.Update(
            layer, indicators, DatasetExtentIndicatorOverlayLayer.DefaultAccent);

        Assert.Equal(2, layer.Features.Count());
    }

    [Fact]
    public void Update_SetsBorderMinVisibleToTheContentCutoff()
    {
        var layer = DatasetExtentIndicatorOverlayLayer.Create();
        var indicators = new List<DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 100, 100), MinVisibleResolution: 42.5),
        };

        DatasetExtentIndicatorOverlayLayer.Update(
            layer, indicators, DatasetExtentIndicatorOverlayLayer.DefaultAccent);

        var style = StyleOf(layer.Features.Single());
        Assert.NotNull(style);
        // The border must only appear once zoomed out past the dataset's
        // content cutoff, i.e. MinVisible == the indicator's cutoff resolution.
        Assert.Equal(42.5, style!.MinVisible);
    }

    [Fact]
    public void Update_DrawsAnUnfilledAccentOutline()
    {
        var layer = DatasetExtentIndicatorOverlayLayer.Create();
        var accent = ((byte)0x12, (byte)0x34, (byte)0x56);
        var indicators = new List<DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 10, 10), MinVisibleResolution: 1.0),
        };

        DatasetExtentIndicatorOverlayLayer.Update(layer, indicators, accent);

        var style = StyleOf(layer.Features.Single());
        Assert.NotNull(style);
        Assert.Null(style!.Fill);
        Assert.NotNull(style.Outline);
        Assert.Equal(0x12, style.Outline!.Color.R);
        Assert.Equal(0x34, style.Outline.Color.G);
        Assert.Equal(0x56, style.Outline.Color.B);
    }
}
