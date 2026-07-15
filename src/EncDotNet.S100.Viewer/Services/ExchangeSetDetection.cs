using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Static helpers that decide whether a dropped folder or file is an
/// S-100 exchange set. The viewer's drag-and-drop handler uses these
/// to route folders containing a <c>CATALOG.XML</c> and ZIP archives
/// containing a root-level <c>CATALOG.XML</c> to
/// <see cref="IExchangeSetService.OpenAsync"/> instead of the
/// single-dataset loader.
/// </summary>
/// <remarks>
/// Both helpers swallow filesystem and IO failures (returning
/// <c>false</c>) so a noisy drop falls through to the single-file
/// loader, which surfaces a more specific error message to the user.
/// </remarks>
internal static class ExchangeSetDetection
{
    /// <summary>The canonical S-100 catalogue filename (S-100 Part 17),
    /// matched case-insensitively.</summary>
    private const string CatalogueFileName = "CATALOG.XML";

    /// <summary>
    /// Accepted exchange-set catalogue filenames, matched
    /// case-insensitively. In addition to the canonical
    /// <c>CATALOG.XML</c> (S-100 Part 17), several products in the wild
    /// — notably JCOMM/IHO S-411 sample sets — name the catalogue
    /// <c>catalogue.xml</c>. Both are recognised so their exchange sets
    /// route to the exchange-set loader instead of silently falling
    /// through to the single-file loader.
    /// </summary>
    private static readonly string[] CatalogueFileNames =
    {
        CatalogueFileName,
        "CATALOGUE.XML",
    };

    /// <summary>The S-57 / S-63 exchange-set catalogue filename, matched
    /// case-insensitively.</summary>
    private const string S57CatalogueFileName = "CATALOG.031";

    /// <summary>The base-cell extension shared by S-57 (S-57 Ed 3.1
    /// Appendix B.1) and S-101 (S-100 Part 10a) ENC datasets. Sequential
    /// updates use <c>.001</c>, <c>.002</c>, …</summary>
    private const string BaseCellExtension = ".000";

