using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Reads the tiles of an OGC GeoPackage (https://www.geopackage.org/spec140/)
/// tiled in Web Mercator, as <c>s100 tiles export</c> writes, or as GDAL
/// writes with <c>TILING_SCHEME=GoogleMapsCompatible</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tile table is the one <c>tiles export</c> writes
/// (<see cref="GeoPackage.TileTable"/>) or else the first that can be served:
/// an EPSG:3857 tile matrix set covering the whole Web Mercator square, with
/// square power-of-two matrices. Each matrix is matched to its XYZ zoom by its
/// width, so a GeoPackage whose <c>zoom_level</c>s don't start at zoom 0 reads
/// right. Tiles in another CRS or tiling are refused.
/// </para>
/// <para>
/// The tile format is the one in the TileJSON <c>tiles export</c> records,
/// or else that of the first tile. As for MBTiles, each read opens the
/// database read-only and without pooling.
/// </para>
/// </remarks>
internal sealed class GeoPackageTileSource : ITileSetSource
{
    private const double ExtentTolerance = 1.0; // metres

    private readonly string _connectionString;
    private readonly string _table;
    private readonly Dictionary<int, int> _zoomLevels; // XYZ zoom → gpkg zoom_level
    private readonly int _tilePixelSize;

    public GeoPackageTileSource(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        try
        {
            using var connection = Open();
            (_table, _zoomLevels, _tilePixelSize) = FindTileTable(connection, Path);
            var tileJson = ReadTileJson(connection);
            Format = tileJson?["format"]?.GetValue<string>() is { } token
                ? TileSource.ParseFormat(token, Path)
                : SniffFormat(connection);
        }
        catch (SqliteException e)
        {
            throw new InvalidDataException($"'{Path}' is not a GeoPackage: {e.Message}", e);
        }
    }

    public string Path { get; }

    public TileContainer Container => TileContainer.GeoPackage;

    public TileImageFormat Format { get; }

    public byte[]? Read(int zoom, int x, int y)
    {
        if (!_zoomLevels.TryGetValue(zoom, out int level))
            return null;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT tile_data FROM {Quote(_table)} WHERE zoom_level = $z AND tile_column = $x AND tile_row = $y";
        command.Parameters.AddWithValue("$z", level);
        command.Parameters.AddWithValue("$x", x);
        command.Parameters.AddWithValue("$y", y);
        return command.ExecuteScalar() as byte[];
    }

    public JsonObject ToTileJson()
    {
        using var connection = Open();
        var json = ReadTileJson(connection) ?? new JsonObject();
        json["tilejson"] = "3.0.0";
        json["scheme"] = "xyz";
        json["format"] = TileSetMetadata.FormatToken(Format);
        json.Remove("tiles");

        // The zooms and bounds of the tiles actually present, whatever wrote it.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT MIN(zoom_level), MAX(zoom_level) FROM {Quote(_table)}";
            using var reader = command.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0))
            {
                var levels = _zoomLevels.ToDictionary(z => z.Value, z => z.Key);
                if (levels.TryGetValue(reader.GetInt32(0), out int min))
                    json["minzoom"] = min;
                if (levels.TryGetValue(reader.GetInt32(1), out int max))
                    json["maxzoom"] = max;
            }
        }

        if (json["name"] is null || json["bounds"] is null)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT identifier, description, min_x, min_y, max_x, max_y, srs_id FROM gpkg_contents WHERE table_name = $t";
            command.Parameters.AddWithValue("$t", _table);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                json["name"] ??= reader.IsDBNull(0) ? System.IO.Path.GetFileNameWithoutExtension(Path) : reader.GetString(0);
                if (json["description"] is null && !reader.IsDBNull(1) && reader.GetString(1).Length > 0)
                    json["description"] = reader.GetString(1);
                if (json["bounds"] is null && !reader.IsDBNull(2) && !reader.IsDBNull(5) && !reader.IsDBNull(6)
                    && reader.GetInt32(6) == GeoPackage.WebMercatorSrsId)
                {
                    var (west, south) = GeoPackage.ToDegrees(reader.GetDouble(2), reader.GetDouble(3));
                    var (east, north) = GeoPackage.ToDegrees(reader.GetDouble(4), reader.GetDouble(5));
                    json["bounds"] = new JsonArray(west, south, east, north);
                }
            }
        }

        json["tileSize"] ??= _tilePixelSize;
        return json;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static (string Table, Dictionary<int, int> ZoomLevels, int TilePixelSize) FindTileTable(SqliteConnection connection, string path)
    {
        var candidates = new List<(string Table, int SrsId, string? Organization, int? OrganizationId, double MinX, double MinY, double MaxX, double MaxY)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.table_name, s.srs_id, r.organization, r.organization_coordsys_id, s.min_x, s.min_y, s.max_x, s.max_y
                FROM gpkg_contents c
                JOIN gpkg_tile_matrix_set s ON s.table_name = c.table_name
                LEFT JOIN gpkg_spatial_ref_sys r ON r.srs_id = s.srs_id
                WHERE c.data_type = 'tiles'
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                candidates.Add((
                    reader.GetString(0), reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7)));
            }
        }

        if (candidates.Count == 0)
            throw new InvalidDataException($"'{path}' is a GeoPackage without tiles.");

        // The table tiles export writes first, then the others in order.
        candidates.Sort((a, b) => (b.Table == GeoPackage.TileTable).CompareTo(a.Table == GeoPackage.TileTable));
        foreach (var candidate in candidates)
        {
            bool webMercator =
                candidate.SrsId is 3857 or 900913
                || (string.Equals(candidate.Organization, "EPSG", StringComparison.OrdinalIgnoreCase) && candidate.OrganizationId is 3857 or 900913);
            double half = GeoPackage.WebMercatorHalfExtent;
            bool square =
                Math.Abs(candidate.MinX + half) < ExtentTolerance && Math.Abs(candidate.MinY + half) < ExtentTolerance
                && Math.Abs(candidate.MaxX - half) < ExtentTolerance && Math.Abs(candidate.MaxY - half) < ExtentTolerance;
            if (!webMercator || !square)
                continue;

            if (ReadMatrices(connection, candidate.Table) is { } matrices)
                return (candidate.Table, matrices.ZoomLevels, matrices.TilePixelSize);
        }

        throw new InvalidDataException(
            $"'{path}' has no tiles that can be served: tiles serve reads GeoPackage tiles in EPSG:3857 on the Web Mercator quad (as tiles export and GDAL's GoogleMapsCompatible scheme write them).");
    }

    private static (Dictionary<int, int> ZoomLevels, int TilePixelSize)? ReadMatrices(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT zoom_level, matrix_width, matrix_height, tile_width, tile_height FROM gpkg_tile_matrix WHERE table_name = $t ORDER BY zoom_level";
        command.Parameters.AddWithValue("$t", table);
        using var reader = command.ExecuteReader();

        var levels = new Dictionary<int, int>();
        int? size = null;
        while (reader.Read())
        {
            long width = reader.GetInt64(1), height = reader.GetInt64(2);
            int tileWidth = reader.GetInt32(3), tileHeight = reader.GetInt32(4);
            if (width != height || width <= 0 || (width & (width - 1)) != 0 || tileWidth != tileHeight)
                return null;
            if (size is { } s && s != tileWidth)
                return null;
            size = tileWidth;
            levels[System.Numerics.BitOperations.Log2((ulong)width)] = reader.GetInt32(0);
        }

        return size is { } pixels && levels.Count > 0 ? (levels, pixels) : null;
    }

    private JsonObject? ReadTileJson(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT m.metadata FROM gpkg_metadata m
                JOIN gpkg_metadata_reference r ON r.md_file_id = m.id
                WHERE m.md_standard_uri = $uri AND r.table_name = $t
                ORDER BY m.id LIMIT 1
                """;
            command.Parameters.AddWithValue("$uri", GeoPackage.TileJsonStandardUri);
            command.Parameters.AddWithValue("$t", _table);
            return GeoPackage.ParseTileJson(command.ExecuteScalar() as string);
        }
        catch (SqliteException)
        {
            // No metadata extension tables.
            return null;
        }
    }

    private TileImageFormat SniffFormat(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT tile_data FROM {Quote(_table)} LIMIT 1";
        var tile = command.ExecuteScalar() as byte[];
        return tile switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => TileImageFormat.Png,
            [0xFF, 0xD8, ..] => TileImageFormat.Jpeg,
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => TileImageFormat.Webp,
            null => TileImageFormat.Png,
            _ => throw new NotSupportedException(
                $"'{Path}' holds tiles that aren't PNG, JPEG or WebP images; only those can be served."),
        };
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
