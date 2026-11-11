using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Static helpers that decide whether a dropped folder or file is an
/// S-100 exchange set. The viewer's drag-and-drop handler uses these
/// to route folders and ZIP archives holding a <c>CATALOG.XML</c> (at the
/// top level or under <c>S100_ROOT</c>) to
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
    // Catalogue file names (CATALOG.XML / CATALOGUE.XML, CATALOG.031) are
    // shared with collection indexing through ExchangeSetLayout, so the
    // viewer and the library agree on what an exchange set is (issue #655).

    /// <summary>The base-cell extension shared by S-57 (S-57 Ed 3.1
    /// Appendix B.1) and S-101 (S-100 Part 10a) ENC datasets. Sequential
    /// updates use <c>.001</c>, <c>.002</c>, …</summary>
    private const string BaseCellExtension = ".000";

    /// <summary>True when <paramref name="path"/> ends with
    /// <c>.zip</c> (case-insensitive).</summary>
    public static bool IsZipPath(string path) =>
        !string.IsNullOrEmpty(path) &&
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="folderPath"/> exists and holds a
    /// recognised catalogue file (<c>CATALOG.XML</c> or
    /// <c>catalogue.xml</c>, case-insensitive) at its top level or in its
    /// <c>S100_ROOT</c> folder (S-100 Part 17). Returns <c>false</c> for
    /// any I/O or permission failure.</summary>
    public static bool LooksLikeExchangeSetFolder(string folderPath) =>
        ResolveFolderRoot(folderPath) is not null;

    /// <summary>
    /// Finds the exchange set in <paramref name="folderPath"/>: the folder
    /// that holds the catalogue (the folder itself or its <c>S100_ROOT</c>)
    /// and the catalogue's on-disk name, e.g. <c>CATALOG.XML</c> or the
    /// S-411 <c>catalogue.xml</c>. Returns <c>null</c> when the folder
    /// doesn't exist, can't be read, or holds no recognised catalogue.
    /// </summary>
    public static ExchangeSetRootLocation? ResolveFolderRoot(string folderPath) =>
        string.IsNullOrEmpty(folderPath) ? null : ExchangeSetRoot.FindInFolder(folderPath);

    /// <summary>
    /// The catalogue's on-disk name within the folder
    /// <see cref="ResolveFolderRoot"/> finds, or <c>null</c> when there's none.
    /// </summary>
    public static string? ResolveFolderCatalogueName(string folderPath) =>
        ResolveFolderRoot(folderPath)?.CatalogueName;

    /// <summary>True when <paramref name="zipPath"/> is a readable
    /// ZIP archive with a recognised catalogue entry (<c>CATALOG.XML</c>
    /// or <c>catalogue.xml</c>, case-insensitive) at its root or under
    /// <c>S100_ROOT/</c>. Returns <c>false</c> for corrupt archives or
    /// I/O failures.</summary>
    public static bool LooksLikeExchangeSetZip(string zipPath) =>
        ResolveZipRoot(zipPath) is not null;

    /// <summary>
    /// Finds the exchange set in the ZIP at <paramref name="zipPath"/>: the
    /// entry prefix it sits under (empty, or <c>S100_ROOT/</c>) and the
    /// catalogue's stored name. Returns <c>null</c> when the archive is
    /// missing, corrupt, or has no recognised catalogue.
    /// </summary>
    public static ExchangeSetRootLocation? ResolveZipRoot(string zipPath) =>
        string.IsNullOrEmpty(zipPath) ? null : ExchangeSetRoot.FindInZip(zipPath);

    /// <summary>
    /// The catalogue's stored name under the prefix
    /// <see cref="ResolveZipRoot"/> finds, or <c>null</c> when there's none.
    /// </summary>
    public static string? ResolveZipCatalogueEntry(string zipPath) =>
        ResolveZipRoot(zipPath)?.CatalogueName;

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
                    Path.GetFileName(f), EncDotNet.S100.Collections.ExchangeSetLayout.S57CatalogueName,
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
                   Path.GetFileName(path), EncDotNet.S100.Collections.ExchangeSetLayout.S57CatalogueName,
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
                    $"No {EncDotNet.S100.Collections.ExchangeSetLayout.S57CatalogueName} found in folder: {path}");
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
