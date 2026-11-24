using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Reads the <c>--layers-from &lt;FILE&gt;</c> lists that the composite
/// commands take next to repeated <c>--layer</c> (issue #1713). A host that
/// composites hundreds of datasets can't put them all on a Windows command
/// line, which stops at 32,767 characters, so it writes them to a file.
/// </summary>
/// <remarks>
/// A list is UTF-8 text with one dataset path per line. Surrounding
/// whitespace is trimmed, and blank lines and lines starting with <c>#</c>
/// are skipped. A relative path is resolved against the list file's folder,
/// not the current directory, so a list can sit beside its datasets. The
/// listed paths follow any <c>--layer</c> values, in file order, and are
/// checked like them.
/// </remarks>
internal static class LayerListFile
{
    /// <summary>The option's template, shared by every command that takes it.</summary>
    public const string Template = "--layers-from <FILE>";

    /// <summary>The option's description, shared by every command that takes it.</summary>
    public const string Description =
        "Add every dataset listed in a text file as if each were given with --layer (repeatable): one path per line, " +
        "blank lines and lines starting with # skipped, relative paths resolved against the list file's folder. " +
        "Use it when there are too many datasets for the command line.";

    /// <summary>
    /// Gets the dataset paths listed in <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The list file.</param>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be read.</exception>
    public static IReadOnlyList<string> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var layers = new List<string>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            layers.Add(Path.GetFullPath(Path.Combine(folder, line)));
        }

        return layers;
    }

    /// <summary>
    /// Appends the paths listed in each of <paramref name="lists"/> to
    /// <paramref name="layers"/>.
    /// </summary>
    /// <param name="layers">The <c>--layer</c> values; replaced with them followed by the listed paths.</param>
    /// <param name="lists">The <c>--layers-from</c> values.</param>
    /// <returns>
    /// A successful result, or an error naming a list that is blank, missing,
    /// unreadable or empty.
    /// </returns>
    public static ValidationResult Expand(ref string[] layers, IReadOnlyList<string> lists)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(lists);
        if (lists.Count == 0)
            return ValidationResult.Success();

        var all = new List<string>(layers);
        foreach (var list in lists)
        {
            if (string.IsNullOrWhiteSpace(list))
                return ValidationResult.Error("A --layers-from value cannot be empty.");
            if (!File.Exists(list))
                return ValidationResult.Error($"Layer list file not found: {list}");

            IReadOnlyList<string> listed;
            try
            {
                listed = Read(list);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return ValidationResult.Error($"Couldn't read the layer list {list}: {ex.Message}");
            }

            if (listed.Count == 0)
                return ValidationResult.Error($"The layer list {list} names no datasets.");
            all.AddRange(listed);
        }

        layers = [.. all];
        return ValidationResult.Success();
    }
}
