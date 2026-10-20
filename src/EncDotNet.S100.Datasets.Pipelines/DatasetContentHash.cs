using System.Security.Cryptography;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Hashes a dataset's raw bytes at load, so a processor can report a
/// <see cref="Portrayal.VectorPortrayalResult.ContentScope"/> that tells
/// datasets apart by content rather than by name or product.
/// </summary>
internal static class DatasetContentHash
{
    /// <summary>
    /// Returns the lowercase hex SHA-256 of <paramref name="stream"/>'s
    /// remaining bytes and rewinds it, so the reader then parses the same
    /// bytes. A forward-only stream is first buffered into memory: the
    /// original is disposed and <paramref name="stream"/> is replaced.
    /// </summary>
    public static string HashAndRewind(ref Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            var buffer = new MemoryStream();
            using (stream)
                stream.CopyTo(buffer);
            buffer.Position = 0;
            stream = buffer;
        }

        var start = stream.Position;
        var hash = SHA256.HashData(stream);
        stream.Position = start;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Hashes <paramref name="streams"/> in order (a base cell then its
    /// updates), rewinding each.
    /// </summary>
    public static string HashAndRewind(IReadOnlyList<Stream> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var stream in streams)
        {
            if (!stream.CanSeek)
                throw new ArgumentException("Every stream must be seekable.", nameof(streams));

            // Length-prefix each stream so moving bytes across a boundary
            // changes the hash.
            var start = stream.Position;
            hash.AppendData(BitConverter.GetBytes(stream.Length - start));
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
            stream.Position = start;
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
