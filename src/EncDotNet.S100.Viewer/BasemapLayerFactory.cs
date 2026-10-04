using System.Collections.Concurrent;
using BruTile.Cache;
using BruTile.Predefined;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Builds the basemap layer for a given <see cref="BasemapMode"/> (issue
/// #295): a bundled offline Natural Earth land layer, an online
/// OpenStreetMap tile layer with a persistent on-disk cache, or nothing.
/// </summary>
internal static class BasemapLayerFactory
{
    /// <summary>
    /// Stable name for the basemap layer, in every mode. Used to identify and
    /// live-replace it (it always sits at layer index 0, beneath the data).
    /// </summary>
    public const string LayerName = "Basemap";

    /// <summary>Land fill — a muted, parchment-like tone over the ENC water back-colour.</summary>
    private static readonly Color LandFill = new(
        NaturalEarthBasemap.LandFill.R,
        NaturalEarthBasemap.LandFill.G,
        NaturalEarthBasemap.LandFill.B);

    /// <summary>
    /// EPSG:3857 X offsets (metres) at which the offline land geometry is
    /// repeated: the standard world (<c>0</c>) plus the immediately-adjacent
    /// world copies one <see cref="WebMercator.Circumference"/> east and west.
    /// A dataset kept in a continuous longitude frame that straddles the ±180°
    /// antimeridian (e.g. the US NWS S-411 sea-ice product, ~175°E → ~225°E)
    /// projects to world-X beyond ±180°; the ±1 copies put land beneath it
    /// instead of leaving it to float over empty water.
    /// </summary>
    private static readonly double[] WorldCopyOffsetsX =
    {
        -WebMercator.Circumference,
        0.0,
        WebMercator.Circumference,
    };

    /// <summary>
    /// The canonical single-world EPSG:3857 extent (a
    /// <c>2·Extent × 2·Extent</c> square centred on the origin). The offline
    /// basemap reports this as its <see cref="ILayer.Extent"/> even though its
    /// features are repeated across adjacent world copies, so the world copies
    /// do not inflate <see cref="Mapsui.Map.Extent"/> (which drives
    /// "zoom to extent" and other auto-fit fallbacks).
    /// </summary>
    private static readonly MRect WorldExtent = new(
        -WebMercator.Circumference / 2.0,
        -WebMercator.Circumference / 2.0,
        WebMercator.Circumference / 2.0,
        WebMercator.Circumference / 2.0);

    /// <summary>
    /// Creates the basemap layer for <paramref name="mode"/>, or null for
    /// <see cref="BasemapMode.None"/>.
    /// </summary>
    public static ILayer? TryCreate(BasemapMode mode) => mode switch
    {
        BasemapMode.Online => CreateOnlineLayer(),
        BasemapMode.Offline => CreateOfflineLayer(),
        _ => null,
    };

    private static ILayer CreateOnlineLayer()
    {
        // Persist tiles so previously-viewed areas survive offline and
        // repeated launches; failures fall back to a network-only layer.
        // NOTE: the OSM XYZ tile schema spans a single world ([-180°, +180°]),
        // and Mapsui's tiling does not render horizontal world copies, so an
        // antimeridian dataset in a continuous frame (see WorldCopyOffsetsX)
        // has no online tiles beneath its portion east of +180°. Wrapping the
        // tile source across world copies is a larger, separate change; the
        // bundled offline basemap does provide world-copied land beneath such
        // datasets.
        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EncDotNet.S100.Viewer", "TileCache", "osm");
            var cache = new FileCache(cacheDir, "png");
            var source = KnownTileSources.Create(
                KnownTileSource.OpenStreetMap, persistentCache: cache);
            return new TileLayer(source) { Name = LayerName };
        }
        catch
        {
            return OpenStreetMap.CreateTileLayer();
        }
    }

    private static ILayer CreateOfflineLayer() => new TiledLandLayer(LayerName)
    {
        Style = new VectorStyle
        {
            Fill = new Brush(LandFill),
            Outline = null,
            Line = null,
        },
    };

    /// <summary>
    /// The offline land layer (issue #731). It reuses the shared headless land
    /// source so the viewer and the Mapsui-free render path draw the same
    /// embedded Natural Earth asset in the same projection (issue #411). Each
    /// fetch picks the level of detail for the map resolution and returns only
    /// the tiles in view, so a harbour-scale view gets the full-resolution
    /// coastline while every pan frame clips a bounded number of points. The
    /// land is repeated across adjacent world copies (see
    /// <see cref="WorldCopyOffsetsX"/>), but the layer reports the canonical
    /// single-world <see cref="WorldExtent"/>, so the extra copies never widen
    /// <see cref="Mapsui.Map.Extent"/> and thus never blow "zoom to extent"
    /// out to several worlds.
    /// </summary>
    private sealed class TiledLandLayer(string name) : BaseLayer(name)
    {
        // Features are built once per tile and world copy and then reused, so
        // their ids (which key Mapsui's path cache) stay stable across frames.
        private readonly ConcurrentDictionary<(LandLevel Level, LandTile Tile, double OffsetX), GeometryFeature[]> _features = new();

        /// <inheritdoc />
        public override MRect? Extent => WorldExtent;

        /// <inheritdoc />
        public override IEnumerable<IFeature> GetFeatures(MRect box, double resolution)
        {
            var level = NaturalEarthBasemap.SelectLevel(resolution);
            if (level is null)
                yield break;

            foreach (var offsetX in WorldCopyOffsetsX)
            {
                var tiles = level.GetTiles(box.MinX - offsetX, box.MinY, box.MaxX - offsetX, box.MaxY);
                foreach (var tile in tiles)
                {
                    var features = _features.GetOrAdd((level, tile, offsetX), key => CreateFeatures(key.Tile, key.OffsetX));
                    foreach (var feature in features)
                    {
                        if (feature.Extent?.Intersects(box) == true)
                            yield return feature;
                    }
                }
            }
        }

        private static GeometryFeature[] CreateFeatures(LandTile tile, double offsetX)
        {
            var features = new List<GeometryFeature>(tile.Polygons.Count);
            foreach (var polygon in tile.Polygons)
            {
                var shell = ToLinearRing(polygon.WorldShell, offsetX, counterClockwise: true);
                if (shell is null)
                    continue;

                var holes = new List<LinearRing>(polygon.WorldHoles.Count);
                foreach (var hole in polygon.WorldHoles)
                {
                    var ring = ToLinearRing(hole, offsetX, counterClockwise: false);
                    if (ring is not null)
                        holes.Add(ring);
                }

                features.Add(new GeometryFeature(new Polygon(shell, holes.ToArray())));
            }

            return features.ToArray();
        }
    }

    private static LinearRing? ToLinearRing(
        IReadOnlyList<(double X, double Y)> world, double offsetX, bool counterClockwise)
    {
        if (world.Count < 4)
            return null;

        var coordinates = new Coordinate[world.Count];
        for (int i = 0; i < world.Count; i++)
            coordinates[i] = new Coordinate(world[i].X + offsetX, world[i].Y);

        // Store rings in the winding Mapsui's polygon path builder wants (shell
        // counter-clockwise, holes clockwise) so it never has to reverse a
        // continent-sized ring on a paint.
        if (NetTopologySuite.Algorithm.Orientation.IsCCW(coordinates) != counterClockwise)
            Array.Reverse(coordinates);

        return new BasemapRing(coordinates);
    }

    /// <summary>
    /// A <see cref="LinearRing"/> that computes <see cref="Geometry.IsSimple"/>
    /// once. Mapsui's polygon path builder checks <c>IsRing</c> (and so
    /// <c>IsSimple</c>, a full NTS noding pass) on every path rebuild, and its
    /// path cache is keyed on the viewport extent, so every pan frame rebuilt
    /// it — several milliseconds per frame for the Natural Earth continent
    /// rings. The basemap geometry is immutable, so the answer never changes.
    /// </summary>
    private sealed class BasemapRing(Coordinate[] coordinates) : LinearRing(coordinates)
    {
        private bool? _isSimple;

        public override bool IsSimple => _isSimple ??= base.IsSimple;
    }
}
