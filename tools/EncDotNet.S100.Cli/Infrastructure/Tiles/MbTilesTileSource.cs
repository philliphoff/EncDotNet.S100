using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Reads an MBTiles 1.3 SQLite database
/// (https://github.com/mapbox/mbtiles-spec/blob/master/1.3/spec.md).
/// </summary>
/// <remarks>
/// Each read opens the database read-only and without pooling, so a database
/// written again while it is served is read as it is now, and nothing holds
/// it open between requests. MBTiles numbers rows from the south (TMS), so
/// each XYZ row is flipped.
/// </remarks>
internal sealed class MbTilesTileSource : ITileSetSource
{
    private readonly string _connectionString;

    public MbTilesTileSource(string path)
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
            var metadata = ReadMetadata();
            Format = TileSource.ParseFormat(metadata.GetValueOrDefault("format"), Path);
        }
        catch (SqliteException e)
        {
            throw new InvalidDataException($"'{Path}' is not an MBTiles database: {e.Message}", e);
        }
    }

    public string Path { get; }

    public TileContainer Container => TileContainer.MbTiles;

    public TileImageFormat Format { get; }

    public byte[]? Read(int zoom, int x, int y)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tile_data FROM tiles WHERE zoom_level = $z AND tile_column = $x AND tile_row = $y";
        command.Parameters.AddWithValue("$z", zoom);
        command.Parameters.AddWithValue("$x", x);
        command.Parameters.AddWithValue("$y", (1 << zoom) - 1 - y);
        return command.ExecuteScalar() as byte[];
    }

    public JsonObject ToTileJson()
    {
        var metadata = ReadMetadata();
        var json = new JsonObject
        {
            ["tilejson"] = "3.0.0",
            ["name"] = metadata.GetValueOrDefault("name") ?? System.IO.Path.GetFileNameWithoutExtension(Path),
            ["scheme"] = "xyz",
            ["format"] = TileSetMetadata.FormatToken(Format),
        };

        foreach (var key in new[] { "description", "version", "attribution", "type" })
        {
            if (metadata.GetValueOrDefault(key) is { } text)
                json[key] = text;
        }

        foreach (var key in new[] { "minzoom", "maxzoom", "tileSize" })
        {
            if (int.TryParse(metadata.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                json[key] = value;
        }

        if (ParseNumbers(metadata.GetValueOrDefault("bounds")) is { Length: 4 } bounds)
            json["bounds"] = new JsonArray(bounds.Select(b => (JsonNode?)b).ToArray());
        if (ParseNumbers(metadata.GetValueOrDefault("center")) is { Length: 3 } center)
            json["center"] = new JsonArray(center[0], center[1], (int)center[2]);

        // The display settings `tiles export` records, as in its TileJSON.
        var settings = new JsonObject();
        foreach (var (key, text) in metadata.Where(m => m.Key.StartsWith("s100:", StringComparison.Ordinal)))
            settings[key["s100:".Length..]] = text;
        if (settings.Count > 0)
            json["s100"] = settings;

        return json;
    }

    private Dictionary<string, string> ReadMetadata()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, value FROM metadata";
        using var reader = command.ExecuteReader();

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                metadata[reader.GetString(0)] = reader.GetString(1);
        }

        return metadata;
    }

    private static double[]? ParseNumbers(string? text)
    {
        if (text is null)
            return null;

        var parts = text.Split(',');
        var numbers = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }

        return numbers;
    }
}
