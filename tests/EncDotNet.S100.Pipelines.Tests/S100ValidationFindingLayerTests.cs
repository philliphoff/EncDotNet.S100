using EncDotNet.S100.DataModel;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Validation;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="S100ValidationFindingLayer"/>, the reusable Mapsui
/// overlay that plots a dataset's spatially-located validation findings. No
/// Avalonia / UI thread is involved.
/// </summary>
public class S100ValidationFindingLayerTests
{
    private static IEnumerable<IFeature> Features(ILayer layer) =>
        ((MemoryLayer)layer).Features ?? Enumerable.Empty<IFeature>();

    private static SymbolStyle? SymbolOf(IFeature feature) =>
        feature.Styles.OfType<SymbolStyle>().FirstOrDefault();

    // SymbolStyle derives from VectorStyle in Mapsui, so exclude it here to match
    // only the bounding-box outline (a plain VectorStyle), never a point marker.
    private static VectorStyle? VectorOf(IFeature feature) =>
        feature.Styles.OfType<VectorStyle>().FirstOrDefault(s => s is not SymbolStyle);

    private static S100ValidationFinding Point(
        ValidationSeverity severity = ValidationSeverity.Info,
        double lat = 40, double lon = -70) =>
        new(severity, new GeoPosition(lat, lon), BoundingBox: null);

    private static S100ValidationFinding Box(
        ValidationSeverity severity = ValidationSeverity.Info) =>
        new(severity, Point: null, new BoundingBox(10, 20, 30, 40));

    [Fact]
    public void Layer_StartsEmptyAndNamed()
    {
        var overlay = new S100ValidationFindingLayer();

        Assert.Equal(S100ValidationFindingLayer.DefaultLayerName, overlay.Layer.Name);
        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Constructor_HonoursCustomName()
    {
        var overlay = new S100ValidationFindingLayer(name: "My Findings");

        Assert.Equal("My Findings", overlay.Layer.Name);
    }

    [Fact]
    public void Show_WithNoFindings_ClearsFeatures()
    {
        var overlay = new S100ValidationFindingLayer();

        overlay.Show(new List<S100ValidationFinding>());

        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Show_PointFinding_ProducesFilledMarkerWithHalo()
    {
        var style = new S100ValidationFindingStyle
        {
            InfoColor = (0x11, 0x22, 0x33),
            HaloColor = (0xFF, 0xFF, 0xFF),
        };
        var overlay = new S100ValidationFindingLayer(style);

        overlay.Show(new[] { Point(ValidationSeverity.Info) });

        var symbol = SymbolOf(Features(overlay.Layer).Single());
        Assert.NotNull(symbol);
        Assert.Equal(SymbolType.Ellipse, symbol!.SymbolType);
        Assert.Equal((0x11, 0x22, 0x33), Rgb(symbol.Fill!.Color));
        Assert.Equal((0xFF, 0xFF, 0xFF), Rgb(symbol.Outline!.Color));
    }

    [Fact]
    public void Show_BoundingBoxFinding_ProducesTranslucentFillAndOpaqueOutline()
    {
        var style = new S100ValidationFindingStyle
        {
            ErrorColor = (0xD1, 0x34, 0x38),
            BoundingBoxFillAlpha = 64,
        };
        var overlay = new S100ValidationFindingLayer(style);

        overlay.Show(new[] { Box(ValidationSeverity.Error) });

        var vector = VectorOf(Features(overlay.Layer).Single());
        Assert.NotNull(vector);
        // Opaque severity-coloured outline.
        Assert.Equal((0xD1, 0x34, 0x38, 255), Rgba(vector!.Outline!.Color));
        // Translucent severity-coloured fill (same RGB, style alpha).
        Assert.Equal((0xD1, 0x34, 0x38, 64), Rgba(vector.Fill!.Color));
    }

    [Fact]
    public void Show_FindingWithBothPointAndBox_ProducesTwoFeatures()
    {
        var overlay = new S100ValidationFindingLayer();

        overlay.Show(new[]
        {
            new S100ValidationFinding(
                ValidationSeverity.Warning,
                new GeoPosition(45, 25),
                new BoundingBox(10, 20, 30, 40)),
        });

        var features = Features(overlay.Layer).ToList();
        Assert.Equal(2, features.Count);
        Assert.Single(features, f => SymbolOf(f) is not null);
        Assert.Single(features, f => VectorOf(f) is not null);
    }

    [Fact]
    public void Show_FindingWithNoSpatialInfo_IsSkipped()
    {
        var overlay = new S100ValidationFindingLayer();

        overlay.Show(new[]
        {
            new S100ValidationFinding(ValidationSeverity.Info, Point: null, BoundingBox: null),
            Point(ValidationSeverity.Info),
        });

        Assert.Single(Features(overlay.Layer));
    }

    [Fact]
    public void Show_MapsSeveritiesToStylePalette()
    {
        var style = new S100ValidationFindingStyle
        {
            ErrorColor = (1, 2, 3),
            WarningColor = (4, 5, 6),
            InfoColor = (7, 8, 9),
        };
        var overlay = new S100ValidationFindingLayer(style);

        overlay.Show(new[]
        {
            Point(ValidationSeverity.Error),
            Point(ValidationSeverity.Warning),
            Point(ValidationSeverity.Info),
        });

        var colors = Features(overlay.Layer)
            .Select(f => Rgb(SymbolOf(f)!.Fill!.Color))
            .ToList();
        Assert.Equal(new[] { (1, 2, 3), (4, 5, 6), (7, 8, 9) }, colors);
    }

    [Fact]
    public void Clear_EmptiesAPopulatedLayer()
    {
        var overlay = new S100ValidationFindingLayer();
        overlay.Show(new[] { Point() });

        overlay.Clear();

        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Show_ReplacesPreviousFeatures()
    {
        var overlay = new S100ValidationFindingLayer();
        overlay.Show(new[] { Point(), Box() });
        Assert.Equal(2, Features(overlay.Layer).Count());

        overlay.Show(new[] { Point() });
        Assert.Single(Features(overlay.Layer));
    }

    [Fact]
    public void Show_NullFindings_Throws()
    {
        var overlay = new S100ValidationFindingLayer();

        Assert.Throws<ArgumentNullException>(() => overlay.Show(null!));
    }

    [Fact]
    public void Style_DefaultsMatchViewerBadgePalette()
    {
        var style = S100ValidationFindingStyle.Default;

        Assert.Equal((0xD1, 0x34, 0x38), Rgb(style.SeverityColor(ValidationSeverity.Error)));
        Assert.Equal((0xCA, 0x50, 0x10), Rgb(style.SeverityColor(ValidationSeverity.Warning)));
        Assert.Equal((0x00, 0x7A, 0xCC), Rgb(style.SeverityColor(ValidationSeverity.Info)));
    }

    private static (int R, int G, int B) Rgb(Color? c) => (c!.Value.R, c.Value.G, c.Value.B);

    private static (int R, int G, int B) Rgb((byte R, byte G, byte B) c) => (c.R, c.G, c.B);

    private static (int R, int G, int B, int A) Rgba(Color? c) => (c!.Value.R, c.Value.G, c.Value.B, c.Value.A);
}
