using System.IO.Compression;

namespace EncDotNet.S100.Collections.Feeds;

/// <summary>
/// Writes a published item's download (issue #680): a zip holding its
/// exchange-set catalogue, base file and updates at their paths relative to
/// the item's root, whether that root is a folder or a ZIP archive.
/// </summary>
public static class S100FeedPackager
{
    /// <summary>The files of <paramref name="location"/>, relative to its root: catalogue first, then base, then updates.</summary>
    public static IReadOnlyList<string> Files(LocalItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return new[] { location.CatalogueRelativePath, location.RelativePath }
            .Concat(location.UpdateRelativePaths)
            .OfType<string>()
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Writes the zip for <paramref name="location"/> to <paramref name="output"/> (which need not seek).</summary>
    /// <exception cref="FileNotFoundException">The base file is gone.</exception>
    public static async Task WriteZipAsync(Stream output, LocalItemLocation location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(location);

        using var source = location.IsZip ? ZipFile.OpenRead(location.RootPath) : null;
        // ZipArchive writes its headers synchronously; hosts that forbid
        // synchronous I/O on the output should buffer it.
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var relative in Files(location))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var content = Open(location, source, relative);
            if (content is null)
            {
                if (relative == Normalize(location.RelativePath))
                    throw new FileNotFoundException("The dataset's base file is gone.", relative);
                continue;
            }

            var entry = zip.CreateEntry(relative, CompressionLevel.Fastest);
            await using var target = entry.Open();
            await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>True when the item's base file is present, so it can be published.</summary>
    public static bool Exists(LocalItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            if (!location.IsZip)
                return File.Exists(FullPath(location.RootPath, location.RelativePath));

            using var zip = ZipFile.OpenRead(location.RootPath);
            return Entry(zip, Normalize(location.RelativePath)) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>The total (uncompressed) size of the item's files, or <see langword="null"/> when unknown.</summary>
    public static long? EstimateSize(LocalItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            if (location.IsZip)
            {
                using var zip = ZipFile.OpenRead(location.RootPath);
                return Files(location).Sum(f => Entry(zip, f)?.Length ?? 0);
            }

            return Files(location).Sum(f => new FileInfo(FullPath(location.RootPath, f)) is { Exists: true } info ? info.Length : 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>When the item's files last changed (the latest of them), or <see langword="null"/> when unknown.</summary>
    public static DateTimeOffset? LastModified(LocalItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            var times = location.IsZip
                ? [new DateTimeOffset(File.GetLastWriteTimeUtc(location.RootPath), TimeSpan.Zero)]
                : Files(location)
                    .Select(f => new FileInfo(FullPath(location.RootPath, f)))
                    .Where(f => f.Exists)
                    .Select(f => new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero))
                    .ToArray();
            return times.Length == 0 ? null : times.Max();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Stream? Open(LocalItemLocation location, ZipArchive? source, string relative)
    {
        if (source is not null)
            return Entry(source, relative)?.Open();

        var path = FullPath(location.RootPath, relative);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    private static ZipArchiveEntry? Entry(ZipArchive zip, string relative) =>
        zip.GetEntry(relative) ?? zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), relative, StringComparison.OrdinalIgnoreCase));

    private static string FullPath(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Forward slashes, no leading slash, no <c>..</c> segments (a zip entry name must stay inside the zip).</summary>
    private static string Normalize(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p == ".."))
            throw new InvalidDataException($"'{relative}' leaves the item's root.");
        return string.Join('/', parts.Where(p => p != "."));
    }
}
