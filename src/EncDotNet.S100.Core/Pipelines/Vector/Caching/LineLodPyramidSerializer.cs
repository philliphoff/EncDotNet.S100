using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// Binary serializer for <see cref="LineLodPyramid"/> used by the disk
/// implementation of <see cref="ILineLodCache"/>. Small, self-contained,
/// stable across cache generations: on read the format version is checked
/// and any mismatch (including truncation) is reported as
/// <see langword="null"/> so the caller invalidates and rebuilds.
/// </summary>
/// <remarks>
/// Format is a length-prefixed sequence of levels, each a length-prefixed
/// sequence of (lat, lon) pairs plus the tolerance and the passthrough
/// flag. The whole file is preceded by a 4-byte magic + 2-byte version so
/// stale or corrupted files can be detected cheaply.
/// </remarks>
internal static class LineLodPyramidSerializer
{
    /// <summary>Magic prefix identifying a line-LOD file.</summary>
    private static readonly byte[] Magic = [(byte)'L', (byte)'L', (byte)'O', (byte)'D'];

    /// <summary>
    /// Persistence format version. Bump whenever the byte layout changes so
    /// existing on-disk entries are treated as misses.
    /// </summary>
    internal const ushort FormatVersion = 1;

    /// <summary>
    /// Serialises the pyramid to a byte array.
    /// </summary>
    public static byte[] Serialize(LineLodPyramid pyramid)
    {
        ArgumentNullException.ThrowIfNull(pyramid);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(pyramid.InputVertexCount);
        writer.Write(pyramid.Levels.Count);

        foreach (var level in pyramid.Levels)
        {
            writer.Write(level.ToleranceMetres);
            writer.Write(level.IsPassthrough);
            writer.Write(level.Coordinates.Count);
            foreach (var coord in level.Coordinates)
            {
                writer.Write(coord.Latitude);
                writer.Write(coord.Longitude);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Attempts to deserialise a pyramid from <paramref name="bytes"/>.
    /// Returns <see langword="null"/> when the buffer is truncated, has a
    /// mismatched magic or version, or otherwise fails to decode — the
    /// caller treats every such case as a cache miss.
    /// </summary>
    public static LineLodPyramid? TryDeserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream);

            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length)
            {
                return null;
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (magic[i] != Magic[i])
                {
                    return null;
                }
            }

            var version = reader.ReadUInt16();
            if (version != FormatVersion)
            {
                return null;
            }

            var inputVertexCount = reader.ReadInt32();
            var levelCount = reader.ReadInt32();
            if (levelCount <= 0 || inputVertexCount < 0)
            {
                return null;
            }

            var levels = new List<LineLodLevel>(levelCount);
            for (var l = 0; l < levelCount; l++)
            {
                var tolerance = reader.ReadDouble();
                var isPassthrough = reader.ReadBoolean();
                var coordCount = reader.ReadInt32();
                if (coordCount < 0)
                {
                    return null;
                }

                var coords = new GeoPosition[coordCount];
                for (var c = 0; c < coordCount; c++)
                {
                    var lat = reader.ReadDouble();
                    var lon = reader.ReadDouble();
                    coords[c] = new GeoPosition(lat, lon);
                }

                levels.Add(new LineLodLevel(tolerance, coords, isPassthrough));
            }

            return new LineLodPyramid(levels, inputVertexCount);
        }
        catch
        {
            return null;
        }
    }
}
