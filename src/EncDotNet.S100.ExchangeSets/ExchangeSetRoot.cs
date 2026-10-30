using System.IO.Compression;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Where an S-100 exchange set's catalogue sits in a folder or ZIP archive.
/// </summary>
/// <param name="BasePath">
/// For a folder, the folder that holds the catalogue. For a ZIP archive, the
/// entry prefix the exchange set sits under (empty, or for example
/// <c>S100_ROOT/</c>), ready for <c>ZipAssetSource.Create(path, basePath)</c>.
/// </param>
/// <param name="CatalogueName">The catalogue's file name, as stored.</param>
public readonly record struct ExchangeSetRootLocation(string BasePath, string CatalogueName);

/// <summary>
/// Finds an S-100 exchange set's catalogue in a folder or ZIP archive: at the
/// top level, or else in an <c>S100_ROOT</c> folder, the layout S-100 Part 17
/// gives an exchange set on media (the IHO S-164 test data is packaged this
/// way).
/// </summary>
/// <remarks>
/// The facade, collection indexing and SoundCharts all use this rule, so they
/// agree on what an S-100 exchange set is.
/// </remarks>
public static class ExchangeSetRoot
{
    /// <summary>The folder S-100 Part 17 puts an exchange set under.</summary>
    public const string FolderName = "S100_ROOT";

    /// <summary>
    /// Accepted catalogue file names, preferred first. S-100 Part 17 names the
    /// file <c>CATALOG.XML</c>; some producers write <c>CATALOGUE.XML</c>.
    /// </summary>
    public static IReadOnlyList<string> CatalogueNames { get; } = ["CATALOG.XML", "CATALOGUE.XML"];

    /// <summary>True when <paramref name="fileName"/> is a catalogue file name (case-insensitive).</summary>
    /// <param name="fileName">A file name without a folder.</param>
    public static bool IsCatalogueName(string? fileName) =>
        fileName is not null
        && CatalogueNames.Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Picks the catalogue among <paramref name="fileNames"/>, preferring
    /// <c>CATALOG.XML</c>, or returns <see langword="null"/>.
    /// </summary>
    /// <param name="fileNames">File names without folders.</param>
    public static string? PickCatalogue(IEnumerable<string?> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        string? fallback = null;
        foreach (var name in fileNames)
        {
            if (string.Equals(name, CatalogueNames[0], StringComparison.OrdinalIgnoreCase))
                return name;
            if (fallback is null && IsCatalogueName(name))
                fallback = name;
        }

        return fallback;
    }

    /// <summary>
    /// Finds the catalogue in <paramref name="folderPath"/> or its
    /// <c>S100_ROOT</c> folder, or returns <see langword="null"/> when there's
    /// none or the folder can't be read.
    /// </summary>
    /// <param name="folderPath">The folder to look in.</param>
    public static ExchangeSetRootLocation? FindInFolder(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        try
        {
            if (!Directory.Exists(folderPath))
                return null;

            if (PickCatalogue(FileNames(folderPath)) is { } name)
                return new(folderPath, name);

            var root = Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => string.Equals(Path.GetFileName(d), FolderName, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
            return root is not null && PickCatalogue(FileNames(root)) is { } nested
                ? new(root, nested)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the catalogue among a ZIP archive's entry names: at the top
    /// level, or else under <c>S100_ROOT/</c>. Returns
    /// <see langword="null"/> when there's none.
    /// </summary>
    /// <param name="entryNames">The archive's entry names (<c>ZipArchiveEntry.FullName</c>).</param>
    public static ExchangeSetRootLocation? FindInZip(IEnumerable<string> entryNames)
    {
        ArgumentNullException.ThrowIfNull(entryNames);

        List<string> top = [];
        Dictionary<string, List<string>> nested = new(StringComparer.Ordinal);
        foreach (var fullName in entryNames.Order(StringComparer.Ordinal))
        {
            var parts = fullName.Replace('\\', '/').Split('/');
            if (parts.Length == 1)
            {
                top.Add(fullName);
            }
            else if (parts.Length == 2 && string.Equals(parts[0], FolderName, StringComparison.OrdinalIgnoreCase))
            {
                // Keep the folder's stored spelling and separator for the prefix.
                var prefix = fullName[..(parts[0].Length + 1)];
                if (!nested.TryGetValue(prefix, out var names))
                    nested[prefix] = names = [];
                names.Add(parts[1]);
            }
        }

        if (PickCatalogue(top) is { } name)
            return new(string.Empty, name);

        foreach (var (prefix, names) in nested.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            if (PickCatalogue(names) is { } nestedName)
                return new(prefix, nestedName);
        }

        return null;
    }

    /// <summary>
    /// Opens the ZIP archive at <paramref name="zipPath"/> and finds its
    /// catalogue (see <see cref="FindInZip(IEnumerable{string})"/>). Returns
    /// <see langword="null"/> when the file is missing, isn't a readable ZIP,
    /// or has no catalogue.
    /// </summary>
    /// <param name="zipPath">The ZIP archive.</param>
    public static ExchangeSetRootLocation? FindInZip(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        try
        {
            if (!File.Exists(zipPath))
                return null;
            using var archive = ZipFile.OpenRead(zipPath);
            return FindInZip(archive.Entries.Select(e => e.FullName));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> FileNames(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal);
}
