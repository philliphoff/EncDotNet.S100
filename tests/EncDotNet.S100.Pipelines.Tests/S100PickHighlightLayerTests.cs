using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests;

public class S100PickHighlightLayerTests
{
    private static int FeatureCount(ILayer layer) =>
        ((MemoryLayer)layer).Features?.Count() ?? 0;

    private static S100FeatureGeometry Area(params GeoPosition[] exterior) =>
        new() { Primitive = S100GeometryType.Surface, ExteriorRing = exterior };

    private static S100Pick Pick(S100FeatureGeometry? geometry, bool isCoverage = false) =>
        new()
        {
            DatasetId = new MapDatasetId("ds"),
            Info = new FeatureInfo { FeatureRef = "1", FeatureType = "T", Attributes = [] },
            Geometry = geometry,
            IsCoverage = isCoverage,
            Inside = true,
            DistanceMeters = 0,
        };

    [Fact]
    public void Layer_StartsEmptyAndNamed()
    {
        var highlight = new S100PickHighlightLayer();

        Assert.Equal(S100PickHighlightLayer.DefaultLayerName, highlight.Layer.Name);
        Assert.Equal(0, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Constructor_HonoursCustomName()
    {
        var highlight = new S100PickHighlightLayer(name: "My Highlight");

        Assert.Equal("My Highlight", highlight.Layer.Name);
    }

    [Fact]
    public void Show_AreaGeometry_DrawsFillAndOutline()
    {
        var highlight = new S100PickHighlightLayer();

        highlight.Show(Area(
            new GeoPosition(47.0, -122.0),
            new GeoPosition(47.0, -121.0),
            new GeoPosition(48.0, -121.0),
            new GeoPosition(48.0, -122.0)));

        // Faint area fill (1) + exterior ring outline (1). No cursor marker —
        // the reusable layer draws only the feature outline.
        Assert.Equal(2, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_AreaWithHole_OutlinesExteriorAndHole()
    {
        var highlight = new S100PickHighlightLayer();

        var geometry = new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Surface,
            ExteriorRing =
            [
                new GeoPosition(47.0, -122.0),
                new GeoPosition(47.0, -121.0),
                new GeoPosition(48.0, -121.0),
                new GeoPosition(48.0, -122.0),
            ],
            InteriorRings =
            [
                [
                    new GeoPosition(47.4, -121.6),
                    new GeoPosition(47.4, -121.4),
                    new GeoPosition(47.6, -121.4),
                ],
            ],
        };

        highlight.Show(geometry);

        // Fill (1) + exterior outline (1) + hole outline (1).
        Assert.Equal(3, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_CurveGeometry_DrawsOnePolylinePerCurve()
    {
        var highlight = new S100PickHighlightLayer();

        var geometry = new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Curve,
            Curves =
            [
                [new GeoPosition(47.0, -122.0), new GeoPosition(47.5, -121.5), new GeoPosition(48.0, -121.0)],
                [new GeoPosition(40.0, -70.0), new GeoPosition(41.0, -71.0)],
            ],
        };

        highlight.Show(geometry);

        Assert.Equal(2, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_PointGeometry_DrawsRingPerPoint()
    {
        var highlight = new S100PickHighlightLayer();

        var geometry = new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Point,
            Points = [new GeoPosition(47.0, -122.0), new GeoPosition(48.0, -121.0)],
        };

        highlight.Show(geometry);

        Assert.Equal(2, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_Pick_OutlinesItsGeometry()
    {
        var highlight = new S100PickHighlightLayer();

        highlight.Show(Pick(Area(
            new GeoPosition(0, 0),
            new GeoPosition(0, 1),
            new GeoPosition(1, 1),
            new GeoPosition(1, 0))));

        Assert.Equal(2, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_NullPick_Clears()
    {
        var highlight = new S100PickHighlightLayer();
        highlight.Show(Area(
            new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1)));
        Assert.True(FeatureCount(highlight.Layer) > 0);

        highlight.Show((S100Pick?)null);

        Assert.Equal(0, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_CoveragePick_Clears()
    {
        var highlight = new S100PickHighlightLayer();
        highlight.Show(Area(
            new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1)));

        // A coverage pick carries no geometry, so it clears the highlight.
        highlight.Show(Pick(geometry: null, isCoverage: true));

        Assert.Equal(0, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_MultiplePicks_OutlinesEach()
    {
        var highlight = new S100PickHighlightLayer();

        var picks = new[]
        {
            Pick(new S100FeatureGeometry
            {
                Primitive = S100GeometryType.Point,
                Points = [new GeoPosition(1, 1)],
            }),
            Pick(geometry: null, isCoverage: true), // contributes nothing
            Pick(new S100FeatureGeometry
            {
                Primitive = S100GeometryType.Point,
                Points = [new GeoPosition(2, 2)],
            }),
        };

        highlight.Show(picks);

        Assert.Equal(2, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Show_EmptyPicks_Clears()
    {
        var highlight = new S100PickHighlightLayer();
        highlight.Show(Area(
            new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1)));

        highlight.Show(Array.Empty<S100Pick>());

        Assert.Equal(0, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Clear_RemovesFeaturesButKeepsLayer()
    {
        var highlight = new S100PickHighlightLayer();
        highlight.Show(Area(
            new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1)));

        highlight.Clear();

        Assert.Equal(0, FeatureCount(highlight.Layer));
        Assert.Equal(S100PickHighlightLayer.DefaultLayerName, highlight.Layer.Name);
    }

    [Fact]
    public void Show_EmptyGeometry_DrawsNothing()
    {
        var highlight = new S100PickHighlightLayer();

        // A degenerate ring (< 3 vertices) and no other primitive is not drawable.
        highlight.Show(new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Surface,
            ExteriorRing = [new GeoPosition(0, 0), new GeoPosition(0, 1)],
        });

        Assert.Equal(0, FeatureCount(highlight.Layer));
    }

    [Fact]
    public void Style_AppliesConfiguredAccentToOutline()
    {
        var style = new S100PickHighlightStyle { Accent = (10, 20, 30) };
        var highlight = new S100PickHighlightLayer(style);

        highlight.Show(new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Curve,
            Curves = [[new GeoPosition(0, 0), new GeoPosition(1, 1)]],
        });

        var feature = (GeometryFeature)((MemoryLayer)highlight.Layer).Features!.First();
        var color = ((VectorStyle)feature.Styles.First()).Line!.Color!;

        Assert.Equal(10, color.R);
        Assert.Equal(20, color.G);
        Assert.Equal(30, color.B);
    }

    [Fact]
    public void Show_CurveCrossingAntimeridian_SplitsIntoSubPaths()
    {
        var highlight = new S100PickHighlightLayer();

        // A single curve running 178°E -> 179°E -> 179°W -> 178°W crosses the
        // antimeridian and must be drawn as two sub-paths, not one line wrapping
        // the globe.
        highlight.Show(new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Curve,
            Curves =
            [
                [
                    new GeoPosition(0, 178.0),
                    new GeoPosition(0, 179.0),
                    new GeoPosition(0, -179.0),
                    new GeoPosition(0, -178.0),
                ],
            ],
        });

        Assert.Equal(2, FeatureCount(highlight.Layer));
    }
}
