using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>Whether a library item is open in the session.</summary>
internal enum LibraryLoadState
{
    /// <summary>Not opened from the library.</summary>
    None,

    /// <summary>Registered to load as it comes into view.</summary>
    Deferred,

    /// <summary>Loaded on the map.</summary>
    Loaded,
}

/// <summary>The outcome of <see cref="ILibraryLoader.LoadAsync"/>.</summary>
/// <param name="Opened">Items opened (loaded or registered to load as you pan).</param>
/// <param name="Skipped">Items that could not be opened (online, missing, catalogue-only, unknown product).</param>
internal sealed record LibraryLoadResult(int Opened, int Skipped);

/// <summary>Opens library items in the viewer session.</summary>
internal interface ILibraryLoader
{
    /// <summary>Raised when any opened item's state changes.</summary>
    event EventHandler? Changed;

    /// <summary>The session state of <paramref name="item"/>.</summary>
    LibraryLoadState StateOf(CollectionItem item);

    /// <summary>
    /// Opens the local items among <paramref name="items"/>: loads them now,
    /// or with <paramref name="defer"/> registers them to load as they come
    /// into view.
    /// </summary>
    Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens library items (issue #655) through
/// <see cref="IExchangeSetService.OpenSubsetAsync"/>, grouping them by the
/// exchange set (or folder) they belong to so each set gets one source and
/// one Datasets-panel header, and remembers which entry each item became so
/// the Library can show it as loaded or deferred.
/// </summary>
internal sealed class LibraryLoadService : ILibraryLoader, IDisposable
{
    private readonly IExchangeSetService _exchangeSets;
    private readonly DatasetsViewModel _datasets;
    private readonly INotificationService? _notifications;
    private readonly Dictionary<string, DatasetEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public LibraryLoadService(IExchangeSetService exchangeSets, DatasetsViewModel datasets, INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(exchangeSets);
        ArgumentNullException.ThrowIfNull(datasets);
        _exchangeSets = exchangeSets;
        _datasets = datasets;
        _notifications = notifications;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
    }

    public event EventHandler? Changed;

    public LibraryLoadState StateOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Location is not LocalItemLocation local || !_entries.TryGetValue(Key(local.RootPath, local.RelativePath), out var entry))
            return LibraryLoadState.None;

        return entry.IsLoaded ? LibraryLoadState.Loaded
            : entry.IsDeferred ? LibraryLoadState.Deferred
            : LibraryLoadState.None;
    }

    public async Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var skipped = 0;
        var groups = new Dictionary<(string Root, string? Catalogue), List<CollectionItem>>();
        foreach (var item in items)
        {
            if (item.Location is not LocalItemLocation local
                || item.ProductSpec == "Unknown"
                || LibraryAvailabilityResolver.Resolve(item) != LibraryAvailability.Local)
            {
                skipped++;
                continue;
            }

            var key = (local.RootPath, local.CatalogueRelativePath);
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = [];
            group.Add(item);
        }

        var opened = 0;
        foreach (var ((root, catalogue), group) in groups)
        {
            var request = new ExchangeSetSubsetRequest(
                root,
                catalogue,
                group.Select(ToSubsetItem).ToArray());

            IReadOnlyList<DatasetEntry> entries;
            try
            {
                entries = await _exchangeSets.OpenSubsetAsync(request, defer, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                skipped += group.Count;
                _notifications?.Create(Strings.Toast_Warning)
                    .WithSeverity(NotificationSeverity.Warning)
                    .WithContent(ex.Message)
                    .Show();
                continue;
            }

            for (var i = 0; i < entries.Count && i < group.Count; i++)
            {
                var local = (LocalItemLocation)group[i].Location;
                var key = Key(local.RootPath, local.RelativePath);
                if (_entries.TryGetValue(key, out var previous) && !ReferenceEquals(previous, entries[i]))
                    previous.PropertyChanged -= OnEntryChanged;
                if (!ReferenceEquals(previous, entries[i]))
                    entries[i].PropertyChanged += OnEntryChanged;
                _entries[key] = entries[i];
            }

            opened += entries.Count;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        if (defer && _notifications is not null && opened > 0)
        {
            _notifications.Create(Strings.Toast_LibraryDeferredTitle)
                .WithSeverity(NotificationSeverity.Info)
                .WithContent(string.Format(CultureInfo.CurrentCulture,
                    skipped > 0 ? Strings.Toast_LibraryDeferredSkippedFormat : Strings.Toast_LibraryDeferredFormat,
                    opened, skipped))
                .Show();
        }

        return new LibraryLoadResult(opened, skipped);
    }

    private static ExchangeSetSubsetItem ToSubsetItem(CollectionItem item)
    {
        var local = (LocalItemLocation)item.Location;
        return new ExchangeSetSubsetItem(
            local.RelativePath,
            local.UpdateRelativePaths,
            item.ProductSpec,
            item.Name,
            item.Bounds is { } b
                ? new ExchangeSets.BoundingBox
                {
                    WestBoundLongitude = b.West,
                    EastBoundLongitude = b.East,
                    SouthBoundLatitude = b.South,
                    NorthBoundLatitude = b.North,
                }
                : null,
            item.MinimumDisplayScale,
            item.MaximumDisplayScale);
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatasetEntry.IsLoaded) or nameof(DatasetEntry.IsDeferred))
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset))
            return;

        var removed = _entries.Where(p => !_datasets.Entries.Contains(p.Value)).ToArray();
        foreach (var (key, entry) in removed)
        {
            entry.PropertyChanged -= OnEntryChanged;
            _entries.Remove(key);
        }

        if (removed.Length > 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Key(string root, string relativePath) =>
        Path.TrimEndingDirectorySeparator(root) + "|" + relativePath.Replace('\\', '/');

    public void Dispose()
    {
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _entries.Values)
            entry.PropertyChanged -= OnEntryChanged;
        _entries.Clear();
    }
}
