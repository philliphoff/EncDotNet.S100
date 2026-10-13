using EncDotNet.S100.Viewer.Routing;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Builds the persistent Mapsui <see cref="MemoryLayer"/> overlay for the
/// editable route collection. Unlike <see cref="MeasureOverlayLayer"/> (a
/// transient ruler shown only while its tool is active) this overlay
/// reflects every <see cref="Route"/> in the collection at all times; the
/// active route and the selection are emphasised, and the route being
/// edited gets the editing style.
/// </summary>
/// <remarks>
/// The layer holds one feature styled with a <see cref="RouteOverlayStyle"/>.
/// <see cref="Update"/> replaces the style's <see cref="RouteOverlayScene"/>
/// snapshot when routes, the selection, edit mode or the palette change, and
/// <see cref="RouteOverlayRenderer"/> lays the scene out for the viewport on
/// every frame, so labels and insert handles follow the zoom without a rebuild.
/// </remarks>
internal static class RouteOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Route Overlay";

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create()
    {
        RouteOverlayRenderer.Register();

        // The renderer draws the whole scene in screen space; the feature only
        // has to intersect every viewport so Mapsui always asks it to draw.
        var (minX, minY) = SphericalMercator.FromLonLat(-180, -85.06);
        var (maxX, maxY) = SphericalMercator.FromLonLat(180, 85.06);
        var world = new GeometryFeature(new GeometryFactory().ToGeometry(new Envelope(minX * 3, maxX * 3, minY, maxY)));
        world.Styles.Add(new RouteOverlayStyle());

        return new MemoryLayer
        {
            Name = LayerName,
            Style = null,
            Features = new List<IFeature> { world },
        };
    }

    /// <summary>The scene <paramref name="layer"/> is drawing.</summary>
    public static RouteOverlayScene SceneOf(MemoryLayer layer)
        => StyleOf(layer)?.Scene ?? RouteOverlayScene.Empty;

    /// <summary>
    /// Replaces <paramref name="layer"/>'s scene with a snapshot of
    /// <paramref name="routes"/>. Caller invalidates the map afterwards.
    /// </summary>
    public static void Update(
        MemoryLayer layer,
        RouteCollection routes,
        int? selectedWaypointIndex,
        int? selectedLegIndex,
        bool isEditing,
        MeasureOverlayAppearance appearance,
        RouteCheckOverlay? check = null)
    {
        if (StyleOf(layer) is not { } style)
            return;
        style.Scene = RouteOverlayScene.Build(routes, selectedWaypointIndex, selectedLegIndex, isEditing, appearance, check);
        layer.DataHasChanged();
    }

    private static RouteOverlayStyle? StyleOf(MemoryLayer layer)
        => layer.Features.FirstOrDefault()?.Styles.OfType<RouteOverlayStyle>().FirstOrDefault();
}
