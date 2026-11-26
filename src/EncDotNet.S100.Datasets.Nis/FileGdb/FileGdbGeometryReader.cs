namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>
/// Decodes the shape buffers of a file geodatabase geometry field into
/// <see cref="NisGeometry"/>.
/// </summary>
/// <remarks>
/// A shape starts with its shape type, whose low byte is the Esri shape type
/// and whose high bits flag Z (0x80000000), M (0x40000000) and curves
/// (0x20000000) for the general types. A point stores its coordinates as
/// unsigned integers offset by one (zero means empty). Multipoints, polylines
/// and polygons store a point count, a part count, an extent, the size of
/// every part but the last, then each coordinate as a signed difference from
/// the one before, X and Y first and then every Z. M values and curve
/// descriptions follow and are not read: curved segments come back as the
/// straight lines between their vertices.
/// </remarks>
internal static class FileGdbGeometryReader
{
    private const uint ZFlag = 0x80000000;
    private const uint MFlag = 0x40000000;

    public static NisGeometry? Read(ReadOnlySpan<byte> blob, FileGdbGeometryInfo info)
    {
        if (blob.IsEmpty) return null;
        var reader = new SpanCursor(blob);
        uint type = (uint)reader.VarUInt();
        int shape = (int)(type & 0xFF);
        if (shape == 0) return null;
        bool hasZ = (type & ZFlag) != 0 || shape is 9 or 10 or 11 or 13 or 15 or 18 or 19 or 20;
        bool hasM = (type & MFlag) != 0 || shape is 11 or 13 or 15 or 18 or 21 or 23 or 25 or 28;
        _ = hasM;

        var kind = shape switch
        {
            1 or 9 or 11 or 21 or 52 => NisGeometryKind.Point,
            8 or 18 or 20 or 28 or 53 => NisGeometryKind.MultiPoint,
            3 or 10 or 13 or 23 or 50 => NisGeometryKind.Line,
            5 or 15 or 19 or 25 or 51 => NisGeometryKind.Area,
            _ => throw new InvalidDataException($"Unsupported shape type {shape}."),
        };

        if (kind == NisGeometryKind.Point)
        {
            ulong x = reader.VarUInt();
            if (x == 0) return null;
            ulong y = reader.VarUInt();
            double? z = null;
            if (hasZ && info.HasZ && !reader.AtEnd)
                z = (reader.VarUInt() - 1) / info.ZScale + info.ZOrigin;
            return NisGeometry.FromPoint(new NisPosition(
                (x - 1) / info.XYScale + info.XOrigin,
                (y - 1) / info.XYScale + info.YOrigin,
                z));
        }

        int points = checked((int)reader.VarUInt());
        if (points == 0) return null;
        int parts = kind == NisGeometryKind.MultiPoint ? 1 : checked((int)reader.VarUInt());
        for (int i = 0; i < 4; i++) reader.VarUInt(); // extent
        var counts = new int[parts];
        int counted = 0;
        for (int i = 0; i < parts - 1; i++)
        {
            counts[i] = checked((int)reader.VarUInt());
            counted += counts[i];
        }
        counts[parts - 1] = points - counted;

        var xs = new double[points];
        var ys = new double[points];
        long dx = 0, dy = 0;
        for (int i = 0; i < points; i++)
        {
            dx += reader.VarInt();
            dy += reader.VarInt();
            xs[i] = dx / info.XYScale + info.XOrigin;
            ys[i] = dy / info.XYScale + info.YOrigin;
        }
        double[]? zs = null;
        if (hasZ && info.HasZ)
        {
            zs = new double[points];
            long dz = 0;
            for (int i = 0; i < points; i++)
            {
                dz += reader.VarInt();
                zs[i] = dz / info.ZScale + info.ZOrigin;
            }
        }

        var result = new List<IReadOnlyList<NisPosition>>(parts);
        int at = 0;
        foreach (int count in counts)
        {
            var part = new NisPosition[count];
            for (int i = 0; i < count; i++, at++)
                part[i] = new NisPosition(xs[at], ys[at], zs?[at]);
            result.Add(part);
        }
        return new NisGeometry(kind, result);
    }
}
