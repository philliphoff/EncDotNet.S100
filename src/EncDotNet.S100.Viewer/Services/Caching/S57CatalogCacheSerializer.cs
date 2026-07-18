using System.Text;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Viewer.Services.Caching;

/// <summary>
/// Binary (de)serializer for the base-cell descriptor list read from an
/// S-57 / S-63 exchange-set catalogue (<c>CATALOG.031</c>), used by the
/// cross-session catalogue sidecar cache (issue #467 WS3 Slice 2) so a large
/// set's descriptors survive a restart and need not be re-parsed from the
/// (binary ISO 8211) catalogue on a later session.
/// </summary>
/// <remarks>
/// <para>
/// The frame is <c>[FormatVersion:int][cellCount:int]</c> followed by one
/// record per cell: the cell name and base-cell relative path (each a
/// length-prefixed UTF-8 string), the ordered update relative paths
/// (<c>[count:int]</c> then that many strings), and a presence-flagged
/// bounding box (a leading <see cref="bool"/> then four doubles in
/// west/east/south/north order). Descriptors only — no dataset bytes are
/// stored, so the on-demand cell load always reads current file content.
/// </para>
/// <para>
/// Mirroring <c>DatasetMetadataSerializer</c>, deserialization is total: a
/// truncated, corrupt, or version-mismatched frame yields
/// <see langword="null"/> so a stale or damaged sidecar degrades to a cache
/// miss. Increment <see cref="FormatVersion"/> whenever this schema changes.
/// </para>
/// </remarks>
internal static class S57CatalogCacheSerializer
{
    /// <summary>
    /// Schema version stamped into every frame. Bump on any change to the
    /// serialized layout so previously persisted sidecars are treated as a
    /// miss instead of being misread.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>Serializes <paramref name="cells"/> into the binary frame.</summary>
    /// <param name="cells">The base cells to persist.</param>
    /// <returns>The serialized bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> is null.</exception>
    public static byte[] Serialize(IReadOnlyList<S57ExchangeSetCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FormatVersion);
            w.Write(cells.Count);

            foreach (var cell in cells)
            {
                w.Write(cell.CellName);
                w.Write(cell.RelativePath);

                w.Write(cell.UpdateRelativePaths.Count);
                foreach (var update in cell.UpdateRelativePaths)
                    w.Write(update);

                var box = cell.BoundingBox;
                w.Write(box is not null);
                if (box is not null)
                {
                    w.Write(box.WestBoundLongitude);
                    w.Write(box.EastBoundLongitude);
                    w.Write(box.SouthBoundLatitude);
                    w.Write(box.NorthBoundLatitude);
                }
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a cell list previously produced by <see cref="Serialize"/>.
    /// Returns <see langword="null"/> when the bytes are truncated, corrupt,
    /// or carry a mismatched <see cref="FormatVersion"/> — the caller treats
    /// that as a cache miss.
    /// </summary>
    /// <param name="bytes">The serialized bytes.</param>
    /// <returns>The deserialized cells, or <see langword="null"/> on any failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
    public static IReadOnlyList<S57ExchangeSetCell>? TryDeserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            if (r.ReadInt32() != FormatVersion)
                return null;

            var count = r.ReadInt32();
            if (count < 0)
                return null;

            var cells = new List<S57ExchangeSetCell>(count);
            for (var i = 0; i < count; i++)
            {
                var cellName = ReadBoundedString(r);
                var relativePath = ReadBoundedString(r);

                var updateCount = r.ReadInt32();
                if (updateCount < 0)
                    return null;

                IReadOnlyList<string> updates;
                if (updateCount == 0)
                {
                    updates = Array.Empty<string>();
                }
                else
                {
                    var updateList = new List<string>(updateCount);
                    for (var u = 0; u < updateCount; u++)
                        updateList.Add(ReadBoundedString(r));
                    updates = updateList;
                }

                BoundingBox? box = null;
                if (r.ReadBoolean())
                {
                    box = new BoundingBox
                    {
                        WestBoundLongitude = r.ReadDouble(),
                        EastBoundLongitude = r.ReadDouble(),
                        SouthBoundLatitude = r.ReadDouble(),
                        NorthBoundLatitude = r.ReadDouble(),
                    };
                }

                cells.Add(new S57ExchangeSetCell
                {
                    CellName = cellName,
                    RelativePath = relativePath,
                    UpdateRelativePaths = updates,
                    BoundingBox = box,
                });
            }

            return cells;
        }
        catch
        {
            // Truncated / corrupt frame: treat as a miss.
            return null;
        }
    }

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
