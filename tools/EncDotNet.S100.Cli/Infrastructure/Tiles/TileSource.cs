using System.Text;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Supplies the tiles <see cref="TileServer"/> serves: read from a built tile
/// set (<see cref="ITileSetSource"/>) or rendered from datasets on demand
/// (<see cref="RenderedTileSource"/>). Members may be called concurrently.
/// </summary>
internal interface ITileSource
{
    /// <summary>What is served, for the startup message: a path, or the datasets rendered.</summary>
    string Path { get; }

    /// <summary>The encoding of the tiles.</summary>
    TileImageFormat Format { get; }

    /// <summary>
    /// The palettes the tiles can be served in, each under its own
    /// <c>/{palette}/</c> URL; empty when the set has one look only.
    /// </summary>
    IReadOnlyList<string> Palettes => [];

    /// <summary>
    /// The encoded tile at XYZ <paramref name="zoom"/>/<paramref name="x"/>/<paramref name="y"/>
    /// (rows numbered from the north) in <paramref name="palette"/> (one of
    /// <see cref="Palettes"/>, or <see langword="null"/> for the default), or
    /// <see langword="null"/> when there is none there.
    /// </summary>
    ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, CancellationToken cancellationToken);

    /// <summary>
    /// A TileJSON 3.0.0 document describing the tiles in <paramref name="palette"/>,
    /// without its <c>tiles</c> URL template (the server adds it).
    /// </summary>
    JsonObject ToTileJson(string? palette);
}

/// <summary>
/// Reads tiles from a built raster tile set: an XYZ directory, a PMTiles v3
/// archive or an MBTiles database, as <c>s100 tiles export</c> writes them or
/// as other tools do.
/// </summary>
/// <remarks>
/// Sources pick up a tile set that is written again while it is served: each
/// call sees the files as they are now, not as they were when the source was
/// opened.
/// </remarks>
internal interface ITileSetSource : ITileSource
{
    /// <summary>The container read.</summary>
    TileContainer Container { get; }

    /// <summary>
    /// The encoded tile at XYZ <paramref name="zoom"/>/<paramref name="x"/>/<paramref name="y"/>
    /// (rows numbered from the north), or <see langword="null"/> when the set has none there.
    /// </summary>
    byte[]? Read(int zoom, int x, int y);

    /// <summary>A TileJSON 3.0.0 document describing the set, without its <c>tiles</c> URL template.</summary>
    JsonObject ToTileJson();

    ValueTask<byte[]?> ITileSource.ReadAsync(int zoom, int x, int y, string? palette, CancellationToken cancellationToken) =>
        ValueTask.FromResult(palette is null ? Read(zoom, x, y) : null);

    JsonObject ITileSource.ToTileJson(string? palette) => ToTileJson();
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
    public static ITileSetSource Open(string path)
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

    /// <summary>
    /// Whether <paramref name="path"/> looks like a built tile set rather than a
    /// dataset or an exchange set: a directory that isn't an exchange set, or a
    /// PMTiles or SQLite (MBTiles) file.
    /// </summary>
    public static bool IsTileSet(string path)
    {
        if (Directory.Exists(path))
            return !ExchangeSetInput.LooksLikeExchangeSet(path);
        if (!File.Exists(path))
            return false;

        var signature = new byte[16];
        int read;
        try
        {
            using var stream = File.OpenRead(path);
            read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        }
        catch (IOException)
        {
            return false;
        }

        return (read >= 7 && Encoding.ASCII.GetString(signature, 0, 7) == "PMTiles")
            || (read == 16 && Encoding.ASCII.GetString(signature) == "SQLite format 3\0");
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
