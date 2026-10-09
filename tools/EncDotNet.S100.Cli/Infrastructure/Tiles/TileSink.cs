using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>The tile container <c>s100 tiles</c> writes.</summary>
internal enum TileContainer
{
    /// <summary>A directory of <c>{z}/{x}/{y}.{ext}</c> files.</summary>
    Xyz,

    /// <summary>A single PMTiles v3 archive.</summary>
    PmTiles,

    /// <summary>An MBTiles 1.3 SQLite database.</summary>
    MbTiles,
}

/// <summary>The encoding of each tile image.</summary>
internal enum TileImageFormat
{
    /// <summary>PNG (lossless, with transparency).</summary>
    Png,

    /// <summary>JPEG (lossy, no transparency).</summary>
    Jpeg,

    /// <summary>WebP (lossy, with transparency).</summary>
    Webp,
}

/// <summary>
/// Describes a tile set: what TileJSON and PMTiles record about it.
/// </summary>
internal sealed record TileSetMetadata
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required TileImageFormat Format { get; init; }

    public required int MinZoom { get; init; }

    public required int MaxZoom { get; init; }

    /// <summary>The WGS-84 bounds: west, south, east, north.</summary>
    public required (double West, double South, double East, double North) Bounds { get; init; }

    /// <summary>The image edge length of each tile, in pixels.</summary>
    public required int TilePixelSize { get; init; }

    /// <summary>The display settings baked into the tiles (palette, scales, …).</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    public (double Longitude, double Latitude, int Zoom) Center =>
        ((Bounds.West + Bounds.East) / 2.0, (Bounds.South + Bounds.North) / 2.0, MinZoom);

    /// <summary>The file extension of a tile, without the dot.</summary>
    public string Extension => FormatToken(Format);

    public static string FormatToken(TileImageFormat format) => format switch
    {
        TileImageFormat.Jpeg => "jpg",
        TileImageFormat.Webp => "webp",
        _ => "png",
    };

    /// <summary>
    /// Builds a TileJSON 3.0.0 document. <paramref name="tilesUrl"/> is the tile
    /// URL template, or <see langword="null"/> to leave it out (PMTiles readers
    /// supply their own).
    /// </summary>
    public JsonObject ToTileJson(string? tilesUrl)
    {
        var settings = new JsonObject();
        foreach (var (key, value) in Settings)
            settings[key] = value;

        var json = new JsonObject
        {
            ["tilejson"] = "3.0.0",
            ["name"] = Name,
            ["description"] = Description,
            ["version"] = "1.0.0",
            ["scheme"] = "xyz",
            ["format"] = Extension,
            ["type"] = "overlay",
            ["minzoom"] = MinZoom,
            ["maxzoom"] = MaxZoom,
            ["bounds"] = new JsonArray(Bounds.West, Bounds.South, Bounds.East, Bounds.North),
            ["center"] = new JsonArray(Center.Longitude, Center.Latitude, Center.Zoom),
            ["tileSize"] = TilePixelSize,
            ["s100"] = settings,
        };
        if (tilesUrl is not null)
            json["tiles"] = new JsonArray(tilesUrl);
        return json;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Metadata is not embedded in HTML; keep '+' in versions readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// Receives encoded tiles and writes them to a container. <see cref="Write"/> may
/// be called concurrently.
/// </summary>
internal interface ITileSink : IDisposable
{
    /// <summary>Stores one encoded tile.</summary>
    void Write(int zoom, int x, int y, byte[] data);

    /// <summary>Finishes the container, recording <paramref name="metadata"/>.</summary>
    void Complete(TileSetMetadata metadata);
}
