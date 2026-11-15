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

    /// <summary>
    /// The WMTS layer a client put each dataset in (issue #1495), by full
    /// path, in the order they were put there; datasets not listed are
    /// grouped as <c>--layers-by</c> says.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Layers { get; init; } = [];

    /// <summary>Whether the start-up datasets are served as they are.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Layers.Count == 0;

    /// <summary>Whether these changes serve the same datasets in the same layers as <paramref name="other"/>.</summary>
    public bool SameAs(DatasetChanges other) =>
        Added.SequenceEqual(other.Added) && Removed.SequenceEqual(other.Removed) && Layers.SequenceEqual(other.Layers);

    /// <summary>The layer a client put <paramref name="path"/> in, or <see langword="null"/>.</summary>
    public string? LayerOf(string path)
    {
        path = Path.GetFullPath(path);
        foreach (var (dataset, layer) in Layers)
        {
            if (PathComparer.Equals(dataset, path))
                return layer;
        }

        return null;
    }

    /// <summary>
    /// These changes with <paramref name="path"/> in <paramref name="layer"/>,
    /// or, for <see langword="null"/>, back in the layer <c>--layers-by</c> gives it.
    /// </summary>
    public DatasetChanges WithLayer(string path, string? layer)
    {
        path = Path.GetFullPath(path);
        if (LayerOf(path) == layer)
            return this;

        var others = Layers.Where(l => !PathComparer.Equals(l.Key, path));
        return this with { Layers = layer is null ? [.. others] : [.. others, new(path, layer)] };
    }

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

    /// <summary>These changes with <paramref name="path"/> no longer served (nor in a layer).</summary>
    public DatasetChanges WithRemoved(string path)
    {
        path = Path.GetFullPath(path);
        var changes = WithLayer(path, null);
        if (changes.Added.Contains(path, PathComparer))
            return changes with { Added = [.. changes.Added.Where(a => !PathComparer.Equals(a, path))] };
        return changes.Removed.Contains(path, PathComparer) ? changes : changes with { Removed = [.. changes.Removed, path] };
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
