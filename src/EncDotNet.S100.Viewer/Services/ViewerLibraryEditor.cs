using System.Diagnostics;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's <see cref="ILibraryEditor"/>: each change goes through the code
/// path the UI uses (the core's source drafts the Add-to-Library dialog is built
/// on, the Library's open and download workflows, and the Library service).
/// </summary>
internal sealed class ViewerLibraryEditor : ILibraryEditor
{
    private readonly CollectionLibrary _library;
    private readonly ViewerLibraryController _reader;
    private readonly LibrarySourceAdder _adder;
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly ViewerLibraryActions _actions;
    private readonly LibrarySourceNames _names;

    /// <summary>Creates the editor.</summary>
    /// <param name="library">The Library it changes.</param>
    /// <param name="reader">Reads the Library's rows as the user sees them.</param>
    /// <param name="adder">Adds sources.</param>
    /// <param name="dispatch">Runs a change on the UI thread.</param>
    /// <param name="actions">Runs <c>library_action</c>.</param>
    /// <param name="names">Names sources as the Library tree does.</param>
    public ViewerLibraryEditor(
        CollectionLibrary library,
        ViewerLibraryController reader,
        LibrarySourceAdder adder,
        Func<Func<Task>, Task> dispatch,
        ViewerLibraryActions actions,
        LibrarySourceNames names)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(adder);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(names);
        _library = library;
        _reader = reader;
        _adder = adder;
        _dispatch = dispatch;
        _actions = actions;
        _names = names;
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
        await _dispatch(async () => outcome = await _actions.RunAsync(action, request, ct).ConfigureAwait(true)).ConfigureAwait(false);
        return outcome;
    }

    /// <inheritdoc />
    public Task CancelAllDownloadsAsync(CancellationToken ct = default) => _dispatch(() =>
    {
        _actions.CancelAll();
        return Task.CompletedTask;
    });

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
        Guid id, SourceOptionsChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ct.ThrowIfCancellationRequested();
        LibraryEditOutcome<SetSourceOptionsResult> outcome = LibraryEditOutcome<SetSourceOptionsResult>.Fail(
            new InvalidArgument("id", "no such collection or source; call list_library_sources"));
        await _dispatch(() =>
        {
            var collection = _library.Collections.FirstOrDefault(c => c.Id == id)
                ?? _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == id));
            if (collection is not null)
            {
                outcome = change.Apply(
                    collection, id, updated => _library.UpdateSource(collection.Id, updated), source => _names.Of(source.Id));
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
                activity = _actions.CaptureActivity(_library);
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
