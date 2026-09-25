using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Walks a local path once and plans what to index: exchange-set folders,
/// ZIP archives that may hold exchange sets, and loose dataset files. Also
/// produces the fingerprint used to decide whether a cached index is stale.
/// </summary>
/// <remarks>
/// <para>
/// A folder that contains an S-100 catalogue (<c>CATALOG.XML</c> or
/// <c>CATALOGUE.XML</c>) or an S-57 catalogue (<c>CATALOG.031</c>) is an
/// exchange-set root: it is indexed from its catalogue, and the walk does not
/// descend into it looking for loose files. In any other folder, <c>.zip</c>
/// files are planned as candidate exchange sets and files with a dataset
/// extension (<c>.000</c>, <c>.h5</c>, <c>.gml</c>) as loose datasets, with
/// S-57 sequential updates (<c>.001</c>, …) attached to their base cell.
/// </para>
/// <para>
/// The fingerprint covers the relative path, length and last-write time of
/// every file the walk considers, including every file inside exchange-set
/// roots, so replacing a cell or catalogue invalidates the index.
/// </para>
/// </remarks>
internal static class LocalSourceScanner
{
    /// <summary>
    /// Bumped whenever indexing output changes for the same input, so indexes
    /// cached by an older build are rebuilt.
    /// </summary>
    internal const string FingerprintVersion = "local-v1";

    internal static readonly string[] S100CatalogueNames = ["CATALOG.XML", "CATALOGUE.XML"];

    internal const string S57CatalogueName = "CATALOG.031";

    private static readonly string[] LooseExtensions = [".000", ".h5", ".gml"];

    private static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Plans the indexing of <paramref name="path"/> (a folder, a catalogue
    /// file, a ZIP, or a single dataset file). Returns <see langword="null"/>
    /// when the path does not exist.
    /// </summary>
    public static ScanResult? Scan(string path, bool recursive, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var fullPath = Path.GetFullPath(path);
        var units = new List<ScanUnit>();
        var stamps = new List<string>();

        if (Directory.Exists(fullPath))
        {
            ScanDirectory(fullPath, fullPath, recursive, units, stamps, cancellationToken);
            return new ScanResult(fullPath, units, Fingerprint(stamps));
        }

        if (!File.Exists(fullPath))
            return null;

        var root = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);

        if (IsS100CatalogueName(fileName))
        {
            units.Add(new S100FolderUnit(root, fileName));
            StampTree(root, root, stamps);
        }
        else if (string.Equals(fileName, S57CatalogueName, StringComparison.OrdinalIgnoreCase))
        {
            units.Add(new S57FolderUnit(root));
            StampTree(root, root, stamps);
        }
        else if (IsZip(fullPath))
        {
            units.Add(new ZipUnit(fullPath));
            Stamp(root, new FileInfo(fullPath), stamps);
        }
        else if (IsLooseCandidate(fileName))
        {
            var siblings = Directory.EnumerateFiles(root, "*", TopLevel).ToArray();
            var unit = new LooseFileUnit(fullPath, FindUpdates(fullPath, siblings));
            units.Add(unit);
            Stamp(root, new FileInfo(fullPath), stamps);
            foreach (var update in unit.UpdatePaths)
                Stamp(root, new FileInfo(update), stamps);
        }

        return new ScanResult(root, units, Fingerprint(stamps));
    }

