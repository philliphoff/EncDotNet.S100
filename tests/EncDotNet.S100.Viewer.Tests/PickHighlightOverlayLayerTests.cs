using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Tools;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;

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
            new PickHighlightState(new GeoPosition(47.6, -122.3), Geometry: null),
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
            new PickHighlightState(new GeoPosition(47.6, -122.3), Geometry: null),
            Appearance);

        // Casing ring + accent ring (no centre dot).
        Assert.Equal(2, FeatureCount(layer));
    }

    [Fact]
    public void Update_AreaGeometry_DrawsFillOutlineAndMarker()
    {
        var layer = PickHighlightOverlayLayer.Create();

        var exterior = new List<GeoPosition>
        {
            new GeoPosition(47.0, -122.0),
            new GeoPosition(47.0, -121.0),
            new GeoPosition(48.0, -121.0),
            new GeoPosition(48.0, -122.0),
        };

        var geometry = new PickHighlightGeometry(
            ExteriorRing: exterior,
            InteriorRings: new List<IReadOnlyList<GeoPosition>>(),
            Curves: new List<IReadOnlyList<GeoPosition>>(),
            Points: new List<GeoPosition>());

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState(new GeoPosition(47.5, -121.5), geometry),
            Appearance);

        // Area fill (1) + exterior ring outline (1) + marker pair (2).
        Assert.Equal(4, FeatureCount(layer));
    }

    [Fact]
    public void Update_CurveGeometry_DrawsOnePolylinePerCurve()
    {
        var layer = PickHighlightOverlayLayer.Create();

        var curve = new List<GeoPosition> { new GeoPosition(47.0, -122.0), new GeoPosition(47.5, -121.5), new GeoPosition(48.0, -121.0) };
        var geometry = new PickHighlightGeometry(
            ExteriorRing: new List<GeoPosition>(),
            InteriorRings: new List<IReadOnlyList<GeoPosition>>(),
            Curves: new List<IReadOnlyList<GeoPosition>> { curve },
            Points: new List<GeoPosition>());

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
            ExteriorRing: new List<GeoPosition>(),
            InteriorRings: new List<IReadOnlyList<GeoPosition>>(),
            Curves: new List<IReadOnlyList<GeoPosition>>(),
            Points: new List<GeoPosition> { new GeoPosition(47.0, -122.0), new GeoPosition(48.0, -121.0) });

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState(Location: null, geometry),
            Appearance);

        // One ring per point.
        Assert.Equal(2, FeatureCount(layer));
    }

    [Theory]
    [InlineData(false, 255, 255, 255)] // Day basemap: bright white casing.
    [InlineData(true, 150, 150, 150)]  // Dusk/Night basemap: dimmed casing to avoid glare.
    public void Update_MarkerCasing_DimsForDarkBasemap(bool darkBasemap, byte r, byte g, byte b)
    {
        var layer = PickHighlightOverlayLayer.Create();
        var appearance = new PickHighlightAppearance((0, 122, 204), darkBasemap);

        PickHighlightOverlayLayer.Update(
            layer,
            new PickHighlightState(new GeoPosition(47.6, -122.3), Geometry: null),
            appearance);

        // The casing ring is the first marker feature (drawn under the accent
        // ring). Its outline colour is what changes with the basemap.
        var casing = layer.Features!.First();
        var style = (SymbolStyle)((GeometryFeature)casing).Styles.First();
        var color = style.Outline!.Color!;

        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }
}
