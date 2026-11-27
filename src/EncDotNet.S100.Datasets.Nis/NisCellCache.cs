using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Datasets.Nis.FileGdb;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// Translates every cell of an NIS file geodatabase to an S-57 base cell and
/// keeps the cells in a cache folder, so anything that reads S-57 files can
/// open an NIS.
/// </summary>
/// <remarks>
/// <para>
/// The geodatabase is read once for all its cells. The cells are written to a
/// folder under the cache root named after a fingerprint of the geodatabase's
/// files (names, sizes and write times), so they are reused until the
/// geodatabase changes, and a changed geodatabase gets new cells. Lock files
/// are left out of the fingerprint, so a geodatabase open in ArcGIS doesn't
/// look changed.
/// </para>
/// <para>
/// Each cell is written with <see cref="NisToS57Translator"/>'s defaults,
/// except that its issue date is the geodatabase's last write date, so the
/// same geodatabase always gives the same bytes.
/// </para>
/// </remarks>
public static class NisCellCache
{
    private const string ManifestName = "cells.txt";

    /// <summary>
    /// The cells of an NIS file geodatabase as S-57 base cells, translating
    /// and writing them unless the cache already holds this version of it.
    /// </summary>
    /// <param name="geodatabase">The <c>.gdb</c> directory.</param>
    /// <param name="cacheRoot">The folder that holds the cached cells; created when missing.</param>
    /// <returns>The cells, in ordinal order of their source names.</returns>
    /// <exception cref="InvalidDataException">The directory isn't an NIS file geodatabase.</exception>
    /// <remarks>This method is synchronous and I/O- and CPU-bound.</remarks>
    public static IReadOnlyList<NisCellFile> Materialize(string geodatabase, string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(geodatabase);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);

        var full = System.IO.Path.GetFullPath(geodatabase).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var folder = System.IO.Path.Combine(cacheRoot, Fingerprint(full));
        if (ReadManifest(folder) is { } cached)
            return cached;

        var source = FileGdbNisSource.Open(full);
        var features = source.GetFeatures(NisQuery.All).ToList();
        var relationships = source.GetRelationships().ToList();
        var issueDate = DateOnly.FromDateTime(LastWrite(full));

        var staging = folder + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(staging);
        try
        {
            var cells = new List<NisCellFile>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in features
                .GroupBy(f => f.DatasetName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var name = UniqueName(NisToS57Translator.CellNameOf(cell.Key), usedNames);
                var options = new NisToS57Options { DatasetName = cell.Key, CellName = name, IssueDate = issueDate };
                var document = new NisToS57Translator(options).Translate(new InMemoryNisSource(cell, relationships));
                var file = name + ".000";
                S57DocumentWriter.WriteToFile(System.IO.Path.Combine(staging, file), document);
                cells.Add(new NisCellFile(cell.Key, file));
            }
            File.WriteAllLines(System.IO.Path.Combine(staging, ManifestName), cells.Select(c => $"{c.DatasetName}\t{c.Path}"));

            try
            {
                Directory.Move(staging, folder);
            }
            catch (IOException) when (Directory.Exists(folder))
            {
                // Another process cached the same version first; use its cells.
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        return ReadManifest(folder)
            ?? throw new IOException($"The cached cells of {geodatabase} could not be read back from {folder}.");
    }

    /// <summary>
    /// The fingerprint a geodatabase's cells are cached under: a hash of its
    /// full path and its files' names, sizes and write times.
    /// </summary>
    /// <param name="geodatabase">The <c>.gdb</c> directory.</param>
    /// <returns>32 lower-case hexadecimal digits.</returns>
    public static string Fingerprint(string geodatabase)
    {
        ArgumentException.ThrowIfNullOrEmpty(geodatabase);
        var text = new StringBuilder(System.IO.Path.GetFullPath(geodatabase)).Append('\n');
        foreach (var file in Files(geodatabase))
        {
            var info = new FileInfo(file);
            text.Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), 0, 16);
    }

    /// <summary>The geodatabase's files that hold data, without its lock files, in ordinal order.</summary>
    /// <param name="geodatabase">The <c>.gdb</c> directory.</param>
    /// <returns>The file paths.</returns>
    public static IReadOnlyList<string> Files(string geodatabase)
    {
        ArgumentException.ThrowIfNullOrEmpty(geodatabase);
        return Directory.EnumerateFiles(geodatabase)
            .Where(f => !f.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static List<NisCellFile>? ReadManifest(string folder)
    {
        var manifest = System.IO.Path.Combine(folder, ManifestName);
        if (!File.Exists(manifest)) return null;
        var cells = new List<NisCellFile>();
        foreach (var line in File.ReadAllLines(manifest))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2) return null;
            var path = System.IO.Path.Combine(folder, parts[1]);
            if (!File.Exists(path)) return null;
            cells.Add(new NisCellFile(parts[0], path));
        }
        return cells;
    }

    private static DateTime LastWrite(string geodatabase)
    {
        var files = Files(geodatabase);
        return files.Count == 0 ? Directory.GetLastWriteTimeUtc(geodatabase) : files.Max(File.GetLastWriteTimeUtc);
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var candidate = name;
        for (int i = 2; !used.Add(candidate); i++)
            candidate = $"{name}_{i.ToString(CultureInfo.InvariantCulture)}";
        return candidate;
    }
}
