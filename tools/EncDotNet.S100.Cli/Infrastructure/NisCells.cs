using System.Collections.Concurrent;
using EncDotNet.S100.Datasets.Nis;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Opens Esri ArcGIS Maritime S-57 NIS file geodatabases as S-57 cells for
/// the dataset commands (#1777): a <c>.gdb</c> becomes one S-57 dataset per
/// source cell, and <c>&lt;path&gt;.gdb/&lt;DSNM&gt;</c> names one of them.
/// </summary>
/// <remarks>
/// The cells are translated with <see cref="NisCellCache"/> and kept under
/// the user's local application data, so the rest of the CLI reads them as
/// ordinary S-57 files and a geodatabase is translated again only when its
/// files change.
/// </remarks>
internal static class NisCells
{
    /// <summary>The spec a translated cell is opened as.</summary>
    public const string Spec = "S-57";

    /// <summary>Where the translated cells are kept.</summary>
    public static string CacheRoot { get; } = Path.Combine(CliPaths.LocalApplicationData, "EncDotNet.S100", "NisCells");

    private static readonly ConcurrentDictionary<string, string> Resolved = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> Errors = new(StringComparer.Ordinal);

    /// <summary>
    /// A dataset path argument with a cell path (<c>&lt;path&gt;.gdb/&lt;DSNM&gt;</c>)
    /// replaced by its translated S-57 file; any other value, or a cell path
    /// that can't be resolved, is returned as it is, and
    /// <see cref="ErrorFor"/> then says why.
    /// </summary>
    public static string? ResolveArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || File.Exists(value) || Directory.Exists(value)) return value;
        if (Resolved.TryGetValue(value, out var file)) return file;
        if (ResolveCellPath(value, out var error) is { } cell)
        {
            Resolved[value] = cell.File;
            return cell.File;
        }
        if (error is not null) Errors[value] = error;
        return value;
    }

    /// <summary>Resolves each of several dataset path arguments with <see cref="ResolveArgument"/>.</summary>
    public static string[] ResolveArguments(string[] values) => values.Select(v => ResolveArgument(v) ?? v).ToArray();

    /// <summary>Why <see cref="ResolveArgument"/> couldn't resolve a cell path, or <see langword="null"/>.</summary>
    public static string? ErrorFor(string? value) => value is not null && Errors.TryGetValue(value, out var error) ? error : null;

    /// <summary>Whether a directory is an NIS file geodatabase.</summary>
    public static bool IsGeodatabase(string directory) => Datasets.Nis.FileGdb.FileGdbNisSource.IsNisGeodatabase(directory);

    /// <summary>
    /// The cells of an NIS file geodatabase, translated (or from the cache);
    /// <see langword="null"/>, with the reason added to
    /// <paramref name="skipped"/>, when it can't be read.
    /// </summary>
    public static IReadOnlyList<NisCellFile>? Materialize(string geodatabase, List<SkippedDataset> skipped)
    {
        try
        {
            return NisCellCache.Materialize(geodatabase, CacheRoot);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedDataset(
                geodatabase,
                SkippedDatasetKind.Failed,
                $"Skipped: the NIS couldn't be read ({ex.Message}).",
                $"Skipped unreadable NIS file geodatabase '{geodatabase}': {ex.Message}"));
            return null;
        }
    }

    /// <summary>
    /// The translated S-57 file for a cell path (<c>&lt;path&gt;.gdb/&lt;DSNM&gt;</c>)
    /// and the dataset id to show for it (<c>&lt;name&gt;.gdb/&lt;DSNM&gt;</c>), or
    /// <see langword="null"/> when <paramref name="path"/> isn't one or names no
    /// cell; <paramref name="error"/> then says why when it looked like one.
    /// </summary>
    public static (string File, string Id)? ResolveCellPath(string path, out string? error)
    {
        error = null;
        if (!NisCellPath.TryParse(path, out var cellPath) || cellPath is null) return null;
        var skipped = new List<SkippedDataset>();
        var cells = Materialize(cellPath.Geodatabase, skipped);
        if (cells is null)
        {
            error = skipped.FirstOrDefault()?.Warning;
            return null;
        }
        if (cellPath.Find(cells) is { } cell) return (cell.Path, $"{Path.GetFileName(cellPath.Geodatabase)}/{cell.DatasetName}");
        error = $"The NIS {cellPath.Geodatabase} holds no cell named {cellPath.DatasetName}. It holds: {string.Join(", ", cells.Select(c => c.DatasetName))}.";
        return null;
    }
}
