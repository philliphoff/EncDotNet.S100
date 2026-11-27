using EncDotNet.S100.Datasets.Nis.FileGdb;

namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// The path of one cell of an NIS file geodatabase: the <c>.gdb</c> directory
/// and the cell's source name, such as <c>production.gdb/US5CA98M.005</c>.
/// </summary>
/// <remarks>
/// It isn't a file: <see cref="NisCellCache"/> turns it into one. A path is a
/// cell path only when the directory part is an NIS file geodatabase and the
/// whole path names no file or directory on disk.
/// </remarks>
/// <param name="Geodatabase">The <c>.gdb</c> directory.</param>
/// <param name="DatasetName">The cell's source name (<c>DSNM</c>), or the name without its extension.</param>
public sealed record NisCellPath(string Geodatabase, string DatasetName)
{
    /// <summary>Parses a cell path.</summary>
    /// <param name="path">The path.</param>
    /// <param name="cellPath">The cell path, when <paramref name="path"/> is one.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is a cell path.</returns>
    public static bool TryParse(string? path, out NisCellPath? cellPath)
    {
        cellPath = null;
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path) || Directory.Exists(path)) return false;
        var trimmed = path.TrimEnd('/', '\\');
        var directory = Path.GetDirectoryName(trimmed);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name)) return false;
        if (!FileGdbNisSource.IsNisGeodatabase(directory)) return false;
        cellPath = new NisCellPath(directory, name);
        return true;
    }

    /// <summary>
    /// The cell among <paramref name="cells"/> this path names, by its full
    /// source name or its name without extension, ignoring case.
    /// </summary>
    /// <param name="cells">The geodatabase's cells.</param>
    /// <returns>The cell, or <see langword="null"/> when none or several match.</returns>
    public NisCellFile? Find(IEnumerable<NisCellFile> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var matches = cells
            .Where(c => string.Equals(c.DatasetName, DatasetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NisToS57Translator.CellNameOf(c.DatasetName), DatasetName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
