using System.IO.Compression;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Protection;

internal static class ProtectedContentReader
{
    private static readonly byte[] ZipLocalFileHeader = [0x50, 0x4B, 0x03, 0x04];

    public static async Task<byte[]> ReadAllBytesAsync(
        IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = await source.OpenAsync(relativePath, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(
            capacity: stream.CanSeek && stream.Length <= int.MaxValue
                ? (int)stream.Length
                : 4096);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public static byte[] Decrypt(byte[] ciphertext, IDatasetKeyProvider keyProvider, string relativePath)
    {
        var fileName = GetFileName(relativePath);
        if (!keyProvider.TryGetCellKey(fileName, out var cellKey) || cellKey is null)
        {
            throw new InvalidOperationException(
                $"No Part 15 cell key is available for protected resource '{relativePath}'.");
        }

        try
        {
            return S100Cipher.DecryptDataset(ciphertext, cellKey);
        }
        finally
        {
            Array.Clear(cellKey);
        }
    }

    public static bool IsZipArchive(byte[] content) =>
        content.Length >= ZipLocalFileHeader.Length &&
        content.AsSpan(0, ZipLocalFileHeader.Length).SequenceEqual(ZipLocalFileHeader);

    public static byte[] ExtractSingleEntry(byte[] zipBytes)
    {
        using var zipStream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = archive.Entries.Count == 1
            ? archive.Entries[0]
            : throw new InvalidDataException(
                $"A compressed Part 15 resource must contain exactly one entry but contained {archive.Entries.Count}.");

        using var entryStream = entry.Open();
        using var output = new MemoryStream(
            capacity: entry.Length > 0 && entry.Length <= int.MaxValue ? (int)entry.Length : 4096);
        entryStream.CopyTo(output);
        return output.ToArray();
    }

    private static string GetFileName(string relativePath)
    {
        var slash = relativePath.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
    }
}
