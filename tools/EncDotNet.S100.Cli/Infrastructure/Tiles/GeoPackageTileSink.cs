using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Writes tiles into an OGC GeoPackage 1.4 tile pyramid
/// (https://www.geopackage.org/spec140/), for GIS desktops and field apps.
/// </summary>
/// <remarks>
/// <para>
/// The tiles go into one tile table, <see cref="GeoPackage.TileTable"/>, in
/// EPSG:3857 with a tile matrix set covering the whole Web Mercator square, so
/// each matrix is an XYZ zoom: <c>tile_column</c>/<c>tile_row</c> are XYZ
/// x/y, rows counted from the north as in XYZ. WebP tiles register the
/// <c>gpkg_webp</c> extension. The TileJSON of the tile set (with the display
/// settings baked into the tiles) is stored through the metadata extension,
/// so <c>s100 tiles serve</c> reads the same description back.
/// </para>
/// <para>
/// As for MBTiles, tiles arrive from several threads and are committed in
/// batches, and the file is written beside the output and moved over it when
/// finished.
/// </para>
/// </remarks>
internal sealed class GeoPackageTileSink : ITileSink
{
    private const int BatchSize = 1000;

    private readonly string _outputPath;
    private readonly string _partialPath;
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _insert;
    private readonly Lock _gate = new();
    private SqliteTransaction _transaction;
    private int _pending;
    private bool _completed;

