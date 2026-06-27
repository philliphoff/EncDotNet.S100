using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Viewer.Tools;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class PickHighlightOverlayLayerTests
{
    private static readonly PickHighlightAppearance Appearance = PickHighlightAppearance.Default;

    private static int FeatureCount(MemoryLayer layer)
        => layer.Features?.Count() ?? 0;

    [Fact]
    public void Create_StartsEmpty()
    {
        var layer = PickHighlightOverlayLayer.Create();

        Assert.Equal(PickHighlightOverlayLayer.LayerName, layer.Name);
        Assert.Equal(0, FeatureCount(layer));
    }

    [Fact]
    public void Update_EmptyState_ClearsFeatures()
    {
        var layer = PickHighlightOverlayLayer.Create();

        // Seed with a marker, then clear with an empty state.
        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState((47.6, -122.3), Geometry: null),
            Appearance);
        Assert.True(FeatureCount(layer) > 0);

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState(Location: null, Geometry: null),
            Appearance);

        Assert.Equal(0, FeatureCount(layer));
    }

    [Fact]
    public void Update_LocationOnly_DrawsMarkerTriplet()
    {
        var layer = PickHighlightOverlayLayer.Create();

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState((47.6, -122.3), Geometry: null),
            Appearance);

        // Halo ring + accent ring + centre dot.
        Assert.Equal(3, FeatureCount(layer));
    }

    [Fact]
    public void Update_AreaGeometry_DrawsFillOutlineAndMarker()
    {
        var layer = PickHighlightOverlayLayer.Create();

        var exterior = new List<(double Lat, double Lon)>
        {
            (47.0, -122.0),
            (47.0, -121.0),
            (48.0, -121.0),
            (48.0, -122.0),
        };

        var geometry = new PickHighlightGeometry(
            ExteriorRing: exterior,
            InteriorRings: new List<IReadOnlyList<(double, double)>>(),
            Curves: new List<IReadOnlyList<(double, double)>>(),
            Points: new List<(double, double)>());

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState((47.5, -121.5), geometry),
            Appearance);

        // Area fill (1) + exterior ring outline (1) + marker triplet (3).
        Assert.Equal(5, FeatureCount(layer));
    }

    [Fact]
    public void Update_CurveGeometry_DrawsOnePolylinePerCurve()
    {
        var layer = PickHighlightOverlayLayer.Create();

        var curve = new List<(double Lat, double Lon)> { (47.0, -122.0), (47.5, -121.5), (48.0, -121.0) };
        var geometry = new PickHighlightGeometry(
            ExteriorRing: new List<(double, double)>(),
            InteriorRings: new List<IReadOnlyList<(double, double)>>(),
            Curves: new List<IReadOnlyList<(double, double)>> { curve },
            Points: new List<(double, double)>());

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState(Location: null, geometry),
            Appearance);

        // One polyline, no marker (location is null).
        Assert.Equal(1, FeatureCount(layer));
    }

    [Fact]
    public void Update_PointGeometry_DrawsRingPerPoint()
    {
        var layer = PickHighlightOverlayLayer.Create();

        var geometry = new PickHighlightGeometry(
            ExteriorRing: new List<(double, double)>(),
            InteriorRings: new List<IReadOnlyList<(double, double)>>(),
            Curves: new List<IReadOnlyList<(double, double)>>(),
            Points: new List<(double, double)> { (47.0, -122.0), (48.0, -121.0) });

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState(Location: null, geometry),
            Appearance);

        // One ring per point.
        Assert.Equal(2, FeatureCount(layer));
    }
}
