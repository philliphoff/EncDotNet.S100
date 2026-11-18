using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>
/// Live feeds in the Library (data source design note D8, slice 9): the Library
/// holds the subscription, as a <see cref="LiveFeedSource"/> in a "Live feeds"
/// collection, and the Datasets panel holds what's on the map. Every
/// subscribed feed gets a Datasets entry; removing the entry takes it off the
/// map until the next start, and unsubscribing in the Library takes it off for
/// good.
/// </summary>
internal sealed class LiveFeedLibrary : IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly SourceDatasetService _sources;
    private readonly DatasetsViewModel _datasets;
    private readonly ViewerSettings _settings;
    private readonly Action<Action> _post;
    private readonly Action<string>? _removedFromMap;
    private readonly HashSet<string> _onMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedThisSession = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    /// <summary>Creates the service; <see cref="Start"/> begins following the Library.</summary>
    /// <param name="library">The Library, which holds the subscriptions.</param>
    /// <param name="sources">The feeds on offer.</param>
    /// <param name="datasets">The Datasets panel, which holds what's on the map.</param>
    /// <param name="settings">The settings, for the feeds saved before the Library held them.</param>
    /// <param name="post">Runs an action on the UI thread; the Library changes off it.</param>
    /// <param name="removedFromMap">
    /// Called with a feed's name when it's removed from the map but still
    /// subscribed, to say it comes back at the next start.
    /// </param>
    public LiveFeedLibrary(
        CollectionLibrary library,
        SourceDatasetService sources,
        DatasetsViewModel datasets,
        ViewerSettings settings,
        Action<Action>? post = null,
        Action<string>? removedFromMap = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(settings);
        _library = library;
        _sources = sources;
        _datasets = datasets;
        _settings = settings;
        _post = post ?? (static action => action());
        _removedFromMap = removedFromMap;
    }

    /// <summary>The feeds a user can subscribe to: those that need no options, or have defaults.</summary>
    public IReadOnlyList<SourceRegistration> Offered =>
        [.. _sources.Registrations.Where(r => r.OptionsType is null || r.DefaultOptions is not null)];

    /// <summary>The registration ids of the feeds subscribed to in the Library.</summary>
    public IReadOnlySet<string> Subscribed =>
        _library.Collections
            .SelectMany(c => c.Sources)
            .Select(s => s.Definition)
            .OfType<LiveFeedSource>()
            .Select(f => f.RegistrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Moves the feeds saved before the Library held them
    /// (<see cref="ViewerSettings.SourceDatasets"/>) into it, puts every
    /// subscribed feed on the map, and follows the Library from then on.
    /// </summary>
    public void Start()
    {
        if (_started)
            return;
        _started = true;
        MigrateSavedFeeds();
        _library.Changed += OnLibraryChanged;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        Sync();
    }

    /// <summary>
    /// Subscribes to a feed in the Library, which puts it on the map. Does
    /// nothing when it's already subscribed, except put it back on the map.
    /// </summary>
    /// <param name="registration">The feed.</param>
    /// <returns><see langword="true"/> when a subscription was added.</returns>
    public bool Subscribe(SourceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _removedThisSession.Remove(registration.Id);
        if (Subscribed.Contains(registration.Id))
        {
            Sync();
            return false;
        }
        var source = new LiveFeedSource(Guid.NewGuid(), registration.DisplayName, registration.Id);
        if (_library.Collections.FirstOrDefault(IsLiveFeedsCollection) is { } collection)
            _library.AddSources(collection.Id, [source]);
        else
            _library.AddCollection(Strings.Library_LiveFeedsCollection, [source]);
        Sync();
        return true;
    }

    private static bool IsLiveFeedsCollection(LibraryCollection collection) =>
        !collection.IsSession
        && collection.Sources.Count > 0
        && collection.Sources.All(s => s.Definition is LiveFeedSource);

    private void MigrateSavedFeeds()
    {
        if (_settings.SourceDatasets.Count == 0)
            return;
        var saved = _settings.SourceDatasets.ToArray();
        _settings.SourceDatasets.Clear();
        foreach (var id in saved)
        {
            if (_sources.Find(id) is { } registration && !Subscribed.Contains(registration.Id))
            {
                var source = new LiveFeedSource(Guid.NewGuid(), registration.DisplayName, registration.Id);
                if (_library.Collections.FirstOrDefault(IsLiveFeedsCollection) is { } collection)
                    _library.AddSources(collection.Id, [source]);
                else
                    _library.AddCollection(Strings.Library_LiveFeedsCollection, [source]);
            }
        }
        try
        {
            _settings.Save();
        }
        catch (IOException)
        {
            // Saved with the next settings change.
        }
        catch (UnauthorizedAccessException)
        {
            // Saved with the next settings change.
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e) => _post(Sync);

    /// <summary>
    /// Puts every subscribed feed on the map, unless it was removed from the
    /// map this session, and takes off the map the feeds no longer subscribed.
    /// </summary>
    internal void Sync()
    {
        var subscribed = Subscribed;
        foreach (var id in subscribed)
        {
            if (_onMap.Contains(id) || _removedThisSession.Contains(id) || _sources.Find(id) is not { } registration)
                continue;
            _onMap.Add(id);
            if (_datasets.AddSource(registration) is { } entry)
                _datasets.RequestLoad(entry);
        }
        foreach (var id in _onMap.Where(id => !subscribed.Contains(id)).ToArray())
        {
            _onMap.Remove(id);
            if (_datasets.FindSource(id) is { } entry)
                _datasets.Entries.Remove(entry);
        }
    }

    private void OnEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null || e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            return;
        foreach (var entry in e.OldItems.OfType<DatasetEntry>())
        {
            if (entry.SourceId is not { } id || !_onMap.Remove(id))
                continue;
            // D8: removing from the map isn't unsubscribing; it comes back at the next start.
            if (Subscribed.Contains(id))
            {
                _removedThisSession.Add(id);
                _removedFromMap?.Invoke(entry.DisplayName);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_started)
            return;
        _library.Changed -= OnLibraryChanged;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
    }
}
