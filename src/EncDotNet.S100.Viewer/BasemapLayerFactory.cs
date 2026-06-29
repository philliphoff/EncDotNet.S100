using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BruTile.Cache;
using BruTile.Predefined;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
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
    private const string LandGeoJsonResource =
        "EncDotNet.S100.Viewer.Assets.Basemap.ne_10m_land.geojson";

    /// <summary>Land fill — a muted, parchment-like tone over the ENC water back-colour.</summary>
    private static readonly Color LandFill = new(238, 232, 220);

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
        var features = new List<GeometryFeature>();
        var stream = typeof(BasemapLayerFactory).Assembly
            .GetManifestResourceStream(LandGeoJsonResource);
        if (stream is null)
            return features;

        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("features", out var arr))
            return features;

        foreach (var feature in arr.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geom)) continue;
            var polygon = ReadGeometry(geom);
            if (polygon is not null)
                features.Add(new GeometryFeature(polygon));
        }

        return features;
    }

    private static Geometry? ReadGeometry(JsonElement geom)
    {
        var type = geom.GetProperty("type").GetString();
        var coords = geom.GetProperty("coordinates");
        return type switch
        {
            "Polygon" => ReadPolygon(coords),
            "MultiPolygon" => ReadMultiPolygon(coords),
            _ => null,
        };
    }

    private static MultiPolygon ReadMultiPolygon(JsonElement coords)
    {
        var polygons = new List<Polygon>();
        foreach (var poly in coords.EnumerateArray())
        {
            var p = ReadPolygon(poly);
            if (p is not null) polygons.Add(p);
        }
        return new MultiPolygon(polygons.ToArray());
    }

    private static Polygon? ReadPolygon(JsonElement rings)
    {
        LinearRing? shell = null;
        var holes = new List<LinearRing>();
        foreach (var ring in rings.EnumerateArray())
        {
            var r = ReadRing(ring);
            if (r is null) continue;
            if (shell is null) shell = r;
            else holes.Add(r);
        }
        return shell is null ? null : new Polygon(shell, holes.ToArray());
    }

    private static LinearRing? ReadRing(JsonElement ring)
    {
        var pts = new List<Coordinate>();
        foreach (var pt in ring.EnumerateArray())
        {
            var lon = pt[0].GetDouble();
            var lat = pt[1].GetDouble();
            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            pts.Add(new Coordinate(x, y));
        }
        return pts.Count >= 4 ? new LinearRing(pts.ToArray()) : null;
    }
}