    public GeoPackageTileSink(string outputPath)
    {
        _outputPath = Path.GetFullPath(outputPath);
        _partialPath = TileSetFile.PartialPath(_outputPath);
        File.Delete(_partialPath);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _partialPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _connection.Open();

        Execute(string.Create(CultureInfo.InvariantCulture, $"""
            PRAGMA journal_mode = MEMORY;
            PRAGMA synchronous = OFF;
            PRAGMA application_id = {GeoPackage.ApplicationId};
            PRAGMA user_version = {GeoPackage.UserVersion};
            CREATE TABLE gpkg_spatial_ref_sys (
                srs_name TEXT NOT NULL,
                srs_id INTEGER NOT NULL PRIMARY KEY,
                organization TEXT NOT NULL,
                organization_coordsys_id INTEGER NOT NULL,
                definition TEXT NOT NULL,
                description TEXT);
            CREATE TABLE gpkg_contents (
                table_name TEXT NOT NULL PRIMARY KEY,
                data_type TEXT NOT NULL,
                identifier TEXT UNIQUE,
                description TEXT DEFAULT '',
                last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                min_x DOUBLE, min_y DOUBLE, max_x DOUBLE, max_y DOUBLE,
                srs_id INTEGER,
                CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id));
            CREATE TABLE gpkg_tile_matrix_set (
                table_name TEXT NOT NULL PRIMARY KEY,
                srs_id INTEGER NOT NULL,
                min_x DOUBLE NOT NULL, min_y DOUBLE NOT NULL, max_x DOUBLE NOT NULL, max_y DOUBLE NOT NULL,
                CONSTRAINT fk_gtms_table_name FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name),
                CONSTRAINT fk_gtms_srs FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys (srs_id));
            CREATE TABLE gpkg_tile_matrix (
                table_name TEXT NOT NULL,
                zoom_level INTEGER NOT NULL,
                matrix_width INTEGER NOT NULL,
                matrix_height INTEGER NOT NULL,
                tile_width INTEGER NOT NULL,
                tile_height INTEGER NOT NULL,
                pixel_x_size DOUBLE NOT NULL,
                pixel_y_size DOUBLE NOT NULL,
                CONSTRAINT pk_ttm PRIMARY KEY (table_name, zoom_level),
                CONSTRAINT fk_tmm_table_name FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name));
            CREATE TABLE {GeoPackage.TileTable} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                zoom_level INTEGER NOT NULL,
                tile_column INTEGER NOT NULL,
                tile_row INTEGER NOT NULL,
                tile_data BLOB NOT NULL,
                UNIQUE (zoom_level, tile_column, tile_row));
            """));

        _transaction = _connection.BeginTransaction();
        _insert = _connection.CreateCommand();
        _insert.CommandText =
            $"INSERT OR REPLACE INTO {GeoPackage.TileTable} (zoom_level, tile_column, tile_row, tile_data) VALUES ($z, $x, $y, $data)";
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
            _insert.Parameters["$y"].Value = y;
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
            WriteDescription(metadata);
            _transaction.Commit();
            _transaction.Dispose();

            _insert.Dispose();
            _connection.Dispose();
            TileSetFile.Publish(_partialPath, _outputPath);
            _completed = true;
        }
    }

    private void WriteDescription(TileSetMetadata metadata)
    {
        foreach (var srs in GeoPackage.SpatialReferenceSystems)
        {
            Insert(
                "INSERT INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description) VALUES ($1, $2, $3, $4, $5, $6)",
                srs.Name, srs.Id, srs.Organization, srs.OrganizationId, srs.Definition, srs.Description);
        }

        // The data's bounds, in the tile set's CRS; the matrix set is the whole square.
        var (west, south, east, north) = metadata.Bounds;
        var (minX, minY) = GeoPackage.ToWebMercator(west, south);
        var (maxX, maxY) = GeoPackage.ToWebMercator(east, north);
        Insert(
            "INSERT INTO gpkg_contents (table_name, data_type, identifier, description, last_change, min_x, min_y, max_x, max_y, srs_id) VALUES ($1, 'tiles', $2, $3, $4, $5, $6, $7, $8, $9)",
            GeoPackage.TileTable,
            metadata.Name,
            metadata.Description,
            DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            minX, minY, maxX, maxY,
            GeoPackage.WebMercatorSrsId);

        double half = GeoPackage.WebMercatorHalfExtent;
        Insert(
            "INSERT INTO gpkg_tile_matrix_set (table_name, srs_id, min_x, min_y, max_x, max_y) VALUES ($1, $2, $3, $4, $5, $6)",
            GeoPackage.TileTable, GeoPackage.WebMercatorSrsId, -half, -half, half, half);

        for (int zoom = metadata.MinZoom; zoom <= metadata.MaxZoom; zoom++)
        {
            long tiles = 1L << zoom;
            double pixel = 2 * half / (tiles * metadata.TilePixelSize);
            Insert(
                "INSERT INTO gpkg_tile_matrix (table_name, zoom_level, matrix_width, matrix_height, tile_width, tile_height, pixel_x_size, pixel_y_size) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
                GeoPackage.TileTable, zoom, tiles, tiles, metadata.TilePixelSize, metadata.TilePixelSize, pixel, pixel);
        }

        bool webp = metadata.Format == TileImageFormat.Webp;
        Execute("""
            CREATE TABLE gpkg_extensions (
                table_name TEXT,
                column_name TEXT,
                extension_name TEXT NOT NULL,
                definition TEXT NOT NULL,
                scope TEXT NOT NULL,
                CONSTRAINT ge_tce UNIQUE (table_name, column_name, extension_name));
            CREATE TABLE gpkg_metadata (
                id INTEGER CONSTRAINT m_pk PRIMARY KEY ASC NOT NULL,
                md_scope TEXT NOT NULL DEFAULT 'dataset',
                md_standard_uri TEXT NOT NULL,
                mime_type TEXT NOT NULL DEFAULT 'text/xml',
                metadata TEXT NOT NULL DEFAULT '');
            CREATE TABLE gpkg_metadata_reference (
                reference_scope TEXT NOT NULL,
                table_name TEXT,
                column_name TEXT,
                row_id_value INTEGER,
                timestamp DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                md_file_id INTEGER NOT NULL,
                md_parent_id INTEGER,
                CONSTRAINT crmr_mfi_fk FOREIGN KEY (md_file_id) REFERENCES gpkg_metadata(id),
                CONSTRAINT crmr_mpi_fk FOREIGN KEY (md_parent_id) REFERENCES gpkg_metadata(id));
            """);
        Insert(
            "INSERT INTO gpkg_extensions (table_name, column_name, extension_name, definition, scope) VALUES ('gpkg_metadata', NULL, 'gpkg_metadata', 'http://www.geopackage.org/spec/#extension_metadata', 'read-write')");
        Insert(
            "INSERT INTO gpkg_extensions (table_name, column_name, extension_name, definition, scope) VALUES ('gpkg_metadata_reference', NULL, 'gpkg_metadata', 'http://www.geopackage.org/spec/#extension_metadata', 'read-write')");
        if (webp)
        {
            Insert(
                "INSERT INTO gpkg_extensions (table_name, column_name, extension_name, definition, scope) VALUES ($1, 'tile_data', 'gpkg_webp', 'http://www.geopackage.org/spec/#extension_tiles_webp', 'read-write')",
                GeoPackage.TileTable);
        }

        // The TileJSON, as the other containers record it: tiles serve reads it back.
        string tileJson = metadata.ToTileJson(tilesUrl: null).ToJsonString(TileSetMetadata.JsonOptions);
        Insert(
            "INSERT INTO gpkg_metadata (id, md_scope, md_standard_uri, mime_type, metadata) VALUES (1, 'dataset', $1, 'application/json', $2)",
            GeoPackage.TileJsonStandardUri, tileJson);
        Insert(
            "INSERT INTO gpkg_metadata_reference (reference_scope, table_name, md_file_id) VALUES ('table', $1, 1)",
            GeoPackage.TileTable);
    }

    public void Dispose()
    {
        _insert.Dispose();
        _transaction.Dispose();
        _connection.Dispose();

        if (!_completed)
            TileSetFile.DeletePartial(_partialPath);
    }

    private void Insert(string sql, params object?[] values)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue("$" + (i + 1).ToString(CultureInfo.InvariantCulture), values[i] ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

/// <summary>What the GeoPackage tile sink and source share.</summary>
internal static class GeoPackage
{
    /// <summary><c>GPKG</c>, the SQLite application id of a GeoPackage 1.2 or later.</summary>
    public const int ApplicationId = 0x47504B47;

    /// <summary><c>GP10</c> and <c>GP11</c>, the application ids of GeoPackage 1.0 and 1.1.</summary>
    public static readonly int[] LegacyApplicationIds = [0x47503130, 0x47503131];

    /// <summary>GeoPackage 1.4.0.</summary>
    public const int UserVersion = 10400;

    /// <summary>The tile table <c>tiles export</c> writes.</summary>
    public const string TileTable = "chart";

    /// <summary>The <c>gpkg_metadata.md_standard_uri</c> of the TileJSON record.</summary>
    public const string TileJsonStandardUri = "https://github.com/mapbox/tilejson-spec/tree/master/3.0.0";

    public const int WebMercatorSrsId = 3857;

    /// <summary>Half the edge of the Web Mercator square, in metres.</summary>
    public const double WebMercatorHalfExtent = Math.PI * 6378137.0;

    /// <summary>WGS-84 degrees to EPSG:3857 metres.</summary>
    public static (double X, double Y) ToWebMercator(double longitude, double latitude)
    {
        latitude = Math.Clamp(latitude, -85.05112877980659, 85.05112877980659);
        double x = longitude * Math.PI / 180.0 * 6378137.0;
        double y = Math.Log(Math.Tan(Math.PI / 4.0 + latitude * Math.PI / 360.0)) * 6378137.0;
        return (x, y);
    }

    /// <summary>EPSG:3857 metres to WGS-84 degrees.</summary>
    public static (double Longitude, double Latitude) ToDegrees(double x, double y) =>
        (x / 6378137.0 * 180.0 / Math.PI, (2 * Math.Atan(Math.Exp(y / 6378137.0)) - Math.PI / 2) * 180.0 / Math.PI);

    internal sealed record Srs(string Name, int Id, string Organization, int OrganizationId, string Definition, string Description);

    /// <summary>The rows every GeoPackage holds, and EPSG:3857 for the tiles.</summary>
    public static readonly Srs[] SpatialReferenceSystems =
    [
        new("Undefined cartesian SRS", -1, "NONE", -1, "undefined", "undefined cartesian coordinate reference system"),
        new("Undefined geographic SRS", 0, "NONE", 0, "undefined", "undefined geographic coordinate reference system"),
        new("WGS 84 geodetic", 4326, "EPSG", 4326,
            """GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563,AUTHORITY["EPSG","7030"]],AUTHORITY["EPSG","6326"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AXIS["Latitude",NORTH],AXIS["Longitude",EAST],AUTHORITY["EPSG","4326"]]""",
            "longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid"),
        new("WGS 84 / Pseudo-Mercator", WebMercatorSrsId, "EPSG", WebMercatorSrsId,
            """PROJCS["WGS 84 / Pseudo-Mercator",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563,AUTHORITY["EPSG","7030"]],AUTHORITY["EPSG","6326"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AUTHORITY["EPSG","4326"]],PROJECTION["Mercator_1SP"],PARAMETER["central_meridian",0],PARAMETER["scale_factor",1],PARAMETER["false_easting",0],PARAMETER["false_northing",0],UNIT["metre",1,AUTHORITY["EPSG","9001"]],AXIS["Easting",EAST],AXIS["Northing",NORTH],EXTENSION["PROJ4","+proj=merc +a=6378137 +b=6378137 +lat_ts=0 +lon_0=0 +x_0=0 +y_0=0 +k=1 +units=m +nadgrids=@null +wktext +no_defs"],AUTHORITY["EPSG","3857"]]""",
            "Web Mercator, as used by web maps"),
    ];

    /// <summary>Reads a TileJSON record into a document, or <see langword="null"/>.</summary>
    public static System.Text.Json.Nodes.JsonObject? ParseTileJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
