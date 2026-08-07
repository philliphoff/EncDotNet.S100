using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="S100DatasetExtentIndicatorLayer"/>, the reusable
/// Mapsui overlay that outlines out-of-scale dataset extents. No Avalonia / UI
/// thread is involved.
/// </summary>
public class S100DatasetExtentIndicatorLayerTests
{
    private static IEnumerable<IFeature> Features(ILayer layer) =>
        ((MemoryLayer)layer).Features ?? Enumerable.Empty<IFeature>();

    private static VectorStyle? StyleOf(IFeature feature) =>
        feature.Styles.OfType<VectorStyle>().FirstOrDefault();

    [Fact]
    public void Layer_StartsEmptyAndNamed()
    {
        var overlay = new S100DatasetExtentIndicatorLayer();

        Assert.Equal(S100DatasetExtentIndicatorLayer.DefaultLayerName, overlay.Layer.Name);
        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Constructor_HonoursCustomName()
    {
        var overlay = new S100DatasetExtentIndicatorLayer(name: "My Extents");

        Assert.Equal("My Extents", overlay.Layer.Name);
    }

    [Fact]
    public void Show_WithNoIndicators_ClearsFeatures()
    {
        var overlay = new S100DatasetExtentIndicatorLayer();

        overlay.Show(new List<S100DatasetExtentIndicator>());

        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Show_ProducesOneOutlineFeaturePerIndicator()
    {
        var overlay = new S100DatasetExtentIndicatorLayer();

        overlay.Show(new List<S100DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 100, 100), MinVisibleResolution: 10.0),
            new(new MRect(200, 200, 300, 300), MinVisibleResolution: 25.0),
        });

        Assert.Equal(2, Features(overlay.Layer).Count());
    }

    [Fact]
    public void Show_SetsBorderMinVisibleToTheContentCutoff()
    {
        var overlay = new S100DatasetExtentIndicatorLayer();

        overlay.Show(new List<S100DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 100, 100), MinVisibleResolution: 42.5),
        });

        var style = StyleOf(Features(overlay.Layer).Single());
        Assert.NotNull(style);
        // The border must only appear once zoomed out past the dataset's content
        // cutoff, i.e. MinVisible == the indicator's cutoff resolution.
        Assert.Equal(42.5, style!.MinVisible);
    }

    [Fact]
    public void Show_DrawsAnUnfilledAccentOutline_FromStyle()
    {
        var style = new S100DatasetExtentIndicatorStyle { Accent = (0x12, 0x34, 0x56) };
        var overlay = new S100DatasetExtentIndicatorLayer(style);

        overlay.Show(new List<S100DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 10, 10), MinVisibleResolution: 1.0),
        });

        var vector = StyleOf(Features(overlay.Layer).Single());
        Assert.NotNull(vector);
        Assert.Null(vector!.Fill);
        Assert.NotNull(vector.Outline);
        Assert.Equal(0x12, vector.Outline!.Color.R);
        Assert.Equal(0x34, vector.Outline.Color.G);
        Assert.Equal(0x56, vector.Outline.Color.B);
    }

    [Fact]
    public void Show_AccentOverride_WinsOverStyleAccent()
    {
        // Mirrors the Viewer re-theming the overlay at runtime without rebuilding.
        var overlay = new S100DatasetExtentIndicatorLayer(
            new S100DatasetExtentIndicatorStyle { Accent = (0x00, 0x00, 0x00) });

        overlay.Show(
            new List<S100DatasetExtentIndicator>
            {
                new(new MRect(0, 0, 10, 10), MinVisibleResolution: 1.0),
            },
            accent: (0xAB, 0xCD, 0xEF));

        var outline = StyleOf(Features(overlay.Layer).Single())!.Outline!;
        Assert.Equal(0xAB, outline.Color.R);
        Assert.Equal(0xCD, outline.Color.G);
        Assert.Equal(0xEF, outline.Color.B);
    }

    [Fact]
    public void Show_UsesDashedHairlineFromStyle()
    {
        var style = new S100DatasetExtentIndicatorStyle
        {
            OutlineWidth = 3.0,
            OutlineOpacity = 0.25f,
            DashArray = new[] { 2.0f, 4.0f },
        };
        var overlay = new S100DatasetExtentIndicatorLayer(style);

        overlay.Show(new List<S100DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 10, 10), MinVisibleResolution: 0.0),
        });

        var vector = StyleOf(Features(overlay.Layer).Single())!;
        Assert.Equal(0.25f, vector.Opacity);
        Assert.Equal(3.0, vector.Outline!.Width);
        Assert.Equal(PenStyle.UserDefined, vector.Outline.PenStyle);
        Assert.Equal(PenStrokeCap.Round, vector.Outline.PenStrokeCap);
        Assert.Equal(new[] { 2.0f, 4.0f }, vector.Outline.DashArray);
    }

    [Fact]
    public void Show_MinVisibleZero_AlwaysVisibleBorder()
    {
        // A catalogue footprint for a not-yet-loaded cell is outlined at every
        // zoom (MinVisible 0), unlike a loaded dataset's out-of-scale cutoff.
        var overlay = new S100DatasetExtentIndicatorLayer();

        overlay.Show(new List<S100DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 10, 10), MinVisibleResolution: 0.0),
        });

        Assert.Equal(0.0, StyleOf(Features(overlay.Layer).Single())!.MinVisible);
    }

    [Fact]
    public void Clear_EmptiesAPopulatedLayer()
    {
        var overlay = new S100DatasetExtentIndicatorLayer();
        overlay.Show(new List<S100DatasetExtentIndicator>
        {
            new(new MRect(0, 0, 10, 10), MinVisibleResolution: 1.0),
        });

        overlay.Clear();

        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Style_DashArray_IsDefensivelyCopied()
    {
        // Mutating the array returned by the shared Default singleton must not
        // change the default (nor any later-built pen).
        var returned = S100DatasetExtentIndicatorStyle.Default.DashArray;
        returned[0] = 999f;

        Assert.NotEqual(999f, S100DatasetExtentIndicatorStyle.Default.DashArray[0]);
    }

    [Fact]
    public void Show_NullIndicators_Throws()
    {
        var overlay = new S100DatasetExtentIndicatorLayer();

        Assert.Throws<ArgumentNullException>(() => overlay.Show(null!));
    }
}
