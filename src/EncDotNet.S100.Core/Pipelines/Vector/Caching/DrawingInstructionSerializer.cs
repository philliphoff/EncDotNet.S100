using System.IO;
using System.Text;

namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// Compact, order-preserving binary (de)serializer for a list of
/// <see cref="DrawingInstruction"/>. Used by
/// <see cref="DiskPortrayalInstructionCache"/> to persist the post-pipeline
/// display list across process restarts.
/// </summary>
/// <remarks>
/// <para>
/// The frame is <c>[FormatVersion:int][count:int]</c> then, per instruction,
/// <c>[typeTag:byte]</c> followed by the base fields and the type-specific
/// fields. Order is preserved exactly: the list is written and read as a
/// sequence, never sorted or de-duplicated, because the input order is the
/// renderer's final priority tie-breaker (<c>VectorPipeline.SortByPriority</c>
/// is a stable sort that falls back to input order).
/// </para>
/// <para>
/// Increment <see cref="FormatVersion"/> whenever the
/// <see cref="DrawingInstruction"/> shape or this frame changes; a mismatched
/// version read is treated as a miss by the disk cache (and the persisted
/// scope key also folds the version in, so stale files are never reused).
/// </para>
/// </remarks>
public static class DrawingInstructionSerializer
{
    /// <summary>
    /// Version stamp for the serialization frame. Bump on any change to the
    /// frame layout or the <see cref="DrawingInstruction"/> field set.
    /// </summary>
    public const int FormatVersion = 1;

    private const byte TagPoint = 1;
    private const byte TagLine = 2;
    private const byte TagArea = 3;
    private const byte TagText = 4;

    /// <summary>Serializes <paramref name="instructions"/> into the binary frame.</summary>
    /// <param name="instructions">The post-pipeline display list to persist.</param>
    /// <returns>The serialized bytes.</returns>
    public static byte[] Serialize(IReadOnlyList<DrawingInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FormatVersion);
            w.Write(instructions.Count);
            foreach (var instruction in instructions)
                WriteInstruction(w, instruction);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a list previously produced by <see cref="Serialize"/>.
    /// Returns <see langword="null"/> when the bytes are truncated, corrupt, or
    /// carry a mismatched <see cref="FormatVersion"/> — the caller treats that
    /// as a cache miss.
    /// </summary>
    /// <param name="bytes">The serialized bytes.</param>
    /// <returns>The deserialized list, or <see langword="null"/> on any failure.</returns>
    public static IReadOnlyList<DrawingInstruction>? TryDeserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var version = r.ReadInt32();
            if (version != FormatVersion)
                return null;

            var count = r.ReadInt32();
            if (count < 0)
                return null;

            var list = new List<DrawingInstruction>(count);
            for (var i = 0; i < count; i++)
                list.Add(ReadInstruction(r));

