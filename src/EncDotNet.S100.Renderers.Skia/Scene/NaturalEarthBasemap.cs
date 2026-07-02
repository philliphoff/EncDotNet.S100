using System.Text.Json;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// A single land polygon of the bundled Natural Earth basemap, expressed in
/// <b>EPSG:3857 Web-Mercator metres</b> (already projected from the source
/// WGS-84 GeoJSON via <see cref="WebMercator.FromLonLat"/>). The exterior shell
/// and any interior holes are consumed both by the vector/composite scene path
/// (as <see cref="AreaPaintOp"/>s) and by the coverage single-render path (drawn
/// directly through its centred-fit projection).
/// </summary>
/// <param name="WorldShell">Exterior ring in EPSG:3857 metres.</param>
/// <param name="WorldHoles">Interior (hole) rings in EPSG:3857 metres.</param>
public sealed record LandPolygon(
    IReadOnlyList<(double X, double Y)> WorldShell,
    IReadOnlyList<IReadOnlyList<(double X, double Y)>> WorldHoles);

/// <summary>
/// The bundled, offline, public-domain <b>Natural Earth 1:10m land</b> basemap
/// (issues #295, #411) as a headless, Mapsui-free source of land geometry.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of land geometry for the headless render paths:
/// the embedded GeoJSON is parsed once and each ring is projected
/// <c>lon/lat → EPSG:3857</c> with <see cref="WebMercator.FromLonLat"/> (which
/// matches the viewer's Mapsui <c>SphericalMercator</c> within a tight metre
/// tolerance). Both the resulting <see cref="LandPolygons"/> and the cached
/// <see cref="LandScene"/> are viewport-independent, so they can be drawn
/// against any chart viewport and register exactly.
/// </para>
/// <para>
/// The land is filled with a muted, parchment-like tone
/// (<see cref="LandFill"/> = <c>238,232,220</c>) — the same colour the viewer's
/// offline basemap uses — with no outline. Ops carry no scale-visibility limits
/// so the basemap draws at every fitted scale.
/// </para>
/// <para>
/// The whole-world land set is emitted without viewport pre-culling; Skia clips
/// off-screen geometry to the canvas. Viewport-clip pre-culling is a possible
/// future optimisation for very large output requests.
/// </para>
/// </remarks>
public static class NaturalEarthBasemap
{
    private const string LandGeoJsonResource =
        "EncDotNet.S100.Renderers.Skia.Assets.Basemap.ne_10m_land.geojson";

    /// <summary>
    /// Land fill — a muted, parchment-like tone drawn over the chart's water
    /// back-colour. Matches the interactive viewer's offline basemap fill.
    /// </summary>
    public static readonly RgbaColor LandFill = new(238, 232, 220);

    private static readonly Lazy<IReadOnlyList<LandPolygon>> LazyPolygons =
        new(LoadLandPolygons, isThreadSafe: true);

    private static readonly Lazy<VectorScene> LazyScene =
        new(BuildLandScene, isThreadSafe: true);

    /// <summary>
    /// The parsed land polygons in EPSG:3857 metres. Parsed once on first access
    /// and cached for the process lifetime.
    /// </summary>
    public static IReadOnlyList<LandPolygon> LandPolygons => LazyPolygons.Value;

    /// <summary>
    /// The land polygons lowered to a resolved <see cref="VectorScene"/> of
    /// parchment-filled <see cref="AreaPaintOp"/>s, suitable for drawing beneath
    /// chart layers via <c>SkiaDisplayListRenderer.RenderOnto</c> or a
    /// <c>VectorCompositeLayer</c>. Built once and cached.
    /// </summary>
    public static VectorScene LandScene => LazyScene.Value;

    private static VectorScene BuildLandScene()
    {
        var polygons = LazyPolygons.Value;
        var ops = new List<PaintOp>(polygons.Count);
        foreach (var polygon in polygons)
        {
            ops.Add(new AreaPaintOp
            {
                FeatureReference = "basemap:ne_10m_land",
                WorldShell = polygon.WorldShell,
                WorldHoles = polygon.WorldHoles,
                Fill = LandFill,
                OutlineColor = RgbaColor.Transparent,
                OutlineWidthPx = 0,
            });
        }

        return new VectorScene(ops);
    }

    private static IReadOnlyList<LandPolygon> LoadLandPolygons()
    {
        var polygons = new List<LandPolygon>();
        var stream = typeof(NaturalEarthBasemap).Assembly
            .GetManifestResourceStream(LandGeoJsonResource);
        if (stream is null)
            return polygons;

        using (stream)
        using (var doc = JsonDocument.Parse(stream))
        {
            if (!doc.RootElement.TryGetProperty("features", out var features))
                return polygons;

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geometry))
                    continue;
                ReadGeometry(geometry, polygons);
            }
        }

        return polygons;
    }

    private static void ReadGeometry(JsonElement geometry, List<LandPolygon> sink)
    {
        if (!geometry.TryGetProperty("type", out var typeElement)
            || !geometry.TryGetProperty("coordinates", out var coordinates))
            return;

        switch (typeElement.GetString())
        {
            case "Polygon":
                var polygon = ReadPolygon(coordinates);
                if (polygon is not null)
                    sink.Add(polygon);
                break;

            case "MultiPolygon":
                foreach (var member in coordinates.EnumerateArray())
                {
                    var part = ReadPolygon(member);
                    if (part is not null)
                        sink.Add(part);
                }
                break;
        }
    }

    private static LandPolygon? ReadPolygon(JsonElement rings)
    {
        IReadOnlyList<(double X, double Y)>? shell = null;
        List<IReadOnlyList<(double X, double Y)>>? holes = null;

        foreach (var ring in rings.EnumerateArray())
        {
            var projected = ReadRing(ring);
            if (projected is null)
                continue;

            if (shell is null)
            {
                shell = projected;
            }
            else
            {
                holes ??= new List<IReadOnlyList<(double X, double Y)>>();
                holes.Add(projected);
            }
        }

        return shell is null
            ? null
            : new LandPolygon(
                shell,
                holes ?? (IReadOnlyList<IReadOnlyList<(double X, double Y)>>)Array.Empty<IReadOnlyList<(double X, double Y)>>());
    }

    private static IReadOnlyList<(double X, double Y)>? ReadRing(JsonElement ring)
    {
        var points = new List<(double X, double Y)>();
        foreach (var point in ring.EnumerateArray())
        {
            // GeoJSON coordinate order is [longitude, latitude].
            double lon = point[0].GetDouble();
            double lat = point[1].GetDouble();
            points.Add(WebMercator.FromLonLat(lon, lat));
        }

        return points.Count >= 4 ? points : null;
    }
}
