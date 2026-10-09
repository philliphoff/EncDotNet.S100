using System.Collections.Concurrent;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Library;

/// <summary>
/// The Library tools' reader and editor for a host without view models, such
/// as <c>s100 mcp serve</c> (#792): over a <see cref="LibraryOperations"/>,
/// describing items and sources in the Library core's words
/// (<see cref="LibraryItemText"/>, <see cref="LibraryNodeText"/>), so the
/// results match the viewer's.
/// </summary>
/// <remarks>
/// <para>
/// There is no Timeline, so <c>validAt: view_time</c> matches nothing, and no
/// viewport, so "load as you pan" opens the items now. Sources are added with
/// a <see cref="LibrarySourceDraft"/>, in the same scopes and words as the
/// viewer's Add-to-Library dialog; a SECOM service can be narrowed to a map
/// view only when <see cref="LibraryCatalogueReaders.CurrentMapView"/> gives one.
/// </para>
/// <para>All members are safe to call from any thread.</para>
/// </remarks>
public sealed class HeadlessLibrary : ILibraryReader, ILibraryEditor
{
    private readonly LibraryOperations _operations;
    private readonly LibrarySync? _sync;
    private readonly Func<IReadOnlyList<KnownCatalogueSource>> _userCatalogues;
    private readonly Func<CollectionSource, FeedHealth?>? _health;
    private readonly TimeProvider _time;
    private readonly LibraryCatalogueReaders _readers;
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>>? _probe;

    // Items of the download batches this host started, by source, for the
    // sources' "Downloading 2 of 5 · …" status lines.
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<CollectionItem>> _downloading = new();

    /// <summary>Creates the reader and editor over <paramref name="operations"/>.</summary>
    /// <param name="operations">The Library, its downloads and its loader.</param>
    /// <param name="sync">Keeps synced sources downloaded, or <see langword="null"/> when nothing syncs.</param>
    /// <param name="userCatalogues">The user's own catalogues, listed after the curated ones; none when null.</param>
    /// <param name="health">How a shared feed's or catalogue's server last answered, for status lines; unknown when null.</param>
    /// <param name="time">The clock (forecast windows); the system clock when null.</param>
    /// <param name="readers">Reads online catalogues when a source is added; none can be added without.</param>
    /// <param name="probe">Recognises a catalogue URL's format, for adding by URL; URLs are refused without.</param>
    public HeadlessLibrary(
        LibraryOperations operations,
        LibrarySync? sync = null,
        Func<IReadOnlyList<KnownCatalogueSource>>? userCatalogues = null,
        Func<CollectionSource, FeedHealth?>? health = null,
        TimeProvider? time = null,
        LibraryCatalogueReaders? readers = null,
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
        _sync = sync;
        _userCatalogues = userCatalogues ?? (() => []);
        _health = health;
        _time = time ?? TimeProvider.System;
        _readers = readers ?? new LibraryCatalogueReaders();
        _probe = probe;
    }

    private CollectionLibrary Library => _operations.Library;

    private LibraryDownloads Downloads => _operations.Downloads;

