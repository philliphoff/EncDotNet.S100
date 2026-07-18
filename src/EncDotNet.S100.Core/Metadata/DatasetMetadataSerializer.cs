using System.Text;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Core.Metadata;

/// <summary>
/// Binary (de)serializer for <see cref="DatasetMetadata"/>, used by the
/// cross-session metadata sidecar cache (issue #467 WS3) so a dataset's
/// cheap "peek" facts — declared spec, extent, CRS, display-scale window,
/// and temporal coverage — survive a process restart and need not be
/// re-parsed from the dataset on a later session.
/// </summary>
/// <remarks>
/// <para>
/// The frame is a small fixed schema:
/// <c>[FormatVersion:int]</c> then the spec name (length-prefixed UTF-8)
/// and edition (three ints), followed by presence-flagged optionals for
/// the extent (four doubles), horizontal CRS EPSG (int), display-scale
/// window (two nullable ints), and temporal coverage (two UTC tick
/// counts). Every optional is guarded by a leading <see cref="bool"/> so
/// a <c>null</c> field costs one byte and never ambiguously aliases a real
/// value.
/// </para>
/// <para>
/// Mirroring <c>DrawingInstructionSerializer</c>, deserialization is
/// total: a truncated, corrupt, or version-mismatched frame yields
/// <see langword="null"/> rather than throwing, so a stale or damaged
/// sidecar degrades to a cache miss. Increment <see cref="FormatVersion"/>
/// whenever this schema changes so old sidecars are rejected.
/// </para>
/// </remarks>
public static class DatasetMetadataSerializer
{
    /// <summary>
    /// Schema version stamped into every frame. Bump on any change to the
    /// serialized layout so previously persisted sidecars are treated as a
    /// miss instead of being misread.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>Serializes <paramref name="metadata"/> into the binary frame.</summary>
    /// <param name="metadata">The metadata to persist.</param>
    /// <returns>The serialized bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is null.</exception>
    public static byte[] Serialize(DatasetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FormatVersion);

            w.Write(metadata.Spec.Name);
            w.Write(metadata.Spec.Edition.Major);
            w.Write(metadata.Spec.Edition.Minor);
            w.Write(metadata.Spec.Edition.Clarification);

            WriteOptional(w, metadata.Extent is not null, () =>
            {
                w.Write(metadata.Extent!.SouthLatitude);
                w.Write(metadata.Extent.WestLongitude);
                w.Write(metadata.Extent.NorthLatitude);
                w.Write(metadata.Extent.EastLongitude);
            });

            WriteNullableInt(w, metadata.HorizontalCrsEpsg);

            WriteOptional(w, metadata.DisplayScale is not null, () =>
            {
                WriteNullableInt(w, metadata.DisplayScale!.Value.Minimum);
                WriteNullableInt(w, metadata.DisplayScale.Value.Maximum);
            });

            WriteOptional(w, metadata.TimeCoverage is not null, () =>
            {
                w.Write(metadata.TimeCoverage!.Value.Start.ToUniversalTime().Ticks);
                w.Write(metadata.TimeCoverage.Value.End.ToUniversalTime().Ticks);
            });
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes metadata previously produced by <see cref="Serialize"/>.
    /// Returns <see langword="null"/> when the bytes are truncated, corrupt,
    /// or carry a mismatched <see cref="FormatVersion"/> — the caller treats
    /// that as a cache miss.
    /// </summary>
    /// <param name="bytes">The serialized bytes.</param>
    /// <returns>The deserialized metadata, or <see langword="null"/> on any failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
    public static DatasetMetadata? TryDeserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var version = r.ReadInt32();
            if (version != FormatVersion)
                return null;

            var name = ReadBoundedString(r);
            var edition = new SpecVersion(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            var spec = new SpecRef(name, edition);

            BoundingBox? extent = null;
            if (r.ReadBoolean())
            {
                extent = new BoundingBox(
                    r.ReadDouble(),
                    r.ReadDouble(),
                    r.ReadDouble(),
                    r.ReadDouble());
            }

            var crs = ReadNullableInt(r);

            DisplayScaleRange? displayScale = null;
            if (r.ReadBoolean())
                displayScale = new DisplayScaleRange(ReadNullableInt(r), ReadNullableInt(r));

            TimeCoverage? timeCoverage = null;
            if (r.ReadBoolean())
            {
                var start = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
                var end = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
                timeCoverage = new TimeCoverage(start, end);
            }

            return new DatasetMetadata
            {
                Spec = spec,
                Extent = extent,
                HorizontalCrsEpsg = crs,
                DisplayScale = displayScale,
                TimeCoverage = timeCoverage,
            };
        }
        catch
        {
            // Truncated / corrupt frame (including an out-of-range edition or
            // an unrecognised spec name that SpecRef rejects): treat as a miss.
            return null;
        }
    }

    private static void WriteOptional(BinaryWriter w, bool present, Action writeBody)
    {
        w.Write(present);
        if (present)
            writeBody();
    }

    private static void WriteNullableInt(BinaryWriter w, int? value)
    {
        w.Write(value.HasValue);
        if (value.HasValue)
            w.Write(value.Value);
    }

    private static int? ReadNullableInt(BinaryReader r)
        => r.ReadBoolean() ? r.ReadInt32() : null;

    /// <summary>
    /// Reads a length-prefixed UTF-8 string, rejecting a length prefix larger
    /// than the bytes remaining in the (in-memory, size-framed) payload before
    /// allocating — so a corrupt sidecar degrades to a cache miss rather than a
    /// large allocation. Wire-compatible with <see cref="BinaryWriter.Write(string)"/>.
    /// </summary>
    private static string ReadBoundedString(BinaryReader r)
    {
        var byteLength = r.Read7BitEncodedInt();
        if (byteLength < 0 || byteLength > r.BaseStream.Length - r.BaseStream.Position)
            throw new InvalidDataException("String length prefix exceeds remaining payload bytes.");

        var bytes = r.ReadBytes(byteLength);
        if (bytes.Length != byteLength)
            throw new EndOfStreamException();

        return Encoding.UTF8.GetString(bytes);
    }
}
