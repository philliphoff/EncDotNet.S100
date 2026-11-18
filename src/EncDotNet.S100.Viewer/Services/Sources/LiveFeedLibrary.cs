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
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>?> _onMap = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>The feeds a user can subscribe to; one that needs settings asks for them in the Library (slice 9b).</summary>
    public IReadOnlyList<SourceRegistration> Offered => _sources.Registrations;

    /// <summary>The registration ids of the feeds subscribed to in the Library.</summary>
    public IReadOnlySet<string> Subscribed => Feeds().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The subscribed feeds by registration id (the first, should one be subscribed twice).</summary>
    private Dictionary<string, LiveFeedSource> Feeds()
    {
        var feeds = new Dictionary<string, LiveFeedSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var feed in _library.Collections.SelectMany(c => c.Sources).Select(s => s.Definition).OfType<LiveFeedSource>())
            feeds.TryAdd(feed.RegistrationId, feed);
        return feeds;
    }

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

    /// <summary>
    /// Subscribes to or unsubscribes from the feed registered as
    /// <paramref name="registrationId"/>, as MCP's tools do. Unsubscribing
    /// removes it from the Library, which takes it off the map.
    /// </summary>
    /// <param name="registrationId">The feed's registration id.</param>
    /// <param name="subscribed">Whether to subscribe.</param>
    /// <returns><see langword="false"/> when no such feed is registered.</returns>
    public bool SetSubscribed(string registrationId, bool subscribed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        if (_sources.Find(registrationId) is not { } registration)
            return false;
        if (subscribed)
        {
            Subscribe(registration);
            return true;
        }
        foreach (var collection in _library.Collections)
        {
            foreach (var source in collection.Sources)
            {
                if (source.Definition is LiveFeedSource feed
                    && string.Equals(feed.RegistrationId, registrationId, StringComparison.OrdinalIgnoreCase))
                {
                    _library.RemoveSource(collection.Id, feed.Id);
                }
            }
        }
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
    /// Puts every subscribed feed on the map with its options, unless it was
    /// removed from the map this session; puts a feed whose options changed
    /// back with the new ones; and takes off the map the feeds no longer subscribed.
    /// </summary>
    internal void Sync()
    {
        var feeds = Feeds();
        foreach (var (id, feed) in feeds)
        {
            if (_removedThisSession.Contains(id) || _sources.Find(id) is not { } registration)
                continue;
            if (_onMap.TryGetValue(id, out var shown))
            {
                if (SameOptions(shown, feed.Options))
                    continue;
                // New settings: put it back with them.
                _onMap.Remove(id);
                if (_datasets.FindSource(id) is { } old)
                    _datasets.Entries.Remove(old);
            }
            _onMap[id] = feed.Options;
            if (_datasets.AddSource(registration, OptionsOf(registration, feed)) is { } entry)
                _datasets.RequestLoad(entry);
        }
        foreach (var id in _onMap.Keys.Where(id => !feeds.ContainsKey(id)).ToArray())
        {
            _onMap.Remove(id);
            if (_datasets.FindSource(id) is { } entry)
                _datasets.Entries.Remove(entry);
        }
    }

    /// <summary>The feed's options from its settings, or the defaults when they don't parse (its row then says why it fails).</summary>
    private static object? OptionsOf(SourceRegistration registration, LiveFeedSource feed)
    {
        try
        {
            return SourceOptionsForm.Build(registration, feed.Options);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool SameOptions(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if ((a?.Count ?? 0) != (b?.Count ?? 0))
            return false;
        if (a is null || b is null)
            return true; // Both empty.
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
                return false;
        }
        return true;
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
