using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Writes tiles into an MBTiles 1.3 SQLite database
/// (https://github.com/mapbox/mbtiles-spec/blob/master/1.3/spec.md).
/// </summary>
/// <remarks>
/// MBTiles numbers rows from the south (TMS), so each XYZ row is flipped on
/// write. Tiles arrive from several threads; one connection serialises them and
/// commits in batches. An existing file at the output path is replaced, and an
/// unfinished database is deleted on dispose.
/// </remarks>
internal sealed class MbTilesTileSink : ITileSink
{
    private const int BatchSize = 1000;

    private readonly string _outputPath;
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _insert;
    private readonly Lock _gate = new();
    private SqliteTransaction _transaction;
    private int _pending;
    private bool _completed;

    public MbTilesTileSink(string outputPath)
    {
        _outputPath = Path.GetFullPath(outputPath);
        File.Delete(_outputPath);

        // No pooling, so disposing the connection releases the file and an
        // unfinished database can be deleted.
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _outputPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _connection.Open();

        Execute("""
            PRAGMA journal_mode = MEMORY;
            PRAGMA synchronous = OFF;
            PRAGMA application_id = 0x4d504258;
            PRAGMA user_version = 0;
            CREATE TABLE metadata (name TEXT, value TEXT);
            CREATE UNIQUE INDEX name ON metadata (name);
            CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);
            CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row);
            """);

        _transaction = _connection.BeginTransaction();
        _insert = _connection.CreateCommand();
        _insert.CommandText =
            "INSERT OR REPLACE INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES ($z, $x, $y, $data)";
        _insert.Parameters.Add("$z", SqliteType.Integer);
        _insert.Parameters.Add("$x", SqliteType.Integer);
        _insert.Parameters.Add("$y", SqliteType.Integer);
        _insert.Parameters.Add("$data", SqliteType.Blob);
    }

    public void Write(int zoom, int x, int y, byte[] data)
    {
        lock (_gate)
        {
            _insert.Transaction = _transaction;
            _insert.Parameters["$z"].Value = zoom;
            _insert.Parameters["$x"].Value = x;
            _insert.Parameters["$y"].Value = (1 << zoom) - 1 - y;
            _insert.Parameters["$data"].Value = data;
            _insert.ExecuteNonQuery();

            if (++_pending >= BatchSize)
            {
                _transaction.Commit();
                _transaction.Dispose();
                _transaction = _connection.BeginTransaction();
                _pending = 0;
            }
        }
    }

    public void Complete(TileSetMetadata metadata)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = "INSERT OR REPLACE INTO metadata (name, value) VALUES ($name, $value)";
            var name = command.Parameters.Add("$name", SqliteType.Text);
            var value = command.Parameters.Add("$value", SqliteType.Text);

            foreach (var (key, text) in Describe(metadata))
            {
                name.Value = key;
                value.Value = text;
                command.ExecuteNonQuery();
            }

            _transaction.Commit();
            _transaction.Dispose();
            _transaction = _connection.BeginTransaction();
            _completed = true;
        }
    }

    /// <summary>The MBTiles <c>metadata</c> rows for a tile set.</summary>
    internal static IEnumerable<(string Name, string Value)> Describe(TileSetMetadata metadata)
    {
        var (west, south, east, north) = metadata.Bounds;
        var (lon, lat, zoom) = metadata.Center;
        yield return ("name", metadata.Name);
        yield return ("format", metadata.Extension);
        yield return ("type", "overlay");
        yield return ("version", "1.0.0");
        yield return ("description", metadata.Description);
        yield return ("minzoom", metadata.MinZoom.ToString(CultureInfo.InvariantCulture));
        yield return ("maxzoom", metadata.MaxZoom.ToString(CultureInfo.InvariantCulture));
        yield return ("bounds", string.Create(CultureInfo.InvariantCulture, $"{west},{south},{east},{north}"));
        yield return ("center", string.Create(CultureInfo.InvariantCulture, $"{lon},{lat},{zoom}"));

        // Not part of MBTiles 1.3: the tile size and the display settings baked
        // into the tiles, as in the TileJSON the other containers write.
        yield return ("tileSize", metadata.TilePixelSize.ToString(CultureInfo.InvariantCulture));
        foreach (var (key, text) in metadata.Settings)
            yield return ("s100:" + key, text);
    }

    public void Dispose()
    {
        _insert.Dispose();
        _transaction.Dispose();
        _connection.Dispose();

        if (!_completed)
        {
            // An unfinished database has no metadata; don't leave it behind.
            try
            {
                File.Delete(_outputPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