    // ── read ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IReadOnlyList<LibraryCollectionInfo>> ListSourcesAsync(bool counts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<LibraryCollectionInfo> result =
        [
            .. Library.Collections.Select(collection => new LibraryCollectionInfo(
                collection.Id,
                collection.Definition.Name,
                LibraryNodeText.KindOf(collection),
                collection.ItemCount,
                LibraryNodeText.Status(StatusInput(collection, collection.Sources)).Line,
                collection.IsSession,
                [.. collection.Sources.Select(source => Source(collection, source, counts))])),
        ];
        return Task.FromResult(result);
    }

    private LibrarySourceInfo Source(LibraryCollection collection, LibrarySource source, bool counts) => new(
        source.Id,
        LibraryNodeText.SourceName(source, collection),
        LibraryNodeText.KindOf(source.Definition),
        source.State.ToString().ToLowerInvariant(),
        source.Error,
        source.Index?.Items.Count ?? 0,
        source.Index?.IndexedAt,
        LibraryNodeText.Status(StatusInput(collection, [source])).Line,
        LibraryToolResults.SourceUrl(source.Definition),
        counts ? LibraryToolResults.Counts((source.Index?.Items ?? []).Select(item => StateOf(item, source).Availability)) : null,
        SyncOf(source),
        source.Definition.ShowOnMap);

    private LibrarySyncStatus? SyncOf(LibrarySource source) =>
        _sync is not null && _sync.IsSynced(source.Definition) ? _sync.StatusOf(source.Id) : null;

    /// <summary>
    /// What a node knows for its status line: for a source, or a collection,
    /// whose counts and sync are its source's when it has only one.
    /// </summary>
    private LibraryNodeStatusInput StatusInput(LibraryCollection collection, IReadOnlyList<LibrarySource> sources)
    {
        var only = sources is [var single] ? single : null;
        return new LibraryNodeStatusInput(collection, sources)
        {
            Downloading = sources.Select(Downloading).FirstOrDefault(line => line is not null),
            Health = _health,
            Sync = only is null ? null : SyncOf(only),
            CatalogueCounts = only is { Definition: S100CatalogueFeedSource, Index: { } catalogue }
                ? LibraryNodeText.CatalogueCounts(catalogue, Downloads)
                : null,
            ForecastCounts = only is { Definition: S100ForecastFeedSource, Index: { } runs }
                ? LibraryNodeText.ForecastCounts(runs, Downloads, _time.GetUtcNow())
                : null,
        };
    }

    /// <summary>"Downloading 2 of 5 · 4,3 MB left" while a batch this host started is downloading the source's items.</summary>
    private string? Downloading(LibrarySource source)
    {
        if (!_downloading.TryGetValue(source.Id, out var items) || Downloads.Progress is null)
            return null;
        var statuses = items.Select(Downloads.StatusOf).ToArray();
        var pending = statuses.Count(s => s is { State: not LibraryDownloadItemState.Failed });
        if (pending == 0)
            return null;
        var left = items.Zip(statuses)
            .Where(p => p.Second is { State: not LibraryDownloadItemState.Failed })
            .Sum(p => Math.Max(0, ((p.First.Location as RemoteItemLocation)?.SizeBytes ?? 0) - p.Second!.BytesReceived));
        return LibraryNodeText.Downloading(items.Count - pending + 1, items.Count, left);
    }

    /// <inheritdoc />
    public Task<LibraryItemPage?> QueryItemsAsync(LibraryItemPageQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        if (Find(query) is not { } matched)
            return Task.FromResult<LibraryItemPage?>(null);
        var items = matched
            .Skip(query.Page * query.PageSize)
            .Take(query.PageSize)
            .Select(Info)
            .ToArray();
        return Task.FromResult<LibraryItemPage?>(new LibraryItemPage(
            matched.Count, query.Page, query.PageSize, (query.Page + 1) * query.PageSize < matched.Count, items));
    }

    /// <summary>Every item matching <paramref name="query"/>'s filters (ignoring its page), or null when its source id matches nothing.</summary>
    private List<LibraryItemState>? Find(LibraryItemPageQuery query)
    {
        var collections = Library.Collections;
        // No Timeline here: nothing is valid at its view time.
        if (query.ValidAtViewTime)
            return LibraryQuery.ScopeOf(collections, query.SourceId) is null ? null : [];

        IReadOnlySet<LibraryAvailability>? states = query.States?
            .Select(name => LibraryAvailabilityNames.TryParse(name, out var state)
                ? state
                : throw new ArgumentException($"Unknown Library state '{name}'.", nameof(query)))
            .ToHashSet();
        var core = new LibraryItemQuery(query.SourceId, states, query.Spec, query.Text, query.Bounds, query.Point)
        {
            ValidAt = query.ValidAt,
        };
        return LibraryQuery.Find(collections, core, StateOf, state => state);
    }

    /// <inheritdoc />
    public Task<LibraryItemDetail?> DescribeItemAsync(string itemId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ct.ThrowIfCancellationRequested();
        if (LibraryQuery.FindById(Library.Collections, itemId) is not var (item, source))
            return Task.FromResult<LibraryItemDetail?>(null);
        var state = StateOf(item, source);
        var input = TextInput(state);
        return Task.FromResult<LibraryItemDetail?>(LibraryToolResults.Detail(
            state, LibraryItemText.Tags(input).Select(tag => tag.Text), LibraryItemText.Details(input)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KnownSourceInfo>> ListKnownSourcesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<KnownSourceInfo> list =
        [
            .. KnownCatalogueSources.All.Select(source => LibraryToolResults.KnownSource(source, userAdded: false)),
            .. _userCatalogues().Select(source => LibraryToolResults.KnownSource(source, userAdded: true)),
        ];
        return Task.FromResult(list);
    }

    private LibraryItemState StateOf(CollectionItem item, LibrarySource source) => _operations.StateOf(item, source, _time);

    private LibraryItemTextInput TextInput(LibraryItemState state) =>
        new(state.Item, state.Source, state.Availability, state.EffectiveItem)
        {
            Download = Downloads.StatusOf(state.Item),
            LoadState = _operations.Loader.StateOf,
            CollectionName = Library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == state.Source.Id))?.Definition.Name,
            LocalRun = state.LocalRun,
            TimeZone = _time.LocalTimeZone,
        };

    private LibraryItemInfo Info(LibraryItemState state) =>
        LibraryToolResults.Item(state, LibraryItemText.Tags(TextInput(state)).Select(tag => tag.Text));

    // ── add ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<AddSourceResult>> AddSourceAsync(AddSourceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var given = new[] { request.KnownSourceId, request.Path, request.Url }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (given != 1)
            return Fail(new InvalidArgument("knownSourceId", "supply exactly one of knownSourceId, path and url"));

        LibrarySourceDraft? draft;
        LibrarySourceKind kind;
        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            var path = Path.GetFullPath(request.Path.Trim());
            if (!File.Exists(path) && !Directory.Exists(path))
                return Fail(new InvalidArgument("path", $"'{path}' does not exist"));
            LibrarySourceKind? pathKind = request.Kind?.Trim().ToLowerInvariant() switch
            {
                null or "" => LibrarySourceKinds.Classify(path),
                "folder" => LibrarySourceKind.Folder,
                "exchange_set" or "exchangeset" => LibrarySourceKind.ExchangeSet,
                "manifest" => LibrarySourceKind.LocalManifest,
                "s128" or "s-128" => LibrarySourceKind.S128Catalogue,
                _ => null,
            };
            if (pathKind is not { } local)
                return Fail(new InvalidArgument("kind", "expected folder, exchange_set, manifest or s128"));
            kind = local;
            draft = LibrarySourceDraft.ForPath(local, path);
        }
        else
        {
            KnownCatalogueSource? known;
            if (!string.IsNullOrWhiteSpace(request.KnownSourceId))
            {
                var id = request.KnownSourceId.Trim();
                known = KnownCatalogueSources.Find(id)
                    ?? _userCatalogues().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                    return Fail(new InvalidArgument("knownSourceId", $"no known source '{id}'; call list_known_sources"));
            }
            else
            {
                if (!Uri.TryCreate(request.Url!.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    return Fail(new InvalidArgument("url", "expected an http or https URL"));
                if (_probe is null)
                    return Fail(new LibraryChangeRejected("this host cannot read online catalogues"));
                CatalogueProbe probe;
                try
                {
                    probe = await _probe(uri, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    return Fail(new LibraryChangeRejected($"the URL could not be read ({ex.Message})"));
                }
                if (probe.Format is not { } format)
                    return Fail(new LibraryChangeRejected("the URL is not a catalogue or feed the Library can read"));
                known = KnownCatalogueSources.FromUrl(uri, format, probe.Title);
            }

            kind = LibrarySourceKinds.Of(known.Format);
            draft = LibrarySourceDraft.ForCatalogue(known, _readers, _time);
        }

        if (draft is null)
            return Fail(new LibraryChangeRejected($"this host cannot read {kind} catalogues"));

        // Online catalogues and collection manifests are read for their choices.
        if (draft.Scope is not null && await draft.LoadAsync(ct).ConfigureAwait(false) is { } error)
            return Fail(new LibraryChangeRejected($"the catalogue could not be loaded: {error}"));

        var collections = Library.Collections.Where(c => !c.IsSession).ToArray();
        if (request.CollectionId is { } collectionId && !collections.Any(c => c.Id == collectionId))
            return Fail(new InvalidArgument("collectionId", "no such collection; call list_library_sources"));
        if (request.InMapView)
        {
            if (draft.Scope is not SecomScope secom)
                return Fail(new InvalidArgument("inMapView", "only a SECOM service can be narrowed to the map view"));
            if (!secom.CanScopeToMapView)
                return Fail(new LibraryChangeRejected("there is no map view to narrow to"));
            secom.InMapView = true;
            if (await draft.LoadAsync(ct).ConfigureAwait(false) is { } areaError)
                return Fail(new LibraryChangeRejected($"the service could not be read for the map view: {areaError}"));
        }

        // Choices are applied for a preview too, so it shows the resulting scope.
        if (request.Choices is { Count: > 0 } choices)
        {
            var options = draft.Groups.SelectMany(g => g.Options).ToArray();
            var unknown = new List<string>();
            foreach (var choice in choices)
            {
                if (options.FirstOrDefault(o => Matches(o.Value, o.Label, choice)) is { } option)
                    option.IsSelected = true;
                else
                    unknown.Add(choice);
            }
            if (unknown.Count > 0)
                return Fail(new InvalidArgument("choices", $"unknown choice(s): {string.Join(", ", unknown)}; preview to list them"));
            draft.IncludeAll = request.IncludeAll ?? false;
        }
        else if (request.IncludeAll is { } includeAll)
        {
            draft.IncludeAll = includeAll;
        }

        if (!string.IsNullOrWhiteSpace(request.Shape))
        {
            var forecast = draft.Scope as S100ForecastScope;
            if (forecast?.Shapes.FirstOrDefault(o => Matches(o.Value, o.Label, request.Shape)) is not { } shape)
                return Fail(new InvalidArgument("shape", "only a forecast feed has shapes; expected tiles or regional"));
            forecast.SelectedShape = shape;
        }
        if (!string.IsNullOrWhiteSpace(request.Resolution))
        {
            var resolution = (draft.Scope as S100CatalogueScope)?.Resolutions.FirstOrDefault(o => Matches(o.Value, o.Label, request.Resolution));
            if (resolution is null)
                return Fail(new InvalidArgument("resolution", "not a resolution this catalogue offers; preview to list them"));
            ((S100CatalogueScope)draft.Scope!).SelectedResolution = resolution;
        }
        if (request.Sync is { } sync)
        {
            if (!draft.CanKeepDownloaded)
                return Fail(new InvalidArgument("sync", "only an online source can be kept downloaded"));
            draft.KeepDownloaded = sync;
        }
        draft.ShowOnMap = request.ShowOnMap;

        if (request.Preview)
            return LibraryEditOutcome<AddSourceResult>.Ok(Describe(draft, collections, request.CollectionId, added: false, null, null));
        if (!draft.CanBuild)
            return Fail(new LibraryChangeRejected("this source cannot be added as it stands"));

        var (collection, source) = draft.AddTo(Library, request.CollectionId, request.CollectionName);
        return LibraryEditOutcome<AddSourceResult>.Ok(Describe(draft, collections, request.CollectionId, added: true, collection, source));

        static LibraryEditOutcome<AddSourceResult> Fail(ToolError error) => LibraryEditOutcome<AddSourceResult>.Fail(error);
    }

    private static AddSourceResult Describe(
        LibrarySourceDraft draft, IReadOnlyList<LibraryCollection> collections, Guid? target, bool added, Guid? collectionId, Guid? sourceId) => new(
        added,
        draft.Kind.ToString(),
        draft.Title,
        draft.CatalogueDetail,
        draft.IsCatalogueStale,
        draft.ForecastEndedNote,
        draft.ScopeSummary,
        [.. draft.Groups.Select(group => new AddChoiceGroup(
            group.Title,
            [.. group.Options.Select(o => new AddChoiceOption(o.Value, o.Label, string.IsNullOrEmpty(o.Detail) ? null : o.Detail, o.IsSelected))]))],
        draft.Scope is S100ForecastScope forecast
            ? [.. forecast.Shapes.Select(o => new AddChoiceOption(o.Value ?? string.Empty, o.Label, null, o == forecast.SelectedShape))]
            : [],
        draft.Scope is S100CatalogueScope { HasResolutions: true } catalogue
            ? [.. catalogue.Resolutions.Select(o => new AddChoiceOption(o.Value ?? string.Empty, o.Label, null, o == catalogue.SelectedResolution))]
            : [],
        [.. collections.Select(c => new AddChoiceOption(c.Id.ToString(), c.Definition.Name, null, target == c.Id))],
        collectionId,
        sourceId,
        draft.CanKeepDownloaded ? draft.KeepDownloaded : null,
        draft.KeepDownloadedHint);

    private static bool Matches(string? value, string label, string wanted)
    {
        var w = wanted.Trim();
        return string.Equals(value, w, StringComparison.OrdinalIgnoreCase)
            || string.Equals(label, w, StringComparison.OrdinalIgnoreCase);
    }

    // ── refresh ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<RefreshResult>> RefreshAsync(Guid? id, TimeSpan wait, CancellationToken ct = default)
    {
        Guid? collectionId = null;
        Guid? sourceId = null;
        if (id is { } wanted)
        {
            var collection = Library.Collections.FirstOrDefault(c => c.Id == wanted && !c.IsSession)
                ?? Library.Collections.FirstOrDefault(c => !c.IsSession && c.Sources.Any(s => s.Id == wanted));
            if (collection is null)
                return LibraryEditOutcome<RefreshResult>.Fail(new InvalidArgument("id", "no such collection or source; call list_library_sources"));
            collectionId = collection.Id;
            sourceId = collection.Id == wanted ? null : wanted;
        }

        var before = States(id);
        Library.Refresh(collectionId, sourceId);

        var waited = wait > TimeSpan.Zero;
        var timedOut = waited && !(await AwaitIdleAsync(wait, ct).ConfigureAwait(false)).Idle;
        var after = States(id);
        var changed = after
            .Where(p => before.TryGetValue(p.Key, out var old) && old != p.Value)
            .GroupBy(p => p.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return LibraryEditOutcome<RefreshResult>.Ok(new RefreshResult(
            waited,
            timedOut,
            after.Count,
            after.Keys.Count(k => !before.ContainsKey(k)),
            before.Keys.Count(k => !after.ContainsKey(k)),
            changed,
            after.Values.GroupBy(v => v, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal)));
    }

    /// <summary>Item id → state for everything in scope (the whole Library when <paramref name="id"/> is null).</summary>
    private Dictionary<string, string> States(Guid? id) =>
        (Find(new LibraryItemPageQuery(id, null, null, null, null, null, 0, 1)) ?? [])
            .GroupBy(state => state.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => LibraryAvailabilityNames.Of(g.First().Availability), StringComparer.Ordinal);

    // ── actions ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<LibraryActionResult>> ActAsync(LibraryActionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!LibraryActionTool.Actions.Contains(action))
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("action", $"expected one of {string.Join(", ", LibraryActionTool.Actions)}"));
        var hasIds = request.ItemIds is { Count: > 0 };
        if (hasIds == (request.Filter is not null))
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument(
                "itemIds", "pass itemIds or filters (sourceId, states, spec, text, box or point), not both and not neither"));
        }

        List<LibraryItemState> states;
        if (request.ItemIds is { Count: > 0 } ids)
        {
            states = [];
            var unknown = new List<string>();
            foreach (var id in ids)
            {
                if (LibraryQuery.FindById(Library.Collections, id.Trim()) is var (item, source))
                    states.Add(StateOf(item, source));
                else
                    unknown.Add(id);
            }
            if (unknown.Count > 0)
                return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("itemIds", $"unknown item id(s): {string.Join(", ", unknown)}"));
        }
        else if (Find(request.Filter!) is { } found)
        {
            states = found;
        }
        else
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("sourceId", "no such collection or source; call list_library_sources"));
        }

        Func<LibraryItemState, bool> eligible = action switch
        {
            "load" or "load_as_you_pan" => CanLoad,
            "download" or "download_only" => CanDownload,
            "update" => state => CanDownload(state) && state.Availability == LibraryAvailability.Outdated,
            _ => state => Downloads.StatusOf(state.Item) is { State: LibraryDownloadItemState.Queued or LibraryDownloadItemState.Running },
        };
        var chosen = states.Where(eligible).ToList();
        var skipped = states.Where(state => !eligible(state))
            .GroupBy(state => LibraryAvailabilityNames.Of(state.Availability), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var downloads = action is "download" or "download_only" or "update";
        var sizes = chosen.Select(state => (state.Item.Location as RemoteItemLocation)?.SizeBytes).ToArray();
        var bytes = downloads ? sizes.Sum(size => size ?? 0) : 0;
        var unknownSizes = downloads ? sizes.Count(size => size is null) : 0;
        var names = chosen.Take(LibraryActionTool.ListedNames).Select(state => state.Item.Name).ToArray();

        LibraryActionResult Result(int? opened, bool started) =>
            new(action, request.DryRun, chosen.Count, skipped, bytes, unknownSizes, names, opened, started);

        if (request.DryRun)
            return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, false));
        if (chosen.Count == 0)
            return LibraryEditOutcome<LibraryActionResult>.Ok(Result(action.StartsWith("load", StringComparison.Ordinal) ? 0 : null, false));
        if (downloads && request.MaxBytes is { } max && bytes > max)
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new LibraryChangeRejected(
                $"the download is {LibraryTextFormat.Bytes(bytes)}, more than maxBytes ({LibraryTextFormat.Bytes(max)})"));
        }

        switch (action)
        {
            case "load" or "load_as_you_pan":
                var loaded = await _operations.LoadAsync([.. chosen.Select(s => s.Item)], defer: action == "load_as_you_pan", ct).ConfigureAwait(false);
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(loaded.Opened, false));
            case "cancel":
                foreach (var state in chosen)
                    Downloads.Cancel(state.Item);
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, false));
            default:
                // Downloads run on in the background; await_library_idle waits for them and
                // for what follows (re-indexing, opening), as LibraryOperations tracks it (#790).
                StartDownload(chosen, load: action == "download");
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, true));
        }
    }

    /// <summary>True when the item can be opened from disk (local, and not already loaded).</summary>
    private static bool CanLoad(LibraryItemState state) =>
        state.Availability is LibraryAvailability.Local or LibraryAvailability.Deferred or LibraryAvailability.Outdated or LibraryAvailability.Expired;

    /// <summary>True when the item can be downloaded (online, or a newer edition is available).</summary>
    private bool CanDownload(LibraryItemState state) =>
        Downloads.CanDownload(state.Item)
        && state.Availability is LibraryAvailability.Online or LibraryAvailability.Outdated;

    private void StartDownload(IReadOnlyList<LibraryItemState> chosen, bool load)
    {
        foreach (var group in chosen.GroupBy(state => state.Source.Id))
            _downloading[group.Key] = [.. group.Select(state => state.Item)];

        // Tracked from this call (LibraryOperations begins its scope before the first await).
        var work = _operations.DownloadAsync([.. chosen.Select(state => (state.Item, state.Source))], load);
        _ = work.ContinueWith(
            _ =>
            {
                foreach (var group in chosen.GroupBy(state => state.Source.Id))
                    _downloading.TryRemove(group.Key, out var _);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public Task CancelAllDownloadsAsync(CancellationToken ct = default)
    {
        Downloads.CancelAll();
        return Task.CompletedTask;
    }

    // ── remove ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<LibraryEditOutcome<RemoveSourceResult>> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Library.Collections.FirstOrDefault(c => c.Id == id) is { } collection)
        {
            if (collection.IsSession)
                return Task.FromResult(LibraryEditOutcome<RemoveSourceResult>.Fail(new LibraryChangeRejected("the session collection cannot be removed")));
            if (Library.RemoveCollection(id))
                return Task.FromResult(LibraryEditOutcome<RemoveSourceResult>.Ok(new RemoveSourceResult(id, collection.Definition.Name, true, collection.ItemCount)));
        }
        else if (Library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == id)) is { } owner)
        {
            var source = owner.Sources.First(s => s.Id == id);
            if (owner.IsSession)
                return Task.FromResult(LibraryEditOutcome<RemoveSourceResult>.Fail(new LibraryChangeRejected("a session catalogue cannot be removed this way")));
            if (Library.RemoveSource(owner.Id, id))
            {
                return Task.FromResult(LibraryEditOutcome<RemoveSourceResult>.Ok(new RemoveSourceResult(
                    id, source.Definition.DisplayName ?? owner.Definition.Name, false, source.Index?.Items.Count ?? 0)));
            }
        }

        return Task.FromResult(LibraryEditOutcome<RemoveSourceResult>.Fail(
            new InvalidArgument("id", "no such collection or source; call list_library_sources")));
    }

    // ── options ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<LibraryEditOutcome<SetSourceOptionsResult>> SetOptionsAsync(Guid id, bool? sync, bool? showOnMap, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (sync is null && showOnMap is null)
            return Task.FromResult(LibraryEditOutcome<SetSourceOptionsResult>.Fail(new InvalidArgument("sync", "supply sync, showOnMap or both")));

        var collection = Library.Collections.FirstOrDefault(c => c.Id == id)
            ?? Library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == id));
        if (collection is null)
            return Task.FromResult(LibraryEditOutcome<SetSourceOptionsResult>.Fail(new InvalidArgument("id", "no such collection or source; call list_library_sources")));
        if (collection.IsSession)
            return Task.FromResult(LibraryEditOutcome<SetSourceOptionsResult>.Fail(new LibraryChangeRejected("a session catalogue has no options; keep it in the Library first")));

        var targets = collection.Id == id ? collection.Sources : [.. collection.Sources.Where(s => s.Id == id)];
        if (sync is not null && targets is [var only] && collection.Id != id && !LibrarySync.CanSyncByDefault(only.Definition))
            return Task.FromResult(LibraryEditOutcome<SetSourceOptionsResult>.Fail(new InvalidArgument("sync", "only an online source can be kept downloaded")));

        var results = new List<SourceOptionsInfo>();
        foreach (var source in targets)
        {
            var definition = source.Definition;
            var canSync = LibrarySync.CanSyncByDefault(definition);
            var updated = definition with
            {
                Sync = canSync && sync is { } s ? s : definition.Sync,
                ShowOnMap = showOnMap ?? definition.ShowOnMap,
            };
            var changed = updated != definition && Library.UpdateSource(collection.Id, updated);
            results.Add(new SourceOptionsInfo(
                source.Id, LibraryNodeText.SourceName(source, collection), updated.Sync, updated.ShowOnMap, canSync, changed));
        }

        return Task.FromResult(LibraryEditOutcome<SetSourceOptionsResult>.Ok(new SetSourceOptionsResult(results)));
    }

    // ── idle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryIdleResult> AwaitIdleAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var wait = await _operations.AwaitIdleAsync(timeout, ct).ConfigureAwait(false);
        var activity = wait.Activity;
        return new LibraryIdleResult(
            activity.IsIdle,
            wait.TimedOut,
            (long)wait.Waited.TotalMilliseconds,
            activity.Indexing,
            activity.PendingDatasets,
            activity.Downloads is { } p ? new LibraryDownloadInfo(p.Completed, p.Failed, p.Total, p.BytesDone, p.BytesTotal) : null);
    }
}
