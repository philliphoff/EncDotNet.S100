using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Library;

/// <summary>
/// Opens library items into an <see cref="IMutableDatasetCatalog"/> (issue
/// #792): the headless <see cref="ILibraryDatasetOpener"/> behind a
/// <see cref="LibraryLoader"/> in <c>s100 mcp serve</c>, and usable over the
/// viewer's catalog adapter too. Each item's base file is loaded with
/// <see cref="IMutableDatasetCatalog.LoadAsync"/>, its product specification
/// given as the hint; the catalog ids it produced are remembered so the
/// Library reports the item as loaded while they stay in the catalog.
/// </summary>
/// <remarks>
/// A catalog has no viewport, so "load as you pan" loads now. An item already
/// open is not loaded twice. Items inside a ZIP exchange set are reported as
/// problems: a catalog loads a ZIP only as a whole.
/// </remarks>
public sealed class CatalogLibraryOpener : ILibraryDatasetOpener, IDisposable
{
    private readonly IMutableDatasetCatalog _catalog;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Path, IReadOnlyList<DatasetId> Ids)> _opened = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an opener that loads into <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The host's dataset catalog.</param>
    public CatalogLibraryOpener(IMutableDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _catalog.Changed += OnCatalogChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public LibraryLoadState StateOf(LocalItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return IsOpen(Key(location)) ? LibraryLoadState.Loaded : LibraryLoadState.None;
    }

    /// <inheritdoc />
    public async Task<LibraryOpenOutcome> OpenAsync(LibraryOpenGroup group, bool defer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.IsZip)
        {
            return new LibraryOpenOutcome(0,
                [$"'{group.RootPath}' is a ZIP exchange set; open it whole with open_dataset, or unzip it into a folder."]);
        }

        var opened = 0;
        var problems = new List<string>();
        foreach (var item in group.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = LibraryOpenGroup.LocationOf(item);
            var key = Key(location);
            if (IsOpen(key))
            {
                opened++;
                continue;
            }

            var path = Path.Combine(group.RootPath, location.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            DatasetLoadOutcome outcome;
            try
            {
                outcome = await _catalog.LoadAsync(path, item.ProductSpec, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or FormatException or NotSupportedException or ArgumentException)
            {
                problems.Add($"{item.Name}: {ex.Message}");
                continue;
            }

            if (outcome.Added.Count == 0)
            {
                problems.Add(outcome.Problems is { Count: > 0 } why
                    ? $"{item.Name}: {string.Join("; ", why)}"
                    : $"{item.Name}: nothing in '{path}' could be opened.");
                continue;
            }

            lock (_gate)
                _opened[key] = (Path.GetFullPath(path), outcome.Added);
            opened++;
        }

        if (opened > 0)
            Changed?.Invoke(this, EventArgs.Empty);
        return new LibraryOpenOutcome(opened, problems);
    }

    /// <summary>
    /// True when an item this opener opened is <paramref name="path"/> or is
    /// inside it (a folder) and is still in the catalog: a synced copy the
    /// Library's sync must not prune.
    /// </summary>
    /// <param name="path">A file or folder.</param>
    public bool IsInUse(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var folder = full + Path.DirectorySeparatorChar;
        IReadOnlyList<DatasetId>[] inside;
        lock (_gate)
        {
            inside = [.. _opened.Values
                .Where(o => string.Equals(o.Path, full, StringComparison.Ordinal) || o.Path.StartsWith(folder, StringComparison.Ordinal))
                .Select(o => o.Ids)];
        }

        return inside.Any(Loaded);
    }

    private bool IsOpen(string key)
    {
        (string Path, IReadOnlyList<DatasetId> Ids) opened;
        lock (_gate)
        {
            if (!_opened.TryGetValue(key, out opened))
                return false;
        }

        return Loaded(opened.Ids);
    }

    private bool Loaded(IReadOnlyList<DatasetId> ids)
    {
        var loaded = _catalog.Datasets;
        return ids.Any(id => loaded.Any(d => d.Id.Equals(id)));
    }

    private void OnCatalogChanged(object? sender, DatasetCatalogChangedEventArgs e)
    {
        if (e.Kind is DatasetCatalogChangeKind.Removed or DatasetCatalogChangeKind.Batch or DatasetCatalogChangeKind.Replaced)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Key(LocalItemLocation location) =>
        Path.TrimEndingDirectorySeparator(location.RootPath) + "|" + location.RelativePath.Replace('\\', '/');

    /// <summary>Stops listening to the catalog.</summary>
    public void Dispose() => _catalog.Changed -= OnCatalogChanged;
}
