using System;
using System.Collections.Generic;
using System.IO;
using BruTile.Cache;
using BruTile.Predefined;
using EncDotNet.S100.Renderers.Skia.Scene;
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
    /// <summary>Land fill — a muted, parchment-like tone over the ENC water back-colour.</summary>
    private static readonly Color LandFill = new(
        NaturalEarthBasemap.LandFill.R,
        NaturalEarthBasemap.LandFill.G,
        NaturalEarthBasemap.LandFill.B);

    /// <summary>
    /// Creates the basemap layer for <paramref name="mode"/>, or null for
    /// <see cref="BasemapMode.None"/>.
    /// </summary>
    public static ILayer? Create(BasemapMode mode) => mode switch
    {
        BasemapMode.Online => CreateOnlineLayer(),
        BasemapMode.Offline => CreateOfflineLayer(),
        _ => null,
    };

    private static ILayer CreateOnlineLayer()
    {
        // Persist tiles so previously-viewed areas survive offline and
        // repeated launches; failures fall back to a network-only layer.
        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EncDotNet.S100.Viewer", "TileCache", "osm");
            var cache = new FileCache(cacheDir, "png");
            var source = KnownTileSources.Create(
                KnownTileSource.OpenStreetMap, persistentCache: cache);
            return new TileLayer(source) { Name = "Basemap" };
        }
        catch
        {
            return OpenStreetMap.CreateTileLayer();
        }
    }

    private static ILayer CreateOfflineLayer()
    {
        var features = LoadLandFeatures();
        return new MemoryLayer("Basemap")
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

    private static List<GeometryFeature> LoadLandFeatures()
    {
        // Reuse the shared headless loader so the viewer and the Mapsui-free
        // render path consume the same embedded Natural Earth asset and the
        // same projection (issue #411). The loader already projects rings to
        // EPSG:3857 metres — identical to Mapsui's SphericalMercator — so the
        // world coordinates map straight onto NTS geometry.
        var features = new List<GeometryFeature>();
        foreach (var polygon in NaturalEarthBasemap.LandPolygons)
        {
            var shell = ToLinearRing(polygon.WorldShell);
            if (shell is null)
                continue;

            var holes = new List<LinearRing>(polygon.WorldHoles.Count);
            foreach (var hole in polygon.WorldHoles)
            {
                var ring = ToLinearRing(hole);
                if (ring is not null)
                    holes.Add(ring);
            }

            features.Add(new GeometryFeature(new Polygon(shell, holes.ToArray())));
        }

        return features;
    }

    private static LinearRing? ToLinearRing(IReadOnlyList<(double X, double Y)> world)
    {
        if (world.Count < 4)
            return null;

        var coordinates = new Coordinate[world.Count];
        for (int i = 0; i < world.Count; i++)
            coordinates[i] = new Coordinate(world[i].X, world[i].Y);
        return new LinearRing(coordinates);
    }
}
