using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Discovers S-101 sequential update files (<c>….001</c>, <c>….002</c>, …) that
/// sit alongside a base cell (<c>….000</c>) on the local file system, so a
/// command-line caller pointed at a loose base cell can render it at its
/// up-to-date state. This is the file-system analogue of
/// <see cref="S101ExchangeSetUpdatePlan"/> (which groups updates from an
/// exchange-set catalogue). S-101 / S-100 Part 10a.
/// </summary>
/// <remarks>
/// Matching is by cell name (the file name without its numeric extension,
/// case-insensitive) within the base cell's own directory only; updates that
/// live elsewhere are not considered. Every sibling with a positive numeric
/// extension is returned in ascending update-number order — gaps are
/// intentionally not filtered out here so the applicator can surface a
/// best-effort warning when the sequence is non-contiguous.
/// </remarks>
public static class S101FilesystemUpdateDiscovery
{
    /// <summary>
    /// Finds the sequential update files that target the base cell at
    /// <paramref name="baseFilePath"/>.
    /// </summary>
    /// <param name="baseFilePath">Path to a base cell (a <c>….000</c> file).</param>
    /// <returns>
    /// The full paths of the sibling update files, ordered by ascending update
    /// number. Empty when <paramref name="baseFilePath"/> is not a <c>….000</c>
    /// base cell, its directory cannot be resolved, or no updates are present.
    /// </returns>
    public static IReadOnlyList<string> FindSequentialUpdates(string baseFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseFilePath);

        // Only a base cell (….000) can carry sequential updates.
        if (!string.Equals(Path.GetExtension(baseFilePath), ".000", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var fullBase = Path.GetFullPath(baseFilePath);
        var directory = Path.GetDirectoryName(fullBase);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();

        var cellName = Path.GetFileNameWithoutExtension(fullBase);

        var updates = new List<(int Number, string Path)>();
        foreach (var candidate in Directory.EnumerateFiles(directory))
        {
            if (!string.Equals(
                    Path.GetFileNameWithoutExtension(candidate),
                    cellName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseUpdateNumber(candidate, out var number) && number > 0)
                updates.Add((number, candidate));
        }

        return updates
            .OrderBy(u => u.Number)
            .Select(u => u.Path)
            .ToList();
    }

    /// <summary>
    /// Parses the numeric S-101 file extension (<c>.001</c> → 1). Returns
    /// <see langword="false"/> when the extension is not exactly three digits.
    /// </summary>
    private static bool TryParseUpdateNumber(string path, out int number)
    {
        number = 0;
        var ext = Path.GetExtension(path);
        return ext.Length == 4
            && int.TryParse(
                ext.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out number);
    }
}
