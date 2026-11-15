using EncDotNet.S100.Datasets.Pipelines;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The datasets added to and removed from the ones <c>tiles serve</c> was
/// started with, while it serves (issue #1494). Immutable: each change makes a
/// new instance. Paths are full paths.
/// </summary>
/// <param name="Added">Datasets served besides the start-up ones, in the order they were added.</param>
/// <param name="Removed">Start-up datasets no longer served.</param>
internal sealed record DatasetChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    /// <summary>No changes: the start-up datasets.</summary>
    public static readonly DatasetChanges None = new([], []);

    /// <summary>Whether the start-up datasets are served as they are.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// These changes with <paramref name="path"/> served too: a removed
    /// start-up dataset is served again, any other is added after the rest.
    /// </summary>
    public DatasetChanges WithAdded(string path)
    {
        path = Path.GetFullPath(path);
        if (Removed.Contains(path, PathComparer))
            return this with { Removed = [.. Removed.Where(r => !PathComparer.Equals(r, path))] };
        return Added.Contains(path, PathComparer) ? this : this with { Added = [.. Added, path] };
    }

    /// <summary>These changes with <paramref name="path"/> no longer served.</summary>
    public DatasetChanges WithRemoved(string path)
    {
        path = Path.GetFullPath(path);
        if (Added.Contains(path, PathComparer))
            return this with { Added = [.. Added.Where(a => !PathComparer.Equals(a, path))] };
        return Removed.Contains(path, PathComparer) ? this : this with { Removed = [.. Removed, path] };
    }

    /// <summary>Paths are matched as the file system matches them.</summary>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Applies the changes to the start-up <paramref name="inputs"/>. On
    /// failure (an added dataset that isn't recognised), prints why.
    /// </summary>
    internal bool TryApply(ref IReadOnlyList<(string Path, string Spec, bool ApplyUpdates)> inputs, bool applyUpdates)
    {
        var changed = inputs
            .Where(i => !Removed.Contains(Path.GetFullPath(i.Path), PathComparer))
            .ToList();
        foreach (var path in Added)
        {
            if (changed.Any(i => PathComparer.Equals(Path.GetFullPath(i.Path), path)))
                continue;

            var spec = DatasetPipelineFactory.DetectProductSpec(path);
            if (spec is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Not a dataset that can be served:[/] {path}");
                return false;
            }

            changed.Add((path, spec, applyUpdates));
        }

        inputs = changed;
        return true;
    }
}
