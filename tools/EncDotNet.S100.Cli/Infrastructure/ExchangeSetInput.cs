using System.IO.Compression;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Classifies a CLI path argument as an S-100 exchange set — a directory
/// containing a top-level <c>CATALOG.XML</c>, a <c>CATALOG.XML</c> file, or a
/// <c>.zip</c> archive whose root holds a <c>CATALOG.XML</c> — and opens the
/// appropriate <see cref="IAssetSource"/> for verification.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 17. Mirrors the viewer's exchange-set detection so
/// the <c>s100 validate</c> command can integrity-check whole exchange sets in
/// addition to single datasets.
/// </remarks>
internal static class ExchangeSetInput
{
    /// <summary>The catalogue filename, matched case-insensitively.</summary>
    private const string CatalogueFileName = "CATALOG.XML";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> looks like an
    /// exchange set (without opening it), so a caller can route it away from the
    /// single-dataset path.
    /// </summary>
    public static bool LooksLikeExchangeSet(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (Directory.Exists(path))
            return FindCatalogueInDirectory(path) is not null;

        if (IsCatalogueFileName(Path.GetFileName(path)) && File.Exists(path))
            return true;

        return IsZipPath(path) && File.Exists(path) && ZipContainsRootCatalogue(path);
    }

    /// <summary>
    /// Opens the exchange set at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">A directory, <c>CATALOG.XML</c> file, or <c>.zip</c> archive.</param>
    /// <returns>
    /// The opened <see cref="IAssetSource"/>, the source-relative catalogue path,
    /// and a short human-readable kind (<c>folder</c>, <c>catalogue</c>, or
    /// <c>zip</c>). The caller owns disposing the returned source.
    /// </returns>
    /// <exception cref="FileNotFoundException">No catalogue was found.</exception>
    public static (IAssetSource Source, string CataloguePath, string Kind) Open(string path)
    {
        if (Directory.Exists(path))
        {
            var catalogue = FindCatalogueInDirectory(path)
                ?? throw new FileNotFoundException(
                    $"No {CatalogueFileName} found in folder: {path}");
            return (FileSystemAssetSource.Create(path), Path.GetFileName(catalogue), "folder");
        }

        if (IsCatalogueFileName(Path.GetFileName(path)) && File.Exists(path))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            return (FileSystemAssetSource.Create(directory), Path.GetFileName(path), "catalogue");
        }

        if (IsZipPath(path) && File.Exists(path))
        {
            var entryName = FindRootCatalogueEntry(path)
                ?? throw new FileNotFoundException(
                    $"No root {CatalogueFileName} found in archive: {path}");
            return (ZipAssetSource.Create(path), entryName, "zip");
        }

        throw new FileNotFoundException($"Not an S-100 exchange set: {path}");
    }

    private static bool IsCatalogueFileName(string? name) =>
        string.Equals(name, CatalogueFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsZipPath(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    private static string? FindCatalogueInDirectory(string folderPath)
    {
        try
        {
            return Directory
                .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => IsCatalogueFileName(Path.GetFileName(f)));
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static bool ZipContainsRootCatalogue(string zipPath) =>
        FindRootCatalogueEntry(zipPath) is not null;

    private static string? FindRootCatalogueEntry(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries.FirstOrDefault(IsRootCatalogueEntry)?.FullName;
        }
        catch (InvalidDataException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static bool IsRootCatalogueEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        if (name.Contains('/') || name.Contains('\\'))
            return false;
        return IsCatalogueFileName(name);
    }

    /// <summary>The S-57 / S-63 exchange-set catalogue filename, matched case-insensitively.</summary>
    private const string S57CatalogueFileName = "CATALOG.031";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> looks like an
    /// <em>S-57</em> exchange set — a directory containing a top-level
    /// <c>CATALOG.031</c>, or a <c>CATALOG.031</c> file itself. The S-57 verifier
    /// is directory-rooted, so (unlike the S-100 path) ZIP archives are not
    /// accepted here.
    /// </summary>
    public static bool LooksLikeS57ExchangeSet(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (Directory.Exists(path))
            return FindS57CatalogueInDirectory(path) is not null;

        return IsS57CatalogueFileName(Path.GetFileName(path)) && File.Exists(path);
    }

    /// <summary>
    /// Resolves the root directory of the S-57 exchange set at
    /// <paramref name="path"/> (the folder that contains <c>CATALOG.031</c>),
    /// which is what <see cref="EncDotNet.S100.Datasets.S57.S57ExchangeSetVerification"/>
    /// expects.
    /// </summary>
    /// <exception cref="FileNotFoundException">No <c>CATALOG.031</c> was found.</exception>
    public static string ResolveS57Root(string path)
    {
        if (Directory.Exists(path))
        {
            if (FindS57CatalogueInDirectory(path) is null)
                throw new FileNotFoundException(
                    $"No {S57CatalogueFileName} found in folder: {path}");
            return Path.GetFullPath(path);
        }

        if (IsS57CatalogueFileName(Path.GetFileName(path)) && File.Exists(path))
            return Path.GetDirectoryName(Path.GetFullPath(path))!;

        throw new FileNotFoundException($"Not an S-57 exchange set: {path}");
    }

    private static bool IsS57CatalogueFileName(string? name) =>
        string.Equals(name, S57CatalogueFileName, StringComparison.OrdinalIgnoreCase);

    private static string? FindS57CatalogueInDirectory(string folderPath)
    {
        try
        {
            return Directory
                .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => IsS57CatalogueFileName(Path.GetFileName(f)));
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }
}
