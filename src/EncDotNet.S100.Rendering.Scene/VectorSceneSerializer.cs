using System.Text;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using NetTopologySuite.IO;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Writes a <see cref="VectorScene"/> to a compact binary form and reads it
/// back, so a cell's scene can be stored on disk and drawn in a later session
/// before the cell is parsed (map rendering slice 11, cell scenes on disk).
/// </summary>
/// <remarks>
/// <para>
/// Every op type, <see cref="VectorScene.ScaleMinimumCap"/> and
/// <see cref="VectorScene.PreferLiveDrawing"/> round-trip exactly: coordinates
/// and sizes are written as the same doubles and floats, so a read scene
/// rasterises to the same pixels as the scene that was written.
/// </para>
/// <para>
/// Resolved resources are stored once per scene, by name: the strings table
/// holds each distinct feature reference, text, pattern name and processed
/// symbol SVG once, and each pattern's tile once, however many ops use it. Ops
/// refer to them by index. A read scene shares one instance of each string
/// and tile, as the builder's asset caches do. Placement areas are stored once
/// each as WKB.
/// </para>
/// <para>
/// The spatial index is not stored: a renderer builds it when the scene is
/// bound, from the read ops, as it does for a newly built scene.
/// </para>
/// </remarks>
public static class VectorSceneSerializer
{
    /// <summary>
    /// The format version. Bump it when the layout changes; a reader refuses
    /// any other version.
    /// </summary>
    /// <remarks>Version 2 stores each text op's <see cref="TextPaintOp.FontSlant"/>.</remarks>
    public const int FormatVersion = 2;

    private const uint Magic = 0x4E435353; // "SSCN" little-endian

    private const byte TagPoint = 1;
    private const byte TagLine = 2;
    private const byte TagArea = 3;
    private const byte TagPattern = 4;
    private const byte TagText = 5;

    private const byte HasScaleMinimum = 1;
    private const byte HasScaleMaximum = 2;

    /// <summary>Serializes <paramref name="scene"/>.</summary>
    /// <param name="scene">The scene to write.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Serialize(VectorScene scene)
    {
        using var stream = new MemoryStream();
        Write(stream, scene);
        return stream.ToArray();
    }

    /// <summary>Writes <paramref name="scene"/> to <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="scene">The scene to write.</param>
    public static void Write(Stream stream, VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(scene);

        var tables = new WriteTables();
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            w.Write7BitEncodedInt(scene.Ops.Count);
            foreach (var op in scene.Ops)
            {
                WriteOp(w, op, tables);
            }
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write7BitEncodedInt(FormatVersion);
        WriteNullable(writer, scene.ScaleMinimumCap);
        writer.Write(scene.PreferLiveDrawing);

        writer.Write7BitEncodedInt(tables.Strings.Count);
        foreach (var s in tables.Strings)
        {
            writer.Write(s);
        }

        writer.Write7BitEncodedInt(tables.Blobs.Count);
        foreach (var blob in tables.Blobs)
        {
            writer.Write7BitEncodedInt(blob.Length);
            writer.Write(blob);
        }

        var wkb = new WKBWriter();
        writer.Write7BitEncodedInt(tables.Areas.Count);
        foreach (var area in tables.Areas)
        {
            var bytes = wkb.Write(area.Area);
            writer.Write7BitEncodedInt(bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();
        body.Position = 0;
        body.CopyTo(stream);
    }

    /// <summary>Reads a scene written by <see cref="Serialize"/>.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="InvalidDataException">The bytes aren't a scene of this format version.</exception>
    public static VectorScene Deserialize(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        return Read(stream);
    }

    /// <summary>Reads a scene written by <see cref="Write"/> from <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream, positioned at the scene.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="InvalidDataException">The data isn't a scene of this format version.</exception>
    public static VectorScene Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("Not a stored vector scene.");
            }

            var version = r.Read7BitEncodedInt();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Stored vector scene format {version} is not {FormatVersion}.");
            }

            var cap = ReadNullableDouble(r);
            var preferLive = r.ReadBoolean();

            var strings = new string[ReadCount(r)];
            for (int i = 0; i < strings.Length; i++)
            {
                strings[i] = r.ReadString();
            }

            var blobs = new byte[ReadCount(r)][];
            for (int i = 0; i < blobs.Length; i++)
            {
                blobs[i] = r.ReadBytes(ReadCount(r));
            }

            var wkb = new WKBReader();
            var areas = new PlacementArea[ReadCount(r)];
            for (int i = 0; i < areas.Length; i++)
            {
                areas[i] = new PlacementArea(wkb.Read(r.ReadBytes(ReadCount(r))));
            }

