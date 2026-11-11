using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.TropicalCyclones.Shapefiles;

namespace EncDotNet.S100.Datasets.TropicalCyclones.Tests;

/// <summary>Issue #1294: the managed SHP and DBF reader.</summary>
public sealed class ShapefileReaderTests
{
    private static IReadOnlyList<GeoPosition> Ring(params (double Lat, double Lon)[] points) =>
        [.. points.Select(p => new GeoPosition(p.Lat, p.Lon))];

    [Fact]
    public void Clockwise_rings_are_outer_and_counter_clockwise_ones_their_holes()
    {
        var outer = Ring((10, 0), (10, 10), (0, 10), (0, 0), (10, 0));      // clockwise
        var hole = Ring((2, 2), (2, 8), (8, 8), (8, 2), (2, 2));             // counter-clockwise, inside
        var other = Ring((30, 20), (30, 25), (25, 25), (25, 20), (30, 20));  // clockwise, apart

        var surfaces = ShapefileReader.GroupRings([outer, hole, other]);

        Assert.Equal(2, surfaces.Count);
        Assert.Same(hole, Assert.Single(surfaces[0].InteriorRings));
        Assert.Empty(surfaces[1].InteriorRings);
    }

    [Fact]
    public void Rings_that_all_run_counter_clockwise_are_read_as_outer_rings()
    {
        var a = Ring((0, 0), (0, 10), (10, 10), (10, 0), (0, 0));
        Assert.True(ShapefileReader.SignedArea(a) > 0);

        var surfaces = ShapefileReader.GroupRings([a]);

        Assert.Same(a, Assert.Single(surfaces).ExteriorRing);
    }

    [Fact]
    public void Reads_the_layers_of_an_NHC_zip()
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(TestSupport.TropicalCycloneFixtures.IanForecast);
        byte[] Bytes(string suffix)
        {
            using var stream = zip.Entries.Single(e => e.Name.EndsWith(suffix, StringComparison.Ordinal)).Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        var points = ShapefileReader.Read(Bytes("_5day_pts.shp"), Bytes("_5day_pts.dbf"));
        var line = ShapefileReader.Read(Bytes("_5day_lin.shp"), Bytes("_5day_lin.dbf"));
        var cone = ShapefileReader.Read(Bytes("_5day_pgn.shp"), Bytes("_5day_pgn.dbf"));

        Assert.Equal(8, points.Count);
        Assert.All(points, p => Assert.IsType<ShapePoint>(p.Geometry));
        Assert.Equal("Hurricane Ian", points[0].Text("STORMNAME"));
        Assert.Equal(952, points[0].Number("MSLP"));
        Assert.Null(points[1].Number("MSLP")); // 9999: not forecast.
        Assert.Null(points[0].Text("NOSUCHFIELD"));

        var track = Assert.IsType<ShapeLine>(Assert.Single(line).Geometry);
        Assert.Equal(8, Assert.Single(track.Parts).Count);

        var polygon = Assert.IsType<ShapePolygon>(Assert.Single(cone).Geometry);
        Assert.Single(polygon.Surfaces);
        Assert.Equal("Ian", cone[0].Text("STORMNAME"));
    }

    [Fact]
    public void Deleted_table_rows_are_skipped_with_their_shapes()
    {
        var shp = new byte[100 + (2 * 28)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(shp, 9994);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(shp.AsSpan(24), shp.Length / 2);
        for (var i = 0; i < 2; i++)
        {
            var at = 100 + (28 * i);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(shp.AsSpan(at), i + 1);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(shp.AsSpan(at + 4), 10);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(shp.AsSpan(at + 8), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(shp.AsSpan(at + 12), i);
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(shp.AsSpan(at + 20), -i);
        }

        // One character field, NAME, width 4; the first row deleted.
        var dbf = new byte[32 + 32 + 1 + (2 * 5)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(4), 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dbf.AsSpan(8), 65);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dbf.AsSpan(10), 5);
        "NAME"u8.CopyTo(dbf.AsSpan(32));
        dbf[43] = (byte)'C';
        dbf[48] = 4;
        dbf[64] = 0x0D;
        "*gone"u8.CopyTo(dbf.AsSpan(65));
        " kept"u8.CopyTo(dbf.AsSpan(70));

        var record = Assert.Single(ShapefileReader.Read(shp, dbf));

        Assert.Equal("kept", record.Text("name"));
        Assert.Equal(-1, Assert.IsType<ShapePoint>(record.Geometry).Position.Latitude);
    }

    [Fact]
    public void A_file_that_is_not_a_shapefile_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => ShapefileReader.Read(new byte[120], []));
}
