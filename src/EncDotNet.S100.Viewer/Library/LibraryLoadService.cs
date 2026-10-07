using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Library;

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

    /// <summary>
    /// Opens <paramref name="items"/> as <see cref="LoadAsync(IReadOnlyList{CollectionItem}, bool, CancellationToken)"/>
    /// does, showing each Library source's datasets under one Datasets row (#809).
    /// </summary>
    /// <param name="items">The items to open.</param>
    /// <param name="defer">True to load as the items come into view.</param>
    /// <param name="sourceOf">The Library source of an item.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer, Func<CollectionItem, LibrarySourceLabel?> sourceOf,
        CancellationToken cancellationToken = default) =>
        LoadAsync(items, defer, cancellationToken);
}

/// <summary>
/// The viewer's <see cref="ILibraryLoader"/> (issue #655): the host-neutral
/// <see cref="LibraryLoader"/> (#792) opening through
/// <see cref="ExchangeSetOpener"/>, plus the viewer's warning and
/// load-as-you-pan notifications.
/// </summary>
internal sealed class LibraryLoadService : ILibraryLoader, IDisposable
{
    private readonly ExchangeSetOpener _opener;
    private readonly LibraryLoader _loader;
    private readonly INotificationService? _notifications;

    public LibraryLoadService(IExchangeSetService exchangeSets, DatasetsViewModel datasets, INotificationService? notifications = null)
    {
        _opener = new ExchangeSetOpener(exchangeSets, datasets);
        _loader = new LibraryLoader(_opener);
        _notifications = notifications;
    }

    public event EventHandler? Changed
    {
        add => _loader.Changed += value;
        remove => _loader.Changed -= value;
    }

    public LibraryLoadState StateOf(CollectionItem item) => _loader.StateOf(item);

    public Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
        LoadAsync(items, defer, _ => null, cancellationToken);

    public async Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer, Func<CollectionItem, LibrarySourceLabel?> sourceOf,
        CancellationToken cancellationToken = default)
    {
        var result = await _loader.LoadAsync(items, defer, sourceOf, cancellationToken).ConfigureAwait(true);

        foreach (var problem in result.Problems ?? [])
        {
            _notifications?.Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(problem)
                .Show();
        }

        if (defer && _notifications is not null && result.Opened > 0)
        {
            _notifications.Create(Strings.Toast_LibraryDeferredTitle)
                .WithSeverity(NotificationSeverity.Info)
                .WithContent(string.Format(CultureInfo.CurrentCulture,
                    result.Skipped > 0 ? Strings.Toast_LibraryDeferredSkippedFormat : Strings.Toast_LibraryDeferredFormat,
                    result.Opened, result.Skipped))
                .Show();
        }

        return result;
    }

    public void Dispose()
    {
        _loader.Dispose();
        _opener.Dispose();
    }

    /// <summary>
    /// Opens a group through <see cref="IExchangeSetService.OpenSubsetAsync"/>,
    /// so each exchange set (or folder) gets one source and one Datasets-panel
    /// header, and remembers which entry each item became so the Library can
    /// show it as loaded or deferred.
    /// </summary>
    internal sealed class ExchangeSetOpener : ILibraryDatasetOpener, IDisposable
    {
        private readonly IExchangeSetService _exchangeSets;
        private readonly DatasetsViewModel _datasets;
        private readonly Dictionary<string, DatasetEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public ExchangeSetOpener(IExchangeSetService exchangeSets, DatasetsViewModel datasets)
        {
            ArgumentNullException.ThrowIfNull(exchangeSets);
            ArgumentNullException.ThrowIfNull(datasets);
            _exchangeSets = exchangeSets;
            _datasets = datasets;
            _datasets.Entries.CollectionChanged += OnEntriesChanged;
        }

        public event EventHandler? Changed;

        public LibraryLoadState StateOf(LocalItemLocation location)
        {
            if (!_entries.TryGetValue(Key(location.RootPath, location.RelativePath), out var entry))
                return LibraryLoadState.None;

            return entry.IsLoaded ? LibraryLoadState.Loaded
                : entry.IsDeferred ? LibraryLoadState.Deferred
                : LibraryLoadState.None;
        }

        public async Task<LibraryOpenOutcome> OpenAsync(LibraryOpenGroup group, bool defer, CancellationToken cancellationToken)
        {
            var request = new ExchangeSetSubsetRequest(
                group.RootPath,
                group.CatalogueRelativePath,
                group.Items.Select(ToSubsetItem).ToArray(),
                group.Source);

            var entries = await _exchangeSets.OpenSubsetAsync(request, defer, cancellationToken).ConfigureAwait(true);

            for (var i = 0; i < entries.Count && i < group.Items.Count; i++)
            {
                var local = LibraryOpenGroup.LocationOf(group.Items[i]);
                var key = Key(local.RootPath, local.RelativePath);
                if (_entries.TryGetValue(key, out var previous) && !ReferenceEquals(previous, entries[i]))
                    previous.PropertyChanged -= OnEntryChanged;
                if (!ReferenceEquals(previous, entries[i]))
                    entries[i].PropertyChanged += OnEntryChanged;
                _entries[key] = entries[i];
            }

            return new LibraryOpenOutcome(entries.Count, []);
        }

        private static ExchangeSetSubsetItem ToSubsetItem(CollectionItem item)
        {
            var local = LibraryOpenGroup.LocationOf(item);
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
}