            var tables = new ReadTables(strings, blobs, areas);
            var ops = new PaintOp[ReadCount(r)];
            for (int i = 0; i < ops.Length; i++)
            {
                ops[i] = ReadOp(r, tables);
            }

            return new VectorScene(ops) { ScaleMinimumCap = cap, PreferLiveDrawing = preferLive };
        }
        catch (Exception ex) when (ex is EndOfStreamException or IndexOutOfRangeException or ArgumentException or ParseException or FormatException)
        {
            throw new InvalidDataException("The stored vector scene is truncated or corrupt.", ex);
        }
    }

    // ------------------------------------------------------------------
    // Writing.

    private static void WriteOp(BinaryWriter w, PaintOp op, WriteTables t)
    {
        switch (op)
        {
            case PointPaintOp point:
                WriteHeader(w, TagPoint, op, t);
                WritePoint(w, point.World);
                WriteArea(w, point.PlacementArea, t);
                w.Write(point.Symbol.HasValue);
                if (point.Symbol is { } symbol)
                {
                    WriteSymbol(w, symbol, t);
                }

                WriteColor(w, point.FallbackColor);
                w.Write(point.FallbackScale);
                WriteNullable(w, point.Rotation);
                w.Write7BitEncodedInt((int)point.RotationCrs);
                w.Write(point.OffsetXpx);
                w.Write(point.OffsetYpx);
                break;

            case LinePaintOp line:
                WriteHeader(w, TagLine, op, t);
                WriteRing(w, line.World);
                WriteColor(w, line.Color);
                w.Write(line.WidthPx);
                if (line.DashArrayPx is { } dashes)
                {
                    w.Write7BitEncodedInt(dashes.Count + 1);
                    foreach (var dash in dashes)
                    {
                        w.Write(dash);
                    }
                }
                else
                {
                    w.Write7BitEncodedInt(0);
                }

                w.Write(line.DefaultDash);
                w.Write(line.DashPhasePx);
                w.Write(line.Stroke);
                w.Write7BitEncodedInt(line.Symbols.Count);
                foreach (var placed in line.Symbols)
                {
                    WriteSymbol(w, placed.Symbol, t);
                    w.Write(placed.PositionPx);
                    w.Write(placed.RotationDegrees);
                }

                w.Write(line.SymbolIntervalPx);
                break;

            case AreaPaintOp area:
                WriteHeader(w, TagArea, op, t);
                WriteRing(w, area.WorldShell);
                WriteRings(w, area.WorldHoles);
                WriteColor(w, area.Fill);
                WriteColor(w, area.OutlineColor);
                w.Write(area.OutlineWidthPx);
                break;

            case PatternAreaPaintOp pattern:
                WriteHeader(w, TagPattern, op, t);
                w.Write7BitEncodedInt(t.String(pattern.PatternReference));
                WriteRing(w, pattern.WorldShell);
                WriteRings(w, pattern.WorldHoles);
                w.Write7BitEncodedInt(t.Blob(pattern.TilePng));
                break;

            case TextPaintOp text:
                WriteHeader(w, TagText, op, t);
                WritePoint(w, text.World);
                WriteArea(w, text.PlacementArea, t);
                w.Write7BitEncodedInt(t.String(text.Text));
                w.Write(text.FontSizePx);
                WriteColor(w, text.ForeColor);
                WriteNullable(w, text.BackColor);
                w.Write7BitEncodedInt((int)text.HorizontalAlignment);
                w.Write7BitEncodedInt((int)text.VerticalAlignment);
                w.Write(text.OffsetXpx);
                w.Write(text.OffsetYpx);
                w.Write7BitEncodedInt((int)text.FontWeight);
                w.Write7BitEncodedInt((int)text.FontProportion);
                WriteNullable(w, text.HaloColor);
                w.Write(text.HaloWidthPx);
                w.Write7BitEncodedInt((int)text.FontSlant);
                break;

            default:
                throw new NotSupportedException($"Paint op '{op.GetType().Name}' can't be stored.");
        }
    }

    private static void WriteHeader(BinaryWriter w, byte tag, PaintOp op, WriteTables t)
    {
        w.Write(tag);
        w.Write7BitEncodedInt(t.String(op.FeatureReference));
        byte flags = 0;
        if (op.ScaleMinimum.HasValue)
        {
            flags |= HasScaleMinimum;
        }

        if (op.ScaleMaximum.HasValue)
        {
            flags |= HasScaleMaximum;
        }

        w.Write(flags);
        if (op.ScaleMinimum is { } min)
        {
            w.Write(min);
        }

        if (op.ScaleMaximum is { } max)
        {
            w.Write(max);
        }
    }

    private static void WriteSymbol(BinaryWriter w, ResolvedSymbol symbol, WriteTables t)
    {
        w.Write7BitEncodedInt(t.String(symbol.ProcessedSvg));
        w.Write(symbol.Scale);
        w.Write(symbol.PivotRelativeX);
        w.Write(symbol.PivotRelativeY);
    }

    private static void WriteArea(BinaryWriter w, PlacementArea? area, WriteTables t) =>
        w.Write7BitEncodedInt(area is null ? 0 : t.Area(area) + 1);

    private static void WritePoint(BinaryWriter w, (double X, double Y) p)
    {
        w.Write(p.X);
        w.Write(p.Y);
    }

    private static void WriteRing(BinaryWriter w, IReadOnlyList<(double X, double Y)> ring)
    {
        w.Write7BitEncodedInt(ring.Count);
        foreach (var p in ring)
        {
            w.Write(p.X);
            w.Write(p.Y);
        }
    }

    private static void WriteRings(BinaryWriter w, IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings)
    {
        w.Write7BitEncodedInt(rings.Count);
        foreach (var ring in rings)
        {
            WriteRing(w, ring);
        }
    }

    private static void WriteColor(BinaryWriter w, RgbaColor c)
    {
        w.Write(c.R);
        w.Write(c.G);
        w.Write(c.B);
        w.Write(c.A);
    }

    private static void WriteNullable(BinaryWriter w, RgbaColor? c)
    {
        w.Write(c.HasValue);
        if (c is { } color)
        {
            WriteColor(w, color);
        }
    }

    private static void WriteNullable(BinaryWriter w, double? value)
    {
        w.Write(value.HasValue);
        if (value is { } v)
        {
            w.Write(v);
        }
    }

    // ------------------------------------------------------------------
    // Reading.

    private static PaintOp ReadOp(BinaryReader r, ReadTables t)
    {
        var tag = r.ReadByte();
        var featureReference = t.String(r);
        var flags = r.ReadByte();
        double? scaleMinimum = (flags & HasScaleMinimum) != 0 ? r.ReadDouble() : null;
        double? scaleMaximum = (flags & HasScaleMaximum) != 0 ? r.ReadDouble() : null;

        switch (tag)
        {
            case TagPoint:
                {
                    var world = ReadPoint(r);
                    var area = t.Area(r);
                    ResolvedSymbol? symbol = r.ReadBoolean() ? ReadSymbol(r, t) : null;
                    return new PointPaintOp
                    {
                        FeatureReference = featureReference,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                        World = world,
                        PlacementArea = area,
                        Symbol = symbol,
                        FallbackColor = ReadColor(r),
                        FallbackScale = r.ReadDouble(),
                        Rotation = ReadNullableDouble(r),
                        RotationCrs = (SymbolRotationCrs)r.Read7BitEncodedInt(),
                        OffsetXpx = r.ReadDouble(),
                        OffsetYpx = r.ReadDouble(),
                    };
                }

            case TagLine:
                {
                    var world = ReadRing(r);
                    var color = ReadColor(r);
                    var width = r.ReadDouble();
                    float[]? dashes = null;
                    var dashCount = ReadCount(r);
                    if (dashCount > 0)
                    {
                        dashes = new float[dashCount - 1];
                        for (int i = 0; i < dashes.Length; i++)
                        {
                            dashes[i] = r.ReadSingle();
                        }
                    }

                    var defaultDash = r.ReadBoolean();
                    var phase = r.ReadDouble();
                    var stroke = r.ReadBoolean();
                    var symbols = new LineSymbolPlacement[ReadCount(r)];
                    for (int i = 0; i < symbols.Length; i++)
                    {
                        symbols[i] = new LineSymbolPlacement(ReadSymbol(r, t), r.ReadDouble(), r.ReadDouble());
                    }

                    return new LinePaintOp
                    {
                        FeatureReference = featureReference,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                        World = world,
                        Color = color,
                        WidthPx = width,
                        DashArrayPx = dashes,
                        DefaultDash = defaultDash,
                        DashPhasePx = phase,
                        Stroke = stroke,
                        Symbols = symbols,
                        SymbolIntervalPx = r.ReadDouble(),
                    };
                }

            case TagArea:
                return new AreaPaintOp
                {
                    FeatureReference = featureReference,
                    ScaleMinimum = scaleMinimum,
                    ScaleMaximum = scaleMaximum,
                    WorldShell = ReadRing(r),
                    WorldHoles = ReadRings(r),
                    Fill = ReadColor(r),
                    OutlineColor = ReadColor(r),
                    OutlineWidthPx = r.ReadDouble(),
                };

            case TagPattern:
                return new PatternAreaPaintOp
                {
                    FeatureReference = featureReference,
                    ScaleMinimum = scaleMinimum,
                    ScaleMaximum = scaleMaximum,
                    PatternReference = t.String(r),
                    WorldShell = ReadRing(r),
                    WorldHoles = ReadRings(r),
                    TilePng = t.Blob(r),
                };

            case TagText:
                return new TextPaintOp
                {
                    FeatureReference = featureReference,
                    ScaleMinimum = scaleMinimum,
                    ScaleMaximum = scaleMaximum,
                    World = ReadPoint(r),
                    PlacementArea = t.Area(r),
                    Text = t.String(r),
                    FontSizePx = r.ReadDouble(),
                    ForeColor = ReadColor(r),
                    BackColor = ReadNullableColor(r),
                    HorizontalAlignment = (TextHorizontalAlignment)r.Read7BitEncodedInt(),
                    VerticalAlignment = (TextVerticalAlignment)r.Read7BitEncodedInt(),
                    OffsetXpx = r.ReadDouble(),
                    OffsetYpx = r.ReadDouble(),
                    FontWeight = (TextFontWeight)r.Read7BitEncodedInt(),
                    FontProportion = (TextFontProportion)r.Read7BitEncodedInt(),
                    HaloColor = ReadNullableColor(r),
                    HaloWidthPx = r.ReadDouble(),
                    FontSlant = (TextFontSlant)r.Read7BitEncodedInt(),
                };

            default:
                throw new InvalidDataException($"Unknown stored paint op tag {tag}.");
        }
    }

    private static ResolvedSymbol ReadSymbol(BinaryReader r, ReadTables t) =>
        new(t.String(r), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

    private static (double X, double Y) ReadPoint(BinaryReader r) => (r.ReadDouble(), r.ReadDouble());

    private static (double X, double Y)[] ReadRing(BinaryReader r)
    {
        var ring = new (double X, double Y)[ReadCount(r)];
        for (int i = 0; i < ring.Length; i++)
        {
            ring[i] = (r.ReadDouble(), r.ReadDouble());
        }

        return ring;
    }

    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> ReadRings(BinaryReader r)
    {
        var count = ReadCount(r);
        if (count == 0)
        {
            return [];
        }

        var rings = new IReadOnlyList<(double X, double Y)>[count];
        for (int i = 0; i < count; i++)
        {
            rings[i] = ReadRing(r);
        }

        return rings;
    }

    private static RgbaColor ReadColor(BinaryReader r) => new(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());

    private static RgbaColor? ReadNullableColor(BinaryReader r) => r.ReadBoolean() ? ReadColor(r) : null;

    private static double? ReadNullableDouble(BinaryReader r) => r.ReadBoolean() ? r.ReadDouble() : null;

    private static int ReadCount(BinaryReader r)
    {
        var count = r.Read7BitEncodedInt();
        if (count < 0 || count > r.BaseStream.Length)
        {
            throw new InvalidDataException("The stored vector scene has an impossible count.");
        }

        return count;
    }

    private sealed class WriteTables
    {
        private readonly Dictionary<string, int> _stringsByReference = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _stringsByValue = new(StringComparer.Ordinal);
        private readonly Dictionary<byte[], int> _blobs = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PlacementArea, int> _areas = new(ReferenceEqualityComparer.Instance);

        public List<string> Strings { get; } = [];

        public List<byte[]> Blobs { get; } = [];

        public List<PlacementArea> Areas { get; } = [];

        public int String(string value)
        {
            // Most repeats are the same instance (a symbol's SVG, a feature's
            // reference), so look those up without hashing the text.
            if (_stringsByReference.TryGetValue(value, out var index))
            {
                return index;
            }

            if (!_stringsByValue.TryGetValue(value, out index))
            {
                index = Strings.Count;
                Strings.Add(value);
                _stringsByValue.Add(value, index);
            }

            _stringsByReference[value] = index;
            return index;
        }

        public int Blob(byte[] value)
        {
            if (!_blobs.TryGetValue(value, out var index))
            {
                index = Blobs.Count;
                Blobs.Add(value);
                _blobs.Add(value, index);
            }

            return index;
        }

        public int Area(PlacementArea value)
        {
            if (!_areas.TryGetValue(value, out var index))
            {
                index = Areas.Count;
                Areas.Add(value);
                _areas.Add(value, index);
            }

            return index;
        }
    }

    private sealed class ReadTables(string[] strings, byte[][] blobs, PlacementArea[] areas)
    {
        public string String(BinaryReader r) => strings[r.Read7BitEncodedInt()];

        public byte[] Blob(BinaryReader r) => blobs[r.Read7BitEncodedInt()];

        public PlacementArea? Area(BinaryReader r)
        {
            var index = r.Read7BitEncodedInt();
            return index == 0 ? null : areas[index - 1];
        }
    }
}
