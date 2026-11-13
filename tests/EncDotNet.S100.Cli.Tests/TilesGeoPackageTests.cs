using System.Text;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// GeoPackage tile output (<c>tiles export -o x.gpkg</c>) and input
/// (<c>tiles serve x.gpkg</c>).
/// </summary>
public sealed class TilesGeoPackageTests : IDisposable
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Directory.CreateTempSubdirectory("s100-gpkg-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private static TileSetMetadata Metadata(TileImageFormat format = TileImageFormat.Png, int tileSize = 256) => new()
    {
        Name = "Test chart",
        Description = "For tests",
        Format = format,
        MinZoom = 1,
        MaxZoom = 3,
        Bounds = (-10, -5, 10, 5),
        TilePixelSize = tileSize,
        Settings = new Dictionary<string, string> { ["palette"] = "dusk" },
    };

    private static byte[] Content(int zoom, int x, int y) => Encoding.ASCII.GetBytes($"tile {zoom}/{x}/{y}");

    private string Write(string name, TileImageFormat format = TileImageFormat.Png, int tileSize = 256)
    {
        var path = Path.Combine(_root, name);
        using var sink = new GeoPackageTileSink(path);
        sink.Write(1, 0, 0, Content(1, 0, 0));
        sink.Write(2, 1, 0, Content(2, 1, 0));
        sink.Write(3, 5, 2, Content(3, 5, 2));
        sink.Complete(Metadata(format, tileSize));
        return path;
    }

    private static SqliteConnection Connect(string path, bool write = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = write ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Connect(path, write: true);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Tiles_are_written_as_a_web_mercator_quad_in_xyz_rows()
    {
        var path = Write("set.gpkg", tileSize: 512);
        using var connection = Connect(path);

        Assert.Equal(0x47504B47L, (long)Scalar(connection, "PRAGMA application_id")!);
        Assert.Equal(10400L, (long)Scalar(connection, "PRAGMA user_version")!);
        Assert.Equal("ok", Scalar(connection, "PRAGMA integrity_check"));
        Assert.Equal(0L, (long)Scalar(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check")!);

        // The required spatial reference systems, and Web Mercator for the tiles.
        Assert.Equal(4L, (long)Scalar(connection, "SELECT COUNT(*) FROM gpkg_spatial_ref_sys WHERE srs_id IN (-1, 0, 4326, 3857)")!);
        Assert.Equal("tiles", Scalar(connection, "SELECT data_type FROM gpkg_contents WHERE table_name = 'chart'"));
        Assert.Equal(3857L, (long)Scalar(connection, "SELECT srs_id FROM gpkg_contents WHERE table_name = 'chart'")!);
        Assert.Equal("Test chart", Scalar(connection, "SELECT identifier FROM gpkg_contents WHERE table_name = 'chart'"));

        // The matrix set is the whole square, so matrices line up with XYZ zooms.
        const double half = 20037508.342789244;
        Assert.Equal(-half, (double)Scalar(connection, "SELECT min_x FROM gpkg_tile_matrix_set")!, 6);
        Assert.Equal(half, (double)Scalar(connection, "SELECT max_y FROM gpkg_tile_matrix_set")!, 6);
        Assert.Equal(3L, (long)Scalar(connection, "SELECT COUNT(*) FROM gpkg_tile_matrix")!);
        Assert.Equal(8L, (long)Scalar(connection, "SELECT matrix_width FROM gpkg_tile_matrix WHERE zoom_level = 3")!);
        Assert.Equal(512L, (long)Scalar(connection, "SELECT tile_width FROM gpkg_tile_matrix WHERE zoom_level = 3")!);
        Assert.Equal(2 * half / (8 * 512), (double)Scalar(connection, "SELECT pixel_x_size FROM gpkg_tile_matrix WHERE zoom_level = 3")!, 9);

        // Rows are counted from the north, as in XYZ (not flipped as in MBTiles).
        Assert.Equal(Content(3, 5, 2), Scalar(connection, "SELECT tile_data FROM chart WHERE zoom_level = 3 AND tile_column = 5 AND tile_row = 2"));

        // The TileJSON is kept through the metadata extension; PNG needs no tile extension.
        var tileJson = JsonNode.Parse((string)Scalar(connection, "SELECT metadata FROM gpkg_metadata")!)!;
        Assert.Equal("dusk", (string?)tileJson["s100"]!["palette"]);
        Assert.Equal(0L, (long)Scalar(connection, "SELECT COUNT(*) FROM gpkg_extensions WHERE extension_name = 'gpkg_webp'")!);
    }

    [Fact]
    public void Webp_tiles_register_the_webp_extension()
    {
        var path = Write("webp.gpkg", TileImageFormat.Webp);
        using (var connection = Connect(path))
            Assert.Equal("tile_data", Scalar(connection, "SELECT column_name FROM gpkg_extensions WHERE extension_name = 'gpkg_webp' AND table_name = 'chart'"));

        Assert.Equal(TileImageFormat.Webp, TileSource.Open(path).Format);
    }

    [Fact]
    public void A_sqlite_file_is_read_as_a_geopackage_or_mbtiles_by_its_contents()
    {
        Assert.IsType<GeoPackageTileSource>(TileSource.Open(Write("a.gpkg")));

        var mbtiles = Path.Combine(_root, "a.mbtiles");
        using (var sink = new MbTilesTileSink(mbtiles))
            sink.Complete(Metadata());
        Assert.IsType<MbTilesTileSource>(TileSource.Open(mbtiles));

        // A GeoPackage without the application id (as some tools write) is still one.
        var unmarked = Write("unmarked.gpkg");
        Execute(unmarked, "PRAGMA application_id = 0");
        Assert.IsType<GeoPackageTileSource>(TileSource.Open(unmarked));
    }

    [Fact]
    public void Matrices_are_matched_to_xyz_zooms_by_width_and_the_format_is_sniffed()
    {
        // As another tool might write it: zoom levels counted from the first
        // matrix (XYZ zoom 1 is level 0), and no TileJSON record.
        var path = Write("offset.gpkg");
        Execute(path, """
            UPDATE gpkg_tile_matrix SET zoom_level = zoom_level - 1;
            UPDATE chart SET zoom_level = zoom_level - 1;
            DELETE FROM gpkg_metadata_reference;
            DELETE FROM gpkg_metadata;
            UPDATE chart SET tile_data = X'89504E470D0A1A0A' WHERE zoom_level = 0;
            """);

        var source = TileSource.Open(path);
        Assert.Equal(TileImageFormat.Png, source.Format);
        Assert.Equal(Content(3, 5, 2), source.Read(3, 5, 2));
        Assert.Equal(Content(2, 1, 0), source.Read(2, 1, 0));
        Assert.Null(source.Read(0, 0, 0));

        var json = source.ToTileJson();
        Assert.Equal("Test chart", (string?)json["name"]);
        Assert.Equal(1, (int?)json["minzoom"]);
        Assert.Equal(3, (int?)json["maxzoom"]);
        Assert.Equal(-10, (double)json["bounds"]![0]!, 6);
        Assert.Equal(5, (double)json["bounds"]![3]!, 6);
        Assert.Equal(256, (int?)json["tileSize"]);
    }

    [Theory]
    [InlineData("UPDATE gpkg_tile_matrix_set SET srs_id = 4326; UPDATE gpkg_contents SET srs_id = 4326")]
    [InlineData("UPDATE gpkg_tile_matrix_set SET min_x = 0")]
    [InlineData("UPDATE gpkg_tile_matrix SET matrix_height = 3 WHERE zoom_level = 2")]
    [InlineData("DELETE FROM gpkg_tile_matrix_set; DELETE FROM gpkg_contents")]
    public void Tiles_on_another_grid_are_refused(string change)
    {
        var path = Write("other.gpkg");
        Execute(path, "PRAGMA foreign_keys = OFF; " + change);

        var error = Assert.Throws<InvalidDataException>(() => TileSource.Open(path));
        Assert.Contains("other.gpkg", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unfinished_export_leaves_the_existing_file()
    {
        var path = Write("replace.gpkg");
        using (var sink = new GeoPackageTileSink(path))
            sink.Write(1, 0, 0, [1, 2, 3]);

        Assert.Equal(Content(1, 0, 0), TileSource.Open(path).Read(1, 0, 0));
        Assert.False(File.Exists(TileSetFile.PartialPath(path)));
    }

    [Fact]
    public void An_exported_chart_holds_the_same_tiles_as_the_xyz_export()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var xyz = Path.Combine(_root, "xyz");
        var gpkg = Path.Combine(_root, "chart.gpkg");
        string[] common = ["--min-zoom", "12", "--max-zoom", "13"];
        Assert.Equal(0, CliApp.Build().Run(["tiles", "export", S57, "-o", xyz, .. common], TestContext.Current.CancellationToken));
        Assert.Equal(0, CliApp.Build().Run(["tiles", "export", S57, "-o", gpkg, .. common], TestContext.Current.CancellationToken));

        var source = TileSource.Open(gpkg);
        Assert.Equal(TileContainer.GeoPackage, source.Container);
        var files = Directory.GetFiles(xyz, "*.png", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var parts = Path.GetRelativePath(xyz, file).Split(Path.DirectorySeparatorChar);
            Assert.Equal(
                File.ReadAllBytes(file),
                source.Read(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(Path.GetFileNameWithoutExtension(parts[2]))));
        }

        Assert.Equal("day", (string?)source.ToTileJson()["s100"]!["palette"]);
    }
}
