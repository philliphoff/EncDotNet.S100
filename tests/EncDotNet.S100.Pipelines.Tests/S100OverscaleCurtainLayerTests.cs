using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="S100OverscaleCurtainLayer"/>, the reusable Mapsui
/// overlay that paints the on-chart overscale curtain. No Avalonia / UI thread is
/// involved; the region geometry itself is covered by
/// <see cref="OverscaleCurtainTests"/>.
/// </summary>
public class S100OverscaleCurtainLayerTests
{
    private static readonly GeometryFactory Gf = new();

    private static IEnumerable<IFeature> Features(ILayer layer) =>
        ((MemoryLayer)layer).Features ?? Enumerable.Empty<IFeature>();

    private static Polygon Rect(double minX, double minY, double maxX, double maxY) =>
        Gf.CreatePolygon(Gf.CreateLinearRing(
        [
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ]));

    private static OverscaleRegion Region(string name, double factor, Geometry geometry) =>
        new(name, factor, geometry);

    [Fact]
    public void Layer_StartsEmptyAndNamed()
    {
        var overlay = new S100OverscaleCurtainLayer();

        Assert.Equal(S100OverscaleCurtainLayer.DefaultLayerName, overlay.Layer.Name);
        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Constructor_HonoursCustomName()
    {
        var overlay = new S100OverscaleCurtainLayer(name: "My Curtain");

        Assert.Equal("My Curtain", overlay.Layer.Name);
    }

    [Fact]
    public void Show_WithNoRegions_ClearsFeatures()
    {
        var overlay = new S100OverscaleCurtainLayer();

        overlay.Show(new List<OverscaleRegion>());

        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Show_ProducesOneCurtainFeaturePerRegion()
    {
        var overlay = new S100OverscaleCurtainLayer();

        overlay.Show(new[]
        {
            Region("Coastal", 2.0, Rect(0, 0, 100, 100)),
            Region("Harbour", 4.0, Rect(200, 200, 300, 300)),
        });

        Assert.Equal(2, Features(overlay.Layer).Count());
    }

    [Fact]
    public void Show_CarriesRegionGeometryAndCurtainStyle()
    {
        var overlay = new S100OverscaleCurtainLayer();
        var geometry = Rect(10, 20, 30, 40);

        overlay.Show(new[] { Region("Coastal", 2.0, geometry) });

        var feature = Assert.IsType<GeometryFeature>(Features(overlay.Layer).Single());
        Assert.Same(geometry, feature.Geometry);
        Assert.Single(feature.Styles.OfType<OverscaleCurtainStyle>());
    }

    [Fact]
    public void Show_SkipsNullOrEmptyRegionGeometry()
    {
        var overlay = new S100OverscaleCurtainLayer();

        overlay.Show(new[]
        {
            Region("Empty", 2.0, Gf.CreatePolygon()),   // empty geometry
            new OverscaleRegion("Null", 3.0, null!),    // absent geometry
            Region("Coastal", 4.0, Rect(0, 0, 10, 10)),
        });

        // Only the non-empty, non-null region becomes a feature.
        Assert.Single(Features(overlay.Layer));
    }

    [Fact]
    public void Show_SharesTheStyleInstanceAcrossFeatures()
    {
        // The renderer only reads the style, so one instance themes the whole
        // overlay — verify all features reference the same style object.
        var style = new OverscaleCurtainStyle { LineSpacingMm = 5.0 };
        var overlay = new S100OverscaleCurtainLayer(style);

        overlay.Show(new[]
        {
            Region("A", 2.0, Rect(0, 0, 10, 10)),
            Region("B", 3.0, Rect(20, 20, 30, 30)),
        });

        var styles = Features(overlay.Layer)
            .Select(f => f.Styles.OfType<OverscaleCurtainStyle>().Single())
            .ToList();
        Assert.All(styles, s => Assert.Same(style, s));
    }

    [Fact]
    public void Show_ReplacesPreviousContents()
    {
        var overlay = new S100OverscaleCurtainLayer();
        overlay.Show(new[]
        {
            Region("A", 2.0, Rect(0, 0, 10, 10)),
            Region("B", 3.0, Rect(20, 20, 30, 30)),
        });

        overlay.Show(new[] { Region("C", 4.0, Rect(40, 40, 50, 50)) });

        Assert.Single(Features(overlay.Layer));
    }

    [Fact]
    public void Clear_EmptiesAPopulatedLayer()
    {
        var overlay = new S100OverscaleCurtainLayer();
        overlay.Show(new[] { Region("A", 2.0, Rect(0, 0, 10, 10)) });

        overlay.Clear();

        Assert.Empty(Features(overlay.Layer));
    }

    [Fact]
    public void Show_NullRegions_Throws()
    {
        var overlay = new S100OverscaleCurtainLayer();

        Assert.Throws<ArgumentNullException>(() => overlay.Show(null!));
    }
}
