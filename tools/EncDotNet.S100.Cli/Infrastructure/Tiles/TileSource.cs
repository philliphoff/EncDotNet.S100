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
    /// The time steps the tiles can be served at (UTC, ascending), chosen with
    /// a <c>t</c> query parameter; empty when the tiles don't vary with time.
    /// </summary>
    IReadOnlyList<DateTime> Times => [];

    /// <summary>
    /// Finds the features and coverage values at a point in the datasets the
    /// tiles are rendered from, for WMS GetFeatureInfo; <see langword="null"/>
    /// when the tiles have no datasets behind them (a built tile set).
    /// </summary>
    DatasetFeatureQuery? FeatureQuery => null;

    /// <summary>
    /// The encoded tile at XYZ <paramref name="zoom"/>/<paramref name="x"/>/<paramref name="y"/>
    /// (rows numbered from the north) in <paramref name="palette"/> (one of
    /// <see cref="Palettes"/>, or <see langword="null"/> for the default) at
    /// the time step nearest <paramref name="time"/> (<see langword="null"/>
    /// for the default; ignored when <see cref="Times"/> is empty), or
    /// <see langword="null"/> when there is none there.
    /// </summary>
    ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken);

    /// <summary>
    /// A TileJSON 3.0.0 document describing the tiles in <paramref name="palette"/>
    /// at the time step nearest <paramref name="time"/>, without its
    /// <c>tiles</c> URL template (the server adds it).
    /// </summary>
    JsonObject ToTileJson(string? palette, DateTime? time = null);
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

    ValueTask<byte[]?> ITileSource.ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken) =>
        ValueTask.FromResult(palette is null ? Read(zoom, x, y) : null);

    JsonObject ITileSource.ToTileJson(string? palette, DateTime? time) => ToTileJson();
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
        {
            return IsGeoPackage(path)
                ? new GeoPackageTileSource(path)
                : new MbTilesTileSource(path);
        }

        throw new InvalidDataException(
            $"'{path}' is not a tile set: expected an XYZ directory, a PMTiles archive, an MBTiles database or a GeoPackage.");
    }

    /// <summary>
    /// Whether <paramref name="path"/> looks like a built tile set rather than a
    /// dataset, a folder of datasets or an exchange set: an XYZ directory (one
    /// holding a <c>tiles.json</c>, or <c>{z}/{x}</c> folders with numeric names),
    /// or a PMTiles or SQLite (MBTiles or GeoPackage) file.
    /// </summary>
    public static bool IsTileSet(string path)
    {
        if (Directory.Exists(path))
            return LooksLikeXyzDirectory(path);
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

    /// <summary>
    /// Whether <paramref name="directory"/> holds a <c>tiles.json</c>, or a zoom
    /// folder (named 0–26) that itself holds a numerically named column folder.
    /// </summary>
    private static bool LooksLikeXyzDirectory(string directory)
    {
        try
        {
            if (File.Exists(System.IO.Path.Combine(directory, XyzDirectoryTileSink.TileJsonFileName)))
                return true;

            return Directory.EnumerateDirectories(directory)
                .Where(z => int.TryParse(System.IO.Path.GetFileName(z), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var zoom)
                    && zoom <= 26)
                .Any(z => Directory.EnumerateDirectories(z)
                    .Any(x => int.TryParse(System.IO.Path.GetFileName(x), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the SQLite database at <paramref name="path"/> is a GeoPackage:
    /// its application id is <c>GPKG</c> (or <c>GP10</c>/<c>GP11</c>), or it has
    /// a <c>gpkg_contents</c> table. Any other SQLite file is read as MBTiles.
    /// </summary>
    internal static bool IsGeoPackage(string path)
    {
        // The application id is the big-endian integer at offset 68 of the header.
        var header = new byte[72];
        using (var stream = File.OpenRead(path))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                return false;
        }

        int applicationId = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(68));
        if (applicationId == GeoPackage.ApplicationId || GeoPackage.LegacyApplicationIds.Contains(applicationId))
            return true;
        if (applicationId != 0)
            return false;

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'gpkg_contents'";
        return command.ExecuteScalar() is not null;
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