            return list;
        }
        catch
        {
            // Truncated / corrupt frame: treat as a miss.
            return null;
        }
    }

    private static void WriteInstruction(BinaryWriter w, DrawingInstruction instruction)
    {
        switch (instruction)
        {
            case PointInstruction p:
                w.Write(TagPoint);
                WriteBase(w, p);
                WriteString(w, p.SymbolReference);
                w.Write(p.SymbolScale);
                WriteNullableDouble(w, p.Rotation);
                w.Write(p.LocalOffsetX);
                w.Write(p.LocalOffsetY);
                WriteNullableDouble(w, p.LinePlacementPosition);
                WriteNullableCoordinate(w, p.CoordinateOverride);
                break;

            case LineInstruction l:
                w.Write(TagLine);
                WriteBase(w, l);
                WriteString(w, l.LineStyleReference);
                w.Write(l.LineWidth);
                WriteString(w, l.LineColor);
                WriteOffsetLengthList(w, l.Dashes);
                w.Write(l.DashOnLengthMm);
                WriteCoordinateList(w, l.CoordinatesOverride);
                break;

            case AreaInstruction a:
                w.Write(TagArea);
                WriteBase(w, a);
                WriteString(w, a.AreaFillReference);
                WriteString(w, a.FillColor);
                WriteNullableDouble(w, a.Transparency);
                WriteString(w, a.OutlineStyleReference);
                break;

            case TextInstruction t:
                w.Write(TagText);
                WriteBase(w, t);
                w.Write(t.Text);
                WriteString(w, t.FontReference);
                w.Write(t.FontSize);
                w.Write(t.FontColor);
                WriteNullableDouble(w, t.FontTransparency);
                WriteString(w, t.BackgroundColor);
                WriteNullableDouble(w, t.BackgroundTransparency);
                WriteNullableDouble(w, t.Rotation);
                WriteNullableDouble(w, t.LinePlacementPosition);
                w.Write((int)t.HorizontalAlignment);
                w.Write((int)t.VerticalAlignment);
                WriteNullableDouble(w, t.OffsetXmm);
                WriteNullableDouble(w, t.OffsetYmm);
                WriteNullableDouble(w, t.LineStartOffset);
                WriteNullableDouble(w, t.LineEndOffset);
                WriteNullableInt(w, t.LineOffsetMode is { } mode ? (int)mode : null);
                WriteNullableCoordinate(w, t.CoordinateOverride);
                break;

            default:
                throw new NotSupportedException(
                    $"Unknown drawing instruction type '{instruction.GetType().Name}'.");
        }
    }

    private static DrawingInstruction ReadInstruction(BinaryReader r)
    {
        var tag = r.ReadByte();
        return tag switch
        {
            TagPoint => ReadPoint(r),
            TagLine => ReadLine(r),
            TagArea => ReadArea(r),
            TagText => ReadText(r),
            _ => throw new InvalidDataException($"Unknown instruction tag {tag}."),
        };
    }

    private static PointInstruction ReadPoint(BinaryReader r)
    {
        var (featureRef, plane, vg, priority, scaleMin, scaleMax) = ReadBase(r);
        return new PointInstruction
        {
            FeatureReference = featureRef,
            Plane = plane,
            ViewingGroup = vg,
            DrawingPriority = priority,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            SymbolReference = ReadString(r),
            SymbolScale = r.ReadDouble(),
            Rotation = ReadNullableDouble(r),
            LocalOffsetX = r.ReadDouble(),
            LocalOffsetY = r.ReadDouble(),
            LinePlacementPosition = ReadNullableDouble(r),
            CoordinateOverride = ReadNullableCoordinate(r),
        };
    }

    private static LineInstruction ReadLine(BinaryReader r)
    {
        var (featureRef, plane, vg, priority, scaleMin, scaleMax) = ReadBase(r);
        return new LineInstruction
        {
            FeatureReference = featureRef,
            Plane = plane,
            ViewingGroup = vg,
            DrawingPriority = priority,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            LineStyleReference = ReadString(r),
            LineWidth = r.ReadDouble(),
            LineColor = ReadString(r),
            Dashes = ReadOffsetLengthList(r),
            DashOnLengthMm = r.ReadDouble(),
            CoordinatesOverride = ReadCoordinateList(r),
        };
    }

    private static AreaInstruction ReadArea(BinaryReader r)
    {
        var (featureRef, plane, vg, priority, scaleMin, scaleMax) = ReadBase(r);
        return new AreaInstruction
        {
            FeatureReference = featureRef,
            Plane = plane,
            ViewingGroup = vg,
            DrawingPriority = priority,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            AreaFillReference = ReadString(r),
            FillColor = ReadString(r),
            Transparency = ReadNullableDouble(r),
            OutlineStyleReference = ReadString(r),
        };
    }

    private static TextInstruction ReadText(BinaryReader r)
    {
        var (featureRef, plane, vg, priority, scaleMin, scaleMax) = ReadBase(r);
        return new TextInstruction
        {
            FeatureReference = featureRef,
            Plane = plane,
            ViewingGroup = vg,
            DrawingPriority = priority,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            Text = r.ReadString(),
            FontReference = ReadString(r),
            FontSize = r.ReadDouble(),
            FontColor = r.ReadString(),
            FontTransparency = ReadNullableDouble(r),
            BackgroundColor = ReadString(r),
            BackgroundTransparency = ReadNullableDouble(r),
            Rotation = ReadNullableDouble(r),
            LinePlacementPosition = ReadNullableDouble(r),
            HorizontalAlignment = (TextHorizontalAlignment)r.ReadInt32(),
            VerticalAlignment = (TextVerticalAlignment)r.ReadInt32(),
            OffsetXmm = ReadNullableDouble(r),
            OffsetYmm = ReadNullableDouble(r),
            LineStartOffset = ReadNullableDouble(r),
            LineEndOffset = ReadNullableDouble(r),
            LineOffsetMode = ReadNullableInt(r) is { } mode ? (LinePlacementMode)mode : null,
            CoordinateOverride = ReadNullableCoordinate(r),
        };
    }

    private static void WriteBase(BinaryWriter w, DrawingInstruction instruction)
    {
        w.Write(instruction.FeatureReference);
        w.Write((int)instruction.Plane);
        w.Write(instruction.ViewingGroup);
        w.Write(instruction.DrawingPriority);
        WriteNullableDouble(w, instruction.ScaleMinimum);
        WriteNullableDouble(w, instruction.ScaleMaximum);
    }

    private static (string FeatureReference, DisplayPlane Plane, int ViewingGroup,
        int DrawingPriority, double? ScaleMinimum, double? ScaleMaximum) ReadBase(BinaryReader r)
    {
        var featureRef = r.ReadString();
        var plane = (DisplayPlane)r.ReadInt32();
        var vg = r.ReadInt32();
        var priority = r.ReadInt32();
        var scaleMin = ReadNullableDouble(r);
        var scaleMax = ReadNullableDouble(r);
        return (featureRef, plane, vg, priority, scaleMin, scaleMax);
    }

    private static void WriteString(BinaryWriter w, string? value)
    {
        if (value is null)
        {
            w.Write(false);
        }
        else
        {
            w.Write(true);
            w.Write(value);
        }
    }

    private static string? ReadString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    private static void WriteNullableDouble(BinaryWriter w, double? value)
    {
        if (value is { } v)
        {
            w.Write(true);
            w.Write(v);
        }
        else
        {
            w.Write(false);
        }
    }

    private static double? ReadNullableDouble(BinaryReader r) => r.ReadBoolean() ? r.ReadDouble() : null;

    private static void WriteNullableInt(BinaryWriter w, int? value)
    {
        if (value is { } v)
        {
            w.Write(true);
            w.Write(v);
        }
        else
        {
            w.Write(false);
        }
    }

    private static int? ReadNullableInt(BinaryReader r) => r.ReadBoolean() ? r.ReadInt32() : null;

    private static void WriteNullableCoordinate(BinaryWriter w, (double Latitude, double Longitude)? value)
    {
        if (value is { } v)
        {
            w.Write(true);
            w.Write(v.Latitude);
            w.Write(v.Longitude);
        }
        else
        {
            w.Write(false);
        }
    }

    private static (double Latitude, double Longitude)? ReadNullableCoordinate(BinaryReader r)
    {
        if (!r.ReadBoolean())
            return null;
        var lat = r.ReadDouble();
        var lon = r.ReadDouble();
        return (lat, lon);
    }

    private static void WriteCoordinateList(BinaryWriter w, IReadOnlyList<(double Latitude, double Longitude)>? list)
    {
        if (list is null)
        {
            w.Write(-1);
            return;
        }

        w.Write(list.Count);
        foreach (var (lat, lon) in list)
        {
            w.Write(lat);
            w.Write(lon);
        }
    }

    private static IReadOnlyList<(double Latitude, double Longitude)>? ReadCoordinateList(BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count < 0)
            return null;

        var list = new List<(double, double)>(count);
        for (var i = 0; i < count; i++)
        {
            var lat = r.ReadDouble();
            var lon = r.ReadDouble();
            list.Add((lat, lon));
        }

        return list;
    }

    private static void WriteOffsetLengthList(BinaryWriter w, IReadOnlyList<(double Offset, double Length)>? list)
    {
        if (list is null)
        {
            w.Write(-1);
            return;
        }

        w.Write(list.Count);
        foreach (var (offset, length) in list)
        {
            w.Write(offset);
            w.Write(length);
        }
    }

    private static IReadOnlyList<(double Offset, double Length)>? ReadOffsetLengthList(BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count < 0)
            return null;

        var list = new List<(double, double)>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = r.ReadDouble();
            var length = r.ReadDouble();
            list.Add((offset, length));
        }

        return list;
    }
}
