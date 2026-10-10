using System.Text;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Reads tiles from a built raster tile set: an XYZ directory, a PMTiles v3
/// archive or an MBTiles database, as <c>s100 tiles export</c> writes them or
/// as other tools do. <see cref="Read"/> and <see cref="ToTileJson"/> may be
/// called concurrently.
/// </summary>
/// <remarks>
/// Sources pick up a tile set that is written again while it is served: each
/// call sees the files as they are now, not as they were when the source was
/// opened.
/// </remarks>
internal interface ITileSource
{
    /// <summary>The full path of the directory or file served.</summary>
    string Path { get; }

    /// <summary>The container read.</summary>
    TileContainer Container { get; }

    /// <summary>The encoding of the tiles.</summary>
    TileImageFormat Format { get; }

    /// <summary>
    /// The encoded tile at XYZ <paramref name="zoom"/>/<paramref name="x"/>/<paramref name="y"/>
    /// (rows numbered from the north), or <see langword="null"/> when the set has none there.
    /// </summary>
    byte[]? Read(int zoom, int x, int y);

    /// <summary>
    /// A TileJSON 3.0.0 document describing the set, without its <c>tiles</c>
    /// URL template (the server adds it).
    /// </summary>
    JsonObject ToTileJson();
}

/// <summary>Opens the <see cref="ITileSource"/> for a path.</summary>
internal static class TileSource
{
    /// <summary>
    /// Opens <paramref name="path"/>: a directory is read as an XYZ directory;
    /// a file as PMTiles or MBTiles, by its signature.
    /// </summary>
    /// <exception cref="InvalidDataException">The path is not a tile set this can read.</exception>
    /// <exception cref="NotSupportedException">The tile set holds tiles other than PNG, JPEG or WebP images.</exception>
    public static ITileSource Open(string path)
    {
        if (Directory.Exists(path))
            return new XyzDirectoryTileSource(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"'{path}' does not exist.", path);

        var signature = new byte[16];
        int read;
        using (var stream = File.OpenRead(path))
            read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);

        if (read >= 7 && Encoding.ASCII.GetString(signature, 0, 7) == "PMTiles")
            return new PmTilesTileSource(path);
        if (read == 16 && Encoding.ASCII.GetString(signature) == "SQLite format 3\0")
            return new MbTilesTileSource(path);

        throw new InvalidDataException(
            $"'{path}' is not a tile set: expected an XYZ directory, a PMTiles archive or an MBTiles database.");
    }

    /// <summary>The tile format named by a TileJSON or MBTiles <c>format</c> value.</summary>
    /// <exception cref="NotSupportedException">The format is not a raster image this can serve.</exception>
    public static TileImageFormat ParseFormat(string? format, string path) => format?.Trim().ToLowerInvariant() switch
    {
        "png" => TileImageFormat.Png,
        "jpg" or "jpeg" => TileImageFormat.Jpeg,
        "webp" => TileImageFormat.Webp,
        null or "" => throw new InvalidDataException($"'{path}' doesn't say what format its tiles are in."),
        var other => throw new NotSupportedException(
            $"'{path}' holds '{other}' tiles; only PNG, JPEG and WebP raster tiles can be served."),
    };

    /// <summary>The HTTP media type of a tile.</summary>
    public static string ContentType(TileImageFormat format) => format switch
    {
        TileImageFormat.Jpeg => "image/jpeg",
        TileImageFormat.Webp => "image/webp",
        _ => "image/png",
    };

    /// <summary>Whether XYZ <paramref name="x"/>/<paramref name="y"/> lie on zoom level <paramref name="zoom"/>.</summary>
    public static bool IsValid(int zoom, int x, int y) =>
        zoom is >= 0 and <= 26 && x >= 0 && y >= 0 && x < 1 << zoom && y < 1 << zoom;
}
