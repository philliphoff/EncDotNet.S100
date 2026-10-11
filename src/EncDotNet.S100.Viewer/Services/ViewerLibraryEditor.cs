using System.Diagnostics;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's <see cref="ILibraryEditor"/>: each change goes through the code
/// path the UI uses (the core's source drafts the Add-to-Library dialog is built
/// on, the panel's download and load entry points, and the Library service).
/// </summary>
internal sealed class ViewerLibraryEditor : ILibraryEditor
{
    private readonly LibraryPanelViewModel _panel;
    private readonly CollectionLibrary _library;
    private readonly ViewerLibraryController _reader;
    private readonly LibrarySourceAdder _adder;
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly LibraryActivityTracker _activity;

    public ViewerLibraryEditor(
        LibraryPanelViewModel panel,
        CollectionLibrary library,
        ViewerLibraryController reader,
        LibrarySourceAdder adder,
        Func<Func<Task>, Task>? dispatch = null,
        LibraryActivityTracker? activity = null)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(adder);
        _panel = panel;
        _library = library;
        _reader = reader;
        _adder = adder;
        _dispatch = dispatch ?? (work => Dispatcher.UIThread.InvokeAsync(work));
        _activity = activity ?? new LibraryActivityTracker();
    }

    // ── add ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<LibraryEditOutcome<AddSourceResult>> AddSourceAsync(AddSourceRequest request, CancellationToken ct = default) =>
        _adder.AddAsync(request, _library, _dispatch, ct);

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
        if (!LibraryActionTool.Actions.Contains(action))
            return LibraryEditOutcome<LibraryActionResult>.Fail(new InvalidArgument("action", $"expected one of {string.Join(", ", LibraryActionTool.Actions)}"));
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
        var names = chosen.Take(LibraryActionTool.ListedNames).Select(row => row.Item.Name).ToArray();

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

    // ── options ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryEditOutcome<SetSourceOptionsResult>> SetOptionsAsync(
        Guid id, bool? sync, bool? showOnMap, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (sync is null && showOnMap is null)
            return LibraryEditOutcome<SetSourceOptionsResult>.Fail(new InvalidArgument("sync", "supply sync, showOnMap or both"));

        LibraryEditOutcome<SetSourceOptionsResult> outcome = LibraryEditOutcome<SetSourceOptionsResult>.Fail(
            new InvalidArgument("id", "no such collection or source; call list_library_sources"));
        await _dispatch(() =>
        {
            var collection = _library.Collections.FirstOrDefault(c => c.Id == id)
                ?? _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == id));
            if (collection is null)
                return Task.CompletedTask;
            if (collection.IsSession)
            {
                outcome = LibraryEditOutcome<SetSourceOptionsResult>.Fail(new LibraryChangeRejected("a session catalogue has no options; keep it in the Library first"));
                return Task.CompletedTask;
            }

            var targets = collection.Id == id ? collection.Sources : [.. collection.Sources.Where(s => s.Id == id)];
            if (sync is not null && targets is [var only] && collection.Id != id && !LibrarySync.CanSyncByDefault(only.Definition))
            {
                outcome = LibraryEditOutcome<SetSourceOptionsResult>.Fail(new InvalidArgument("sync", "only an online source can be kept downloaded"));
                return Task.CompletedTask;
            }

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
                var changed = updated != definition && _library.UpdateSource(collection.Id, updated);
                results.Add(new SourceOptionsInfo(
                    source.Id, _panel.SourceDisplayName(source.Id), updated.Sync, updated.ShowOnMap, canSync, changed));
            }

            outcome = LibraryEditOutcome<SetSourceOptionsResult>.Ok(new SetSourceOptionsResult(results));
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
