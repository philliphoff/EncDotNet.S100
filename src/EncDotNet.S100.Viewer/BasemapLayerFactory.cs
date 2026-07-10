using System;
using System.Collections.Generic;
using System.IO;
using BruTile.Cache;
using BruTile.Predefined;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia.Scene;
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

    private static ILayer CreateOfflineLayer()
    {
        var features = LoadLandFeatures();
        return new WorldCopiedLandLayer(LayerName)
        {
            Features = features,
            Style = new VectorStyle
            {
                Fill = new Brush(LandFill),
                Outline = null,
                Line = null,
            },
        };
    }

    /// <summary>
    /// A <see cref="MemoryLayer"/> whose land geometry is repeated across
    /// adjacent world copies but which reports the canonical single-world
    /// <see cref="WorldExtent"/>, so the extra copies never widen
    /// <see cref="Mapsui.Map.Extent"/> and thus never blow "zoom to extent"
    /// out to several worlds.
    /// </summary>
    private sealed class WorldCopiedLandLayer : MemoryLayer
    {
        public WorldCopiedLandLayer(string name) : base(name)
        {
        }

        /// <inheritdoc />
        public override MRect? Extent => WorldExtent;
    }

    private static List<GeometryFeature> LoadLandFeatures()
    {
        // Reuse the shared headless loader so the viewer and the Mapsui-free
        // render path consume the same embedded Natural Earth asset and the
        // same projection (issue #411). The loader already projects rings to
        // EPSG:3857 metres — identical to Mapsui's SphericalMercator — so the
        // world coordinates map straight onto NTS geometry. Each polygon is
        // emitted once per world copy (see WorldCopyOffsetsX) so continuous-
        // frame antimeridian datasets have land beneath them; Mapsui culls
        // features by envelope, so the off-screen copies cost only a bounds
        // test per frame.
        var features = new List<GeometryFeature>();
        foreach (var offsetX in WorldCopyOffsetsX)
        {
            foreach (var polygon in NaturalEarthBasemap.LandPolygons)
            {
                var shell = ToLinearRing(polygon.WorldShell, offsetX);
                if (shell is null)
                    continue;

                var holes = new List<LinearRing>(polygon.WorldHoles.Count);
                foreach (var hole in polygon.WorldHoles)
                {
                    var ring = ToLinearRing(hole, offsetX);
                    if (ring is not null)
                        holes.Add(ring);
                }

                features.Add(new GeometryFeature(new Polygon(shell, holes.ToArray())));
            }
        }

        return features;
    }

    private static LinearRing? ToLinearRing(IReadOnlyList<(double X, double Y)> world, double offsetX)
    {
        if (world.Count < 4)
            return null;

        var coordinates = new Coordinate[world.Count];
        for (int i = 0; i < world.Count; i++)
            coordinates[i] = new Coordinate(world[i].X + offsetX, world[i].Y);
        return new LinearRing(coordinates);
    }
}