    private static bool IsCatalogueFileName(string fileName) =>
        Array.Exists(
            CatalogueFileNames,
            n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));

    private static string? PickCatalogueName(IEnumerable<string?> names) =>
        names.OfType<string>()
            .Where(IsCatalogueFileName)
            .OrderBy(
                name => string.Equals(
                    name, CatalogueFileName, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>True when <paramref name="path"/> ends with
    /// <c>.zip</c> (case-insensitive).</summary>
    public static bool IsZipPath(string path) =>
        !string.IsNullOrEmpty(path) &&
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="folderPath"/> exists and
    /// contains a recognised catalogue file (<c>CATALOG.XML</c> or
    /// <c>catalogue.xml</c>) at its top level (case-insensitive).
    /// Returns <c>false</c> for any I/O or permission failure.</summary>
    public static bool LooksLikeExchangeSetFolder(string folderPath) =>
        ResolveFolderCatalogueName(folderPath) is not null;

    /// <summary>
    /// Returns the actual top-level catalogue file name found in
    /// <paramref name="folderPath"/> (preserving its on-disk casing and
    /// spelling, e.g. <c>CATALOG.XML</c> or <c>catalogue.xml</c>), or
    /// <c>null</c> when the folder does not exist, is inaccessible, or
    /// contains no recognised catalogue. Callers pass the returned name
    /// to <c>ExchangeSet.OpenAsync</c> so producers using the S-411
    /// <c>catalogue.xml</c> convention resolve correctly.
    /// </summary>
    public static string? ResolveFolderCatalogueName(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return null;
        try
        {
            if (!Directory.Exists(folderPath)) return null;
            return PickCatalogueName(
                Directory.EnumerateFiles(
                        folderPath, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName));
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>True when <paramref name="zipPath"/> is a readable
    /// ZIP archive whose root contains a recognised catalogue entry
    /// (<c>CATALOG.XML</c> or <c>catalogue.xml</c>, case-insensitive).
    /// Returns <c>false</c> for corrupt archives or I/O failures.</summary>
    public static bool LooksLikeExchangeSetZip(string zipPath) =>
        ResolveZipCatalogueEntry(zipPath) is not null;

    /// <summary>
    /// Returns the actual root-level catalogue entry name found in the
    /// ZIP at <paramref name="zipPath"/> (preserving its stored casing
    /// and spelling), or <c>null</c> when the archive is missing,
    /// corrupt, or contains no recognised root catalogue.
    /// </summary>
    public static string? ResolveZipCatalogueEntry(string zipPath)
    {
        if (string.IsNullOrEmpty(zipPath)) return null;
        try
        {
            if (!File.Exists(zipPath)) return null;
            using var archive = ZipFile.OpenRead(zipPath);
            return PickCatalogueName(
                archive.Entries
                    .Where(IsRootEntry)
                    .Select(entry => entry.FullName));
        }
        catch (InvalidDataException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static bool IsRootEntry(ZipArchiveEntry entry)
    {
        // ZIP entry names use forward slashes per the spec; tolerate
        // backslashes too in case a producer wrote them. A "root"
        // entry has no separator at all.
        var name = entry.FullName;
        return !name.Contains('/') && !name.Contains('\\');
    }

    /// <summary>True when <paramref name="path"/> is an S-57 / S-63 exchange set
    /// — a directory containing a top-level <c>CATALOG.031</c>, or a
    /// <c>CATALOG.031</c> file itself. The S-57 verifier is directory-rooted, so
    /// (unlike the S-100 path) ZIP archives are not recognised here.</summary>
    public static bool LooksLikeS57ExchangeSet(string path) =>
        LooksLikeS57ExchangeSetFolder(path) || IsS57CataloguePath(path);

    /// <summary>True when <paramref name="folderPath"/> exists and contains a
    /// <c>CATALOG.031</c> at its top level (case-insensitive). Returns
    /// <c>false</c> for any I/O or permission failure.</summary>
    public static bool LooksLikeS57ExchangeSetFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return false;
        try
        {
            if (!Directory.Exists(folderPath)) return false;
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Any(f => string.Equals(
                    Path.GetFileName(f), S57CatalogueFileName,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>True when <paramref name="path"/> is an existing file named
    /// <c>CATALOG.031</c> (case-insensitive) — i.e. the user dropped the S-57
    /// catalogue file directly.</summary>
    public static bool IsS57CataloguePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return string.Equals(
                   Path.GetFileName(path), S57CatalogueFileName,
                   StringComparison.OrdinalIgnoreCase)
               && File.Exists(path);
    }

    /// <summary>
    /// Resolves the root directory of the S-57 exchange set at
    /// <paramref name="path"/> (the folder that contains <c>CATALOG.031</c>),
    /// which is what the S-57 reader / verifier expect.
    /// </summary>
    /// <exception cref="FileNotFoundException">No <c>CATALOG.031</c> was found.</exception>
    public static string ResolveS57Root(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (Directory.Exists(path))
        {
            if (!LooksLikeS57ExchangeSetFolder(path))
                throw new FileNotFoundException(
                    $"No {S57CatalogueFileName} found in folder: {path}");
            return Path.GetFullPath(path);
        }

        if (IsS57CataloguePath(path))
            return Path.GetDirectoryName(Path.GetFullPath(path))!;

        throw new FileNotFoundException($"Not an S-57 exchange set: {path}");
    }

    /// <summary>
    /// Enumerates the top-level base cells (<c>….000</c> files) in
    /// <paramref name="folderPath"/>, returning their full paths in
    /// case-insensitive ordinal name order. Recognises both S-57
    /// (S-57 Ed 3.1 Appendix B.1) and S-101 (S-100 Part 10a) cells,
    /// which share the <c>.000</c> extension — the two are told apart
    /// later by content (the S-57 DSPM sniff in
    /// <c>DatasetPipelineFactory.DetectProductSpec</c>). Returns an
    /// empty list when the folder does not exist, is inaccessible, or
    /// holds no base cells.
    /// </summary>
    public static IReadOnlyList<string> EnumerateLooseBaseCells(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return Array.Empty<string>();
        try
        {
            if (!Directory.Exists(folderPath)) return Array.Empty<string>();
            return Directory
                .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(
                    Path.GetExtension(f), BaseCellExtension,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// True when <paramref name="folderPath"/> is a catalogue-less folder
    /// of loose ENC cells — it contains at least one top-level base cell
    /// (<c>….000</c>) but <em>no</em> exchange-set catalogue (neither
    /// <c>CATALOG.XML</c>/<c>catalogue.xml</c> nor <c>CATALOG.031</c>).
    /// Such folders (e.g. a single cell's subfolder extracted from an
    /// exchange set) are loaded by scanning for base cells and applying
    /// their sibling sequential updates, rather than by parsing a
    /// catalogue. Returns <c>false</c> for any folder that already routes
    /// to an exchange-set loader, and for I/O or permission failures.
    /// </summary>
    public static bool LooksLikeLooseCellFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return false;

        // A folder with a catalogue is handled by the exchange-set
        // loaders; only genuinely catalogue-less folders take this path.
        if (LooksLikeExchangeSetFolder(folderPath)) return false;
        if (LooksLikeS57ExchangeSetFolder(folderPath)) return false;

        return EnumerateLooseBaseCells(folderPath).Count > 0;
    }
}
