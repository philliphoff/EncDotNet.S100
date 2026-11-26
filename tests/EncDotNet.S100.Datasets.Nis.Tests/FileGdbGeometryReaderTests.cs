using EncDotNet.S100.Datasets.Nis.FileGdb;

namespace EncDotNet.S100.Datasets.Nis.Tests;

/// <summary>Tests for <see cref="FileGdbGeometryReader"/> on hand-built shape buffers.</summary>
public sealed class FileGdbGeometryReaderTests
{
    // Origin 0, 10 units a degree, so coordinates are tenths of a degree.
    private static readonly FileGdbGeometryInfo Info = new("GEOGCS[\"GCS_WGS_1984\"]", HasZ: true, HasM: false, 0, 0, 10, 0, 100);

    private static byte[] VarUInt(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            bytes.Add(b);
        }
        while (value != 0);
        return [.. bytes];
    }

    private static byte[] VarInt(long value)
    {
        ulong magnitude = (ulong)Math.Abs(value);
        var bytes = new List<byte>();
        byte first = (byte)(magnitude & 0x3F);
        if (value < 0) first |= 0x40;
        magnitude >>= 6;
        if (magnitude != 0) first |= 0x80;
        bytes.Add(first);
        while (magnitude != 0)
        {
            byte b = (byte)(magnitude & 0x7F);
            magnitude >>= 7;
            if (magnitude != 0) b |= 0x80;
            bytes.Add(b);
        }
        return [.. bytes];
    }

    [Fact]
    public void Reads_a_point_with_z()
    {
        // Shape type 9 (point Z): x = 1.5, y = 0, z = 7.25, each stored plus one.
        byte[] blob = [.. VarUInt(9), .. VarUInt(15 + 1), .. VarUInt(0 + 1), .. VarUInt(725 + 1)];

        var geometry = FileGdbGeometryReader.Read(blob, Info);

        Assert.NotNull(geometry);
        Assert.Equal(NisGeometryKind.Point, geometry.Kind);
        var p = Assert.Single(Assert.Single(geometry.Parts));
        Assert.Equal(1.5, p.Longitude, 9);
        Assert.Equal(0.0, p.Latitude, 9);
        Assert.Equal(7.25, p.Z!.Value, 9);
    }

    [Fact]
    public void Reads_a_two_part_polyline_from_coordinate_differences()
    {
        // Shape type 3 (polyline), 4 points in 2 parts of 2; deltas include a
        // negative step and a multi-byte value (100 = 10 degrees).
        byte[] blob =
        [
            .. VarUInt(3), .. VarUInt(4), .. VarUInt(2),
            .. VarUInt(0), .. VarUInt(0), .. VarUInt(0), .. VarUInt(0), // extent, not used
            .. VarUInt(2), // first part's size
            .. VarInt(10), .. VarInt(20),
            .. VarInt(100), .. VarInt(-5),
            .. VarInt(-30), .. VarInt(0),
            .. VarInt(1), .. VarInt(1),
            .. VarInt(0), .. VarInt(0), .. VarInt(0), .. VarInt(0), // Z
        ];

        var geometry = FileGdbGeometryReader.Read(blob, Info with { HasZ = false });

        Assert.NotNull(geometry);
        Assert.Equal(NisGeometryKind.Line, geometry.Kind);
        Assert.Equal(2, geometry.Parts.Count);
        Assert.Equal([(1.0, 2.0), (11.0, 1.5)], geometry.Parts[0].Select(p => (p.Longitude, p.Latitude)).ToArray());
        Assert.Equal([(8.0, 1.5), (8.1, 1.6)], geometry.Parts[1].Select(p => (Math.Round(p.Longitude, 9), Math.Round(p.Latitude, 9))).ToArray());
    }

    [Fact]
    public void An_empty_point_has_no_geometry()
        => Assert.Null(FileGdbGeometryReader.Read([.. VarUInt(1), .. VarUInt(0), .. VarUInt(0)], Info));
}
