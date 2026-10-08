using System.Diagnostics;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's <see cref="ILibraryEditor"/>: each change goes through the code
/// path the UI uses (the Add-to-Library dialog's view model, the panel's download
/// and load entry points, and the Library service).
/// </summary>
internal sealed class ViewerLibraryEditor : ILibraryEditor
{
    /// <summary>The actions library_action accepts.</summary>
    internal static readonly IReadOnlyList<string> Actions = ["load", "load_as_you_pan", "download", "download_only", "update", "cancel"];

    private const int ListedNames = 50;

    private readonly LibraryPanelViewModel _panel;
    private readonly CollectionLibrary _library;
    private readonly ViewerLibraryController _reader;
    private readonly Func<AddToLibraryDialogViewModel> _dialogs;
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>>? _probe;
    private readonly Func<IReadOnlyList<KnownCatalogueSource>> _userCatalogues;
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly LibraryActivityTracker _activity;

    public ViewerLibraryEditor(
        LibraryPanelViewModel panel,
        CollectionLibrary library,
        ViewerLibraryController reader,
        Func<AddToLibraryDialogViewModel> dialogs,
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null,
        Func<IReadOnlyList<KnownCatalogueSource>>? userCatalogues = null,
        Func<Func<Task>, Task>? dispatch = null,
        LibraryActivityTracker? activity = null)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(dialogs);
        _panel = panel;
        _library = library;
        _reader = reader;
        _dialogs = dialogs;
        _probe = probe;
        _userCatalogues = userCatalogues ?? (() => []);
        _dispatch = dispatch ?? (work => Dispatcher.UIThread.InvokeAsync(work));
        _activity = activity ?? new LibraryActivityTracker();
    }

    // ── add ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<AddSourceResult>> AddSourceAsync(AddSourceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var given = new[] { request.KnownSourceId, request.Path, request.Url }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (given != 1)
            return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("knownSourceId", "supply exactly one of knownSourceId, path and url"));

        KnownCatalogueSource? known = null;
        if (!string.IsNullOrWhiteSpace(request.KnownSourceId))
        {
            var id = request.KnownSourceId.Trim();
            known = KnownCatalogueSources.Find(id)
                ?? _userCatalogues().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (known is null)
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("knownSourceId", $"no known source '{id}'; call list_known_sources"));
        }
        else if (!string.IsNullOrWhiteSpace(request.Url))
        {
            if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("url", "expected an http or https URL"));
            if (_probe is null)
                return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected("this viewer cannot read online catalogues"));
            CatalogueProbe probe;
            try
            {
                probe = await _probe(uri, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected($"the URL could not be read ({ex.Message})"));
            }
            if (probe.Format is not { } format)
                return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected("the URL is not a catalogue or feed the Library can read"));
            known = KnownCatalogueSources.FromUrl(uri, format, probe.Title);
        }

        AddToLibraryKind? pathKind = null;
        string? path = null;
        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            path = Path.GetFullPath(request.Path.Trim());
            if (!File.Exists(path) && !Directory.Exists(path))
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("path", $"'{path}' does not exist"));
            pathKind = request.Kind?.Trim().ToLowerInvariant() switch
            {
                null or "" => LibraryImportCoordinator.Classify(path),
                "folder" => AddToLibraryKind.Folder,
                "exchange_set" or "exchangeset" => AddToLibraryKind.ExchangeSet,
                "manifest" => AddToLibraryKind.LocalManifest,
                "s128" or "s-128" => AddToLibraryKind.S128Catalogue,
                _ => null,
            };
            if (pathKind is null)
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("kind", "expected folder, exchange_set, manifest or s128"));
        }

        LibraryEditOutcome<AddSourceResult> outcome = default;
        await _dispatch(async () => outcome = await AddOnUiThreadAsync(request, known, pathKind, path, ct).ConfigureAwait(true)).ConfigureAwait(false);
        return outcome;
    }

    private async Task<LibraryEditOutcome<AddSourceResult>> AddOnUiThreadAsync(
        AddSourceRequest request, KnownCatalogueSource? known, AddToLibraryKind? pathKind, string? path, CancellationToken ct)
    {
        var dialog = _dialogs();
        if (known is not null)
            dialog.Initialize(known, request.CollectionId);
        else
            dialog.Initialize(pathKind!.Value, path, request.CollectionId);

        if (dialog.Kind is not (AddToLibraryKind.Folder or AddToLibraryKind.ExchangeSet or AddToLibraryKind.S128Catalogue))
        {
            await dialog.LoadCatalogAsync(ct).ConfigureAwait(true);
            if (dialog.LoadError is { } error)
                return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected($"the catalogue could not be loaded: {error}"));
        }

        if (request.CollectionId is { } collectionId && !dialog.ExistingCollections.Any(c => c.Id == collectionId))
            return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("collectionId", "no such collection; call list_library_sources"));

        if (request.InMapView)
        {
            if (!dialog.IsSecom)
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("inMapView", "only a SECOM service can be narrowed to the map view"));
            if (!dialog.CanScopeSecomToMapView)
                return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected("there is no map view to narrow to"));
            await dialog.SetSecomInMapViewAsync(true, ct).ConfigureAwait(true);
            if (dialog.LoadError is { } areaError)
                return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected($"the service could not be read for the map view: {areaError}"));
        }

        // Choices, shape and resolution are applied for a preview too, so it
        // shows the resulting scope.
        var options = dialog.FacetGroups.SelectMany(g => g.AllOptions).ToArray();
        if (request.Choices is { Count: > 0 } choices)
        {
            var unknown = new List<string>();
            foreach (var choice in choices)
            {
                var option = options.FirstOrDefault(o => Matches(o.Value, o.Label, choice));
                if (option is null)
                    unknown.Add(choice);
                else
                    option.IsSelected = true;
            }
            if (unknown.Count > 0)
            {
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument(
                    "choices", $"unknown choice(s): {string.Join(", ", unknown)}; preview to list them"));
            }
            dialog.IncludeAll = request.IncludeAll ?? false;
        }
        else if (request.IncludeAll is { } includeAll)
        {
            dialog.IncludeAll = includeAll;
        }

        if (!string.IsNullOrWhiteSpace(request.Shape))
        {
            var shape = dialog.ForecastShapes.FirstOrDefault(o => Matches(o.Value, o.Label, request.Shape));
            if (!dialog.IsS100Forecast || shape is null)
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("shape", "only a forecast feed has shapes; expected tiles or regional"));
            dialog.SelectedForecastShape = shape;
        }

        if (!string.IsNullOrWhiteSpace(request.Resolution))
        {
            var resolution = dialog.Resolutions.FirstOrDefault(o => Matches(o.Value, o.Label, request.Resolution));
            if (resolution is null)
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("resolution", "not a resolution this catalogue offers; preview to list them"));
            dialog.SelectedResolution = resolution;
        }

        if (request.Sync is { } sync)
        {
            if (dialog.IsSecom)
                dialog.SecomSync = sync;
            else if (dialog.CanKeepDownloaded)
                dialog.KeepDownloaded = sync;
            else
                return LibraryEditOutcome<AddSourceResult>.Fail(new InvalidArgument("sync", "only an online source can be kept downloaded"));
        }

        dialog.ShowOnMap = request.ShowOnMap;

        if (request.CollectionId is { } target)
        {
            dialog.CreateNew = false;
            dialog.SelectedCollection = dialog.ExistingCollections.First(c => c.Id == target);
        }
        else
        {
            dialog.CreateNew = true;
            if (!string.IsNullOrWhiteSpace(request.CollectionName))
                dialog.NewCollectionName = request.CollectionName.Trim();
        }

        if (request.Preview)
            return LibraryEditOutcome<AddSourceResult>.Ok(Describe(dialog, added: false, null, null));

        if (!dialog.ConfirmCommand.CanExecute(null))
        {
            return LibraryEditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected(
                dialog.IncludeAll ? "the dialog cannot add this source as it stands" : "nothing is selected; pass choices or includeAll"));
        }

        var before = _library.Collections.SelectMany(c => c.Sources).Select(s => s.Id).ToHashSet();
        dialog.ConfirmCommand.Execute(null);
        var added = _library.Collections
            .SelectMany(c => c.Sources.Select(s => (Collection: c, Source: s)))
            .FirstOrDefault(p => !before.Contains(p.Source.Id));
        return LibraryEditOutcome<AddSourceResult>.Ok(Describe(dialog, added: true, added.Collection?.Id, added.Source?.Id));
    }

    private static AddSourceResult Describe(AddToLibraryDialogViewModel dialog, bool added, Guid? collectionId, Guid? sourceId) => new(
        added,
        dialog.Kind.ToString(),
        dialog.Title,
        dialog.IsLoaded ? dialog.CatalogueDetail : null,
        dialog.IsCatalogueStale,
        dialog.ForecastEndedNote,
        dialog.ScopeSummary,
        [.. dialog.FacetGroups.Select(group => new AddChoiceGroup(
            group.Title,
            [.. group.AllOptions.Select(o => new AddChoiceOption(o.Value, o.Label, string.IsNullOrEmpty(o.Detail) ? null : o.Detail, o.IsSelected))]))],
        dialog.IsS100Forecast
            ? [.. dialog.ForecastShapes.Select(o => new AddChoiceOption(o.Value ?? string.Empty, o.Label, null, ReferenceEquals(o, dialog.SelectedForecastShape)))]
            : [],
        dialog.HasResolutions
            ? [.. dialog.Resolutions.Select(o => new AddChoiceOption(o.Value ?? string.Empty, o.Label, null, ReferenceEquals(o, dialog.SelectedResolution)))]
            : [],
        [.. dialog.ExistingCollections.Select(c => new AddChoiceOption(c.Id.ToString(), c.Definition.Name, null, !dialog.CreateNew && dialog.SelectedCollection?.Id == c.Id))],
        collectionId,
        sourceId,
        dialog.IsSecom ? dialog.SecomSync : dialog.CanKeepDownloaded ? dialog.KeepDownloaded : null,
        dialog.IsSecom ? dialog.SecomSyncHint : null);

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
        Dictionary<string, string>? before = null;
        Guid? collectionId = null;
        Guid? sourceId = null;
        await _dispatch(() =>
        {
            if (id is { } wanted)
            {
                var collection = _library.Collections.FirstOrDefault(c => c.Id == wanted && !c.IsSession)
                    ?? _library.Collections.FirstOrDefault(c => !c.IsSession && c.Sources.Any(s => s.Id == wanted));
                if (collection is null)
                    return Task.CompletedTask;
                collectionId = collection.Id;
                sourceId = collection.Id == wanted ? null : wanted;
            }
            before = States(id);
            // As the panel's Refresh does for the selected node (or Refresh all).
            _library.Refresh(collectionId, sourceId);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        if (before is null)
            return LibraryEditOutcome<RefreshResult>.Fail(new InvalidArgument("id", "no such collection or source; call list_library_sources"));

        var waited = wait > TimeSpan.Zero;
        var timedOut = false;
        if (waited)
            timedOut = !(await AwaitIdleAsync(wait, ct).ConfigureAwait(false)).Idle;

        Dictionary<string, string> after = [];
        await _dispatch(() =>
        {
            after = States(id);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

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
        (_reader.FindRows(new LibraryItemPageQuery(id, null, null, null, null, null, 0, 1)) ?? [])
            .GroupBy(row => $"{row.Source.Id}:{row.Item.Key}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => LibraryAvailabilityNames.Of(g.First().Availability), StringComparer.Ordinal);

    // ── actions ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<LibraryActionResult>> ActAsync(LibraryActionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Actions.Contains(action))
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("action", $"expected one of {string.Join(", ", Actions)}"));
        var hasIds = request.ItemIds is { Count: > 0 };
        if (hasIds == (request.Filter is not null))
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument(
                "itemIds", "pass itemIds or filters (sourceId, states, spec, text, box or point), not both and not neither"));
        }

        LibraryEditOutcome<LibraryActionResult> outcome = default;
        await _dispatch(async () => outcome = await ActOnUiThreadAsync(action, request, ct).ConfigureAwait(true)).ConfigureAwait(false);
        return outcome;
    }

    /// <inheritdoc />
    public Task CancelAllDownloadsAsync(CancellationToken ct = default) => _dispatch(() =>
    {
        _panel.Downloader.CancelAll();
        return Task.CompletedTask;
    });

    private async Task<LibraryEditOutcome<LibraryActionResult>> ActOnUiThreadAsync(string action, LibraryActionRequest request, CancellationToken ct)
    {
        List<LibraryItemViewModel> rows;
        if (request.ItemIds is { Count: > 0 } ids)
        {
            rows = [];
            var unknown = new List<string>();
            foreach (var id in ids)
            {
                if (Row(id) is { } row)
                    rows.Add(row);
                else
                    unknown.Add(id);
            }
            if (unknown.Count > 0)
                return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("itemIds", $"unknown item id(s): {string.Join(", ", unknown)}"));
        }
        else if (_reader.FindRows(request.Filter!) is { } found)
        {
            rows = found;
        }
        else
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("sourceId", "no such collection or source; call list_library_sources"));
        }

        Func<LibraryItemViewModel, bool> eligible = action switch
        {
            "load" or "load_as_you_pan" => row => row.CanLoad,
            "download" or "download_only" => row => row.CanDownload,
            "update" => row => row.CanDownload && row.Availability == LibraryAvailability.Outdated,
            _ => row => _panel.Downloader.StatusOf(row.Item) is { State: LibraryDownloadItemState.Queued or LibraryDownloadItemState.Running },
        };
        var chosen = rows.Where(eligible).ToList();
        var skipped = rows.Where(row => !eligible(row))
            .GroupBy(row => LibraryAvailabilityNames.Of(row.Availability), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var downloads = action is "download" or "download_only" or "update";
        var sizes = chosen.Select(row => (row.Item.Location as RemoteItemLocation)?.SizeBytes).ToArray();
        var bytes = downloads ? sizes.Sum(size => size ?? 0) : 0;
        var unknownSizes = downloads ? sizes.Count(size => size is null) : 0;
        var names = chosen.Take(ListedNames).Select(row => row.Item.Name).ToArray();

        LibraryActionResult Result(int? opened, bool started) =>
            new(action, request.DryRun, chosen.Count, skipped, bytes, unknownSizes, names, opened, started);

        if (request.DryRun)
            return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, false));
        if (chosen.Count == 0)
            return LibraryEditOutcome<LibraryActionResult>.Ok(Result(action.StartsWith("load", StringComparison.Ordinal) ? 0 : null, false));
        if (downloads && request.MaxBytes is { } max && bytes > max)
        {
            return LibraryEditOutcome<LibraryActionResult>.Fail(new LibraryChangeRejected(
                $"the download is {LibraryItemViewModel.FormatBytes(bytes)}, more than maxBytes ({LibraryItemViewModel.FormatBytes(max)})"));
        }

        switch (action)
        {
            case "load" or "load_as_you_pan":
                // Tracked so a concurrent await_library_idle waits for the opens (#790).
                using (_activity.Begin(action == "load" ? chosen.Count : 0))
                {
                    var loaded = await _panel.LoadRowsAsync(chosen, defer: action == "load_as_you_pan").ConfigureAwait(true);
                    return LibraryEditOutcome<LibraryActionResult>.Ok(Result(loaded.Opened, false));
                }
            case "cancel":
                foreach (var row in chosen)
                    row.CancelDownloadCommand.Execute(null);
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, false));
            default:
                // Downloads run on in the background, as from the panel; await_library_idle waits for
                // them and for what follows (re-indexing, opening) until RunDownloadAsync ends (#790).
                var load = action == "download";
                _ = RunDownloadAsync(chosen, load, _activity.Begin(load ? chosen.Count : 0));
                return LibraryEditOutcome<LibraryActionResult>.Ok(Result(null, true));
        }
    }

    private async Task RunDownloadAsync(IReadOnlyList<LibraryItemViewModel> rows, bool load, IDisposable activity)
    {
        using var scope = activity;
        try
        {
            await _panel.DownloadRowsAsync(rows, load).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Failures show on the rows (Failed · Retry) and in await_library_idle's counts.
            Debug.WriteLine($"library_action download failed: {ex}");
        }
    }

    private LibraryItemViewModel? Row(string itemId)
    {
        if (!LibraryItemState.TryParseId(itemId.Trim(), out var sourceId, out var key))
            return null;
        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        var item = source?.Index?.Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal));
        return source is not null && item is not null ? _panel.CreateItem(item, source) : null;
    }

    // ── remove ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<RemoveSourceResult>> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        LibraryEditOutcome<RemoveSourceResult> outcome = LibraryEditOutcome<RemoveSourceResult>.Fail(
            new InvalidArgument("id", "no such collection or source; call list_library_sources"));
        await _dispatch(() =>
        {
            if (_library.Collections.FirstOrDefault(c => c.Id == id) is { } collection)
            {
                if (collection.IsSession)
                {
                    outcome = LibraryEditOutcome<RemoveSourceResult>.Fail(new LibraryChangeRejected("the session collection cannot be removed"));
                }
                else if (_library.RemoveCollection(id))
                {
                    outcome = LibraryEditOutcome<RemoveSourceResult>.Ok(new RemoveSourceResult(id, collection.Definition.Name, true, collection.ItemCount));
                }
            }
            else if (_library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == id)) is { } owner)
            {
                var source = owner.Sources.First(s => s.Id == id);
                if (owner.IsSession)
                {
                    outcome = LibraryEditOutcome<RemoveSourceResult>.Fail(new LibraryChangeRejected("a session catalogue cannot be removed this way"));
                }
                else if (_library.RemoveSource(owner.Id, id))
                {
                    outcome = LibraryEditOutcome<RemoveSourceResult>.Ok(new RemoveSourceResult(
                        id, source.Definition.DisplayName ?? owner.Definition.Name, false, source.Index?.Items.Count ?? 0));
                }
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return outcome;
    }

    // ── idle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryIdleResult> AwaitIdleAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            LibraryActivitySnapshot activity = null!;
            await _dispatch(() =>
            {
                activity = LibraryActivitySnapshot.Capture(_library, _panel.Downloader.Progress, _activity);
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            var idle = activity.IsIdle;
            var timedOut = !idle && clock.Elapsed >= timeout;
            if (idle || timedOut)
            {
                return new LibraryIdleResult(
                    idle,
                    timedOut,
                    clock.ElapsedMilliseconds,
                    activity.Indexing,
                    activity.PendingDatasets,
                    activity.Downloads is { } p ? new LibraryDownloadInfo(p.Completed, p.Failed, p.Total, p.BytesDone, p.BytesTotal) : null);
            }
            await Task.Delay(LibraryOperations.IdlePollInterval, ct).ConfigureAwait(false);
        }
    }
}