    private static void ScanDirectory(
        string sourceRoot,
        string start,
        bool recursive,
        List<ScanUnit> units,
        List<string> stamps,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = pending.Pop();
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", TopLevel)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var s100Catalogue = PickS100Catalogue(files.Select(Path.GetFileName)!);
            var hasS57Catalogue = files.Any(f =>
                string.Equals(Path.GetFileName(f), S57CatalogueName, StringComparison.OrdinalIgnoreCase));

            if (s100Catalogue is not null || hasS57Catalogue)
            {
                if (s100Catalogue is not null)
                    units.Add(new S100FolderUnit(directory, s100Catalogue));
                if (hasS57Catalogue)
                    units.Add(new S57FolderUnit(directory));
                StampTree(sourceRoot, directory, stamps);
                continue;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (IsZip(file))
                {
                    units.Add(new ZipUnit(file));
                    Stamp(sourceRoot, new FileInfo(file), stamps);
                }
                else if (IsLooseCandidate(name))
                {
                    var unit = new LooseFileUnit(file, FindUpdates(file, files));
                    units.Add(unit);
                    Stamp(sourceRoot, new FileInfo(file), stamps);
                    foreach (var update in unit.UpdatePaths)
                        Stamp(sourceRoot, new FileInfo(update), stamps);
                }
            }

            if (!recursive)
                continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory, "*", TopLevel)
                    .Where(d => !Path.GetFileName(d).StartsWith('.'))
                    .OrderByDescending(d => d, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
                pending.Push(child);
        }
    }

    /// <summary>
    /// Returns the S-57 sequential update files (<c>.001</c>–<c>.999</c>)
    /// that share <paramref name="basePath"/>'s stem, in update order. Only
    /// <c>.000</c> base cells have updates.
    /// </summary>
    private static IReadOnlyList<string> FindUpdates(string basePath, IEnumerable<string> siblings)
    {
        if (!string.Equals(Path.GetExtension(basePath), ".000", StringComparison.OrdinalIgnoreCase))
            return [];

        var stem = Path.GetFileNameWithoutExtension(basePath);
        return siblings
            .Select(f => (Path: f, Number: UpdateNumberOf(f, stem)))
            .Where(u => u.Number > 0)
            .OrderBy(u => u.Number)
            .Select(u => u.Path)
            .ToArray();
    }

    private static int UpdateNumberOf(string path, string stem)
    {
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), stem, StringComparison.OrdinalIgnoreCase))
            return -1;

        var ext = Path.GetExtension(path);
        return ext.Length == 4
            && int.TryParse(ext.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : -1;
    }

    internal static string? PickS100Catalogue(IEnumerable<string> fileNames)
    {
        string? fallback = null;
        foreach (var name in fileNames)
        {
            if (string.Equals(name, S100CatalogueNames[0], StringComparison.OrdinalIgnoreCase))
                return name;
            if (fallback is null && IsS100CatalogueName(name))
                fallback = name;
        }

        return fallback;
    }

    internal static bool IsS100CatalogueName(string fileName) =>
        Array.Exists(S100CatalogueNames, n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));

    private static bool IsZip(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsLooseCandidate(string fileName) =>
        Array.Exists(LooseExtensions, e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static void StampTree(string sourceRoot, string directory, List<string> stamps)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", Recursive))
                Stamp(sourceRoot, new FileInfo(file), stamps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stamps.Add(RelativePath(sourceRoot, directory) + "|unreadable");
        }
    }

    private static void Stamp(string sourceRoot, FileInfo file, List<string> stamps)
    {
        try
        {
            stamps.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{RelativePath(sourceRoot, file.FullName)}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stamps.Add(RelativePath(sourceRoot, file.FullName) + "|unreadable");
        }
    }

    private static string Fingerprint(List<string> stamps)
    {
        stamps.Sort(StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', stamps)));
        return FingerprintVersion + ":" + Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns <paramref name="path"/> relative to <paramref name="root"/>
    /// with forward slashes, or <c>"."</c> for the root itself.
    /// </summary>
    internal static string RelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Length == 0 ? "." : relative;
    }
}

/// <summary>The planned work for one local source.</summary>
/// <param name="RootDirectory">The folder that item keys are relative to.</param>
/// <param name="Units">The exchange sets, ZIPs and loose files found.</param>
/// <param name="Fingerprint">The source's fingerprint.</param>
internal sealed record ScanResult(string RootDirectory, IReadOnlyList<ScanUnit> Units, string Fingerprint);

/// <summary>One thing to index.</summary>
internal abstract record ScanUnit;

/// <summary>A folder holding an S-100 exchange-set catalogue.</summary>
internal sealed record S100FolderUnit(string Directory, string CatalogueFileName) : ScanUnit;

/// <summary>A folder holding an S-57 <c>CATALOG.031</c>.</summary>
internal sealed record S57FolderUnit(string Directory) : ScanUnit;

/// <summary>A ZIP archive that may hold one or more exchange sets.</summary>
internal sealed record ZipUnit(string Path) : ScanUnit;

/// <summary>A loose dataset file and its sequential updates.</summary>
internal sealed record LooseFileUnit(string Path, IReadOnlyList<string> UpdatePaths) : ScanUnit;
