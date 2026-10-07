using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Changes the Library for agents (MCP <c>add_library_source</c>,
/// <c>refresh_library_source</c>, <c>library_action</c>,
/// <c>remove_library_source</c>, <c>await_library_idle</c>, #715). Each goes
/// through the code path the UI uses: the Add-to-Library dialog's view model,
/// the panel's download and load entry points, and the Library service.
/// </summary>
internal interface IViewerLibraryEditor
{
    /// <summary>Previews or adds a source, as the Add-to-Library dialog does.</summary>
    Task<EditOutcome<AddSourceResult>> AddSourceAsync(AddSourceRequest request, CancellationToken ct = default);

    /// <summary>Re-indexes a collection, a source, or everything, and reports what changed.</summary>
    Task<EditOutcome<RefreshResult>> RefreshAsync(Guid? id, TimeSpan wait, CancellationToken ct = default);

    /// <summary>Loads, downloads, updates or cancels items, or previews doing so.</summary>
    Task<EditOutcome<LibraryActionResult>> ActAsync(LibraryActionRequest request, CancellationToken ct = default);

    /// <summary>Removes a collection or source from the Library.</summary>
    Task<EditOutcome<RemoveSourceResult>> RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Waits until no source is indexing, no download is running, and every
    /// dataset a Library action opens has opened.
    /// </summary>
    Task<LibraryIdleResult> AwaitIdleAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Cancels every running download, as the bulk bar's Cancel does.</summary>
    Task CancelAllDownloadsAsync(CancellationToken ct = default);
}

/// <summary>A result, or the error explaining why there is none.</summary>
internal readonly record struct EditOutcome<T>(T? Value, ToolError? Error)
{
    public static EditOutcome<T> Ok(T value) => new(value, null);

    public static EditOutcome<T> Fail(ToolError error) => new(default, error);
}

/// <summary>What <see cref="IViewerLibraryEditor.AddSourceAsync"/> adds.</summary>
/// <param name="KnownSourceId">A <c>list_known_sources</c> id.</param>
/// <param name="Path">A local folder, exchange set (folder, ZIP or catalogue), collection manifest or S-128 catalogue.</param>
/// <param name="Url">An online catalogue or feed URL, recognised by its format, or a SECOM service endpoint.</param>
/// <param name="Kind">For a path: folder, exchange_set, manifest or s128; inferred when null.</param>
/// <param name="Choices">Choice values or labels to include (states, districts, rivers, models, areas, groups, products, charts).</param>
/// <param name="IncludeAll">True to include everything the catalogue lists; defaults to true without choices.</param>
/// <param name="CollectionId">An existing collection to add to; a new one when null.</param>
/// <param name="CollectionName">The new collection's name; the dialog's default when null.</param>
/// <param name="Shape">For a forecast feed: tiles or regional.</param>
/// <param name="Resolution">For an S-100 catalogue with several resolutions: its value or label.</param>
/// <param name="Preview">True to only load the catalogue and report the choices.</param>
internal sealed record AddSourceRequest(
    string? KnownSourceId,
    string? Path,
    string? Url,
    string? Kind,
    IReadOnlyList<string>? Choices,
    bool? IncludeAll,
    Guid? CollectionId,
    string? CollectionName,
    string? Shape,
    string? Resolution,
    bool Preview);

/// <summary>What add_library_source found or added.</summary>
[Description("What add_library_source found (preview) or added.")]
internal sealed record AddSourceResult(
    [property: Description("True when a source was added; false for a preview.")] bool Added,
    [property: Description("What is being added: Folder, ExchangeSet, S128Catalogue, LocalManifest, NoaaFeed, UsaceFeed, CommunityFeed, S100Feed, S100Catalogue, S100Forecast or Secom.")] string Kind,
    [property: Description("The dialog's title for it, e.g. the known source's name.")] string Title,
    [property: Description("What the catalogue says about itself (date, size), or null.")] string? CatalogueDetail,
    [property: Description("True when the catalogue is over a year old.")] bool CatalogueStale,
    [property: Description("A note shown in the dialog, e.g. that a forecast has ended with no newer run, or null.")] string? Note,
    [property: Description("The scope as the dialog summarises it.")] string Scope,
    [property: Description("Groups of choices (pass values or labels in 'choices'); empty when there is nothing to choose.")] IReadOnlyList<AddChoiceGroup> Choices,
    [property: Description("For a forecast feed, the download shapes (pass one as 'shape'); otherwise empty.")] IReadOnlyList<AddChoiceOption> Shapes,
    [property: Description("For an S-100 catalogue with several resolutions, the choices (pass one as 'resolution'); otherwise empty.")] IReadOnlyList<AddChoiceOption> Resolutions,
    [property: Description("Existing collections the source can be added to (pass an id as 'collectionId').")] IReadOnlyList<AddChoiceOption> Collections,
    [property: Description("The collection the source was added to, or null for a preview.")] Guid? CollectionId,
    [property: Description("The new source's id, or null for a preview.")] Guid? SourceId);

/// <summary>A group of add choices.</summary>
[Description("A group of choices in the Add-to-Library dialog, e.g. States or Forecast models.")]
internal sealed record AddChoiceGroup(
    [property: Description("Group title.")] string Title,
    [property: Description("Its options.")] IReadOnlyList<AddChoiceOption> Options);

/// <summary>One add choice.</summary>
[Description("One choice in the Add-to-Library dialog.")]
internal sealed record AddChoiceOption(
    [property: Description("Value to pass back.")] string Value,
    [property: Description("Label as shown (also accepted).")] string Label,
    [property: Description("Detail as shown, e.g. cell count and size, or null.")] string? Detail,
    [property: Description("True when ticked.")] bool Selected);

/// <summary>What refresh_library_source changed.</summary>
[Description("What a refresh changed.")]
internal sealed record RefreshResult(
    [property: Description("True when the call waited for indexing to finish.")] bool Waited,
    [property: Description("True when indexing was still running when the wait ended.")] bool TimedOut,
    [property: Description("Items in scope after the refresh.")] int Total,
    [property: Description("Items that were not listed before.")] int Added,
    [property: Description("Items no longer listed.")] int Removed,
    [property: Description("Items whose state changed, counted by their new state (e.g. 'update', 'expired').")] IReadOnlyDictionary<string, int> Changed,
    [property: Description("Item counts by state after the refresh.")] IReadOnlyDictionary<string, int> Counts);

/// <summary>What library_action does to which items.</summary>
/// <param name="Action">load, load_as_you_pan, download, download_only, update or cancel.</param>
/// <param name="ItemIds">Item ids, or null to use <paramref name="Filter"/>.</param>
/// <param name="Filter">The items to act on, as query_library_items filters them, or null.</param>
/// <param name="DryRun">True to report what would happen without doing it.</param>
/// <param name="MaxBytes">Refuse a download larger than this.</param>
internal sealed record LibraryActionRequest(
    string Action,
    IReadOnlyList<string>? ItemIds,
    LibraryItemPageQuery? Filter,
    bool DryRun,
    long? MaxBytes);

/// <summary>What library_action did or would do.</summary>
[Description("What library_action did, or would do on a dry run.")]
internal sealed record LibraryActionResult(
    [property: Description("The action.")] string Action,
    [property: Description("True for a dry run: nothing was done.")] bool DryRun,
    [property: Description("Items the action applies to.")] int Eligible,
    [property: Description("Selected items it does not apply to, counted by their state.")] IReadOnlyDictionary<string, int> Skipped,
    [property: Description("Known download size in bytes (download, download_only, update).")] long Bytes,
    [property: Description("Eligible items whose download size is unknown.")] int UnknownSizes,
    [property: Description("Names of up to 50 eligible items.")] IReadOnlyList<string> Items,
    [property: Description("For load and load_as_you_pan, how many datasets were opened or registered; otherwise null.")] int? Opened,
    [property: Description("True when downloads were started; they continue in the background (await_library_idle waits for them).")] bool Started);

/// <summary>What remove_library_source removed.</summary>
[Description("What was removed from the Library.")]
internal sealed record RemoveSourceResult(
    [property: Description("The id removed.")] Guid Id,
    [property: Description("Its name.")] string Name,
    [property: Description("True for a whole collection, false for one source.")] bool WasCollection,
    [property: Description("How many items it listed.")] int ItemCount);

/// <summary>The Library's background work.</summary>
[Description("Whether the Library is busy indexing, downloading or opening datasets.")]
internal sealed record LibraryIdleResult(
    [property: Description("True when nothing is indexing, downloading or opening datasets.")] bool Idle,
    [property: Description("True when the wait ended before the Library was idle.")] bool TimedOut,
    [property: Description("How long the call waited, in milliseconds.")] long WaitedMs,
    [property: Description("True while a source is indexing.")] bool Indexing,
    [property: Description("Datasets a library_action download or load has yet to open (including any still downloading).")] int Loading,
    [property: Description("The running download batch, or null.")] LibraryDownloadInfo? Downloads);

/// <summary>A running download batch.</summary>
[Description("Progress of the running download batch.")]
internal sealed record LibraryDownloadInfo(
    [property: Description("Items finished.")] int Completed,
    [property: Description("Items failed.")] int Failed,
    [property: Description("Items in the batch.")] int Total,
    [property: Description("Bytes downloaded.")] long BytesDone,
    [property: Description("Bytes in the batch, as far as known.")] long BytesTotal);

/// <summary>A library_action, add or remove could not be done as asked.</summary>
[Description("Raised when a Library change cannot be done as asked; the reason says why (e.g. the catalogue failed to load, nothing is selected, or a download exceeds maxBytes).")]
internal sealed record LibraryChangeRejected(
    [property: Description("Why the change was not made.")] string Reason)
    : ToolError("library_change_rejected", $"The Library was not changed: {Reason}.");

/// <summary>Default <see cref="IViewerLibraryEditor"/>.</summary>
internal sealed class ViewerLibraryEditor : IViewerLibraryEditor
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
    public async Task<EditOutcome<AddSourceResult>> AddSourceAsync(AddSourceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var given = new[] { request.KnownSourceId, request.Path, request.Url }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (given != 1)
            return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("knownSourceId", "supply exactly one of knownSourceId, path and url"));

        KnownCatalogueSource? known = null;
        if (!string.IsNullOrWhiteSpace(request.KnownSourceId))
        {
            var id = request.KnownSourceId.Trim();
            known = KnownCatalogueSources.Find(id)
                ?? _userCatalogues().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (known is null)
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("knownSourceId", $"no known source '{id}'; call list_known_sources"));
        }
        else if (!string.IsNullOrWhiteSpace(request.Url))
        {
            if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("url", "expected an http or https URL"));
            if (_probe is null)
                return EditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected("this viewer cannot read online catalogues"));
            CatalogueProbe probe;
            try
            {
                probe = await _probe(uri, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return EditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected($"the URL could not be read ({ex.Message})"));
            }
            if (probe.Format is not { } format)
                return EditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected("the URL is not a catalogue or feed the Library can read"));
            known = KnownCatalogueSources.FromUrl(uri, format, probe.Title);
        }

        AddToLibraryKind? pathKind = null;
        string? path = null;
        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            path = Path.GetFullPath(request.Path.Trim());
            if (!File.Exists(path) && !Directory.Exists(path))
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("path", $"'{path}' does not exist"));
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
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("kind", "expected folder, exchange_set, manifest or s128"));
        }

        EditOutcome<AddSourceResult> outcome = default;
        await _dispatch(async () => outcome = await AddOnUiThreadAsync(request, known, pathKind, path, ct).ConfigureAwait(true)).ConfigureAwait(false);
        return outcome;
    }

    private async Task<EditOutcome<AddSourceResult>> AddOnUiThreadAsync(
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
                return EditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected($"the catalogue could not be loaded: {error}"));
        }

        if (request.CollectionId is { } collectionId && !dialog.ExistingCollections.Any(c => c.Id == collectionId))
            return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("collectionId", "no such collection; call list_library_sources"));

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
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument(
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
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("shape", "only a forecast feed has shapes; expected tiles or regional"));
            dialog.SelectedForecastShape = shape;
        }

        if (!string.IsNullOrWhiteSpace(request.Resolution))
        {
            var resolution = dialog.Resolutions.FirstOrDefault(o => Matches(o.Value, o.Label, request.Resolution));
            if (resolution is null)
                return EditOutcome<AddSourceResult>.Fail(new InvalidArgument("resolution", "not a resolution this catalogue offers; preview to list them"));
            dialog.SelectedResolution = resolution;
        }

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
            return EditOutcome<AddSourceResult>.Ok(Describe(dialog, added: false, null, null));

        if (!dialog.ConfirmCommand.CanExecute(null))
        {
            return EditOutcome<AddSourceResult>.Fail(new LibraryChangeRejected(
                dialog.IncludeAll ? "the dialog cannot add this source as it stands" : "nothing is selected; pass choices or includeAll"));
        }

        var before = _library.Collections.SelectMany(c => c.Sources).Select(s => s.Id).ToHashSet();
        dialog.ConfirmCommand.Execute(null);
        var added = _library.Collections
            .SelectMany(c => c.Sources.Select(s => (Collection: c, Source: s)))
            .FirstOrDefault(p => !before.Contains(p.Source.Id));
        return EditOutcome<AddSourceResult>.Ok(Describe(dialog, added: true, added.Collection?.Id, added.Source?.Id));
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
        sourceId);

    private static bool Matches(string? value, string label, string wanted)
    {
        var w = wanted.Trim();
        return string.Equals(value, w, StringComparison.OrdinalIgnoreCase)
            || string.Equals(label, w, StringComparison.OrdinalIgnoreCase);
    }

    // ── refresh ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<EditOutcome<RefreshResult>> RefreshAsync(Guid? id, TimeSpan wait, CancellationToken ct = default)
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
            return EditOutcome<RefreshResult>.Fail(new InvalidArgument("id", "no such collection or source; call list_library_sources"));

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
        return EditOutcome<RefreshResult>.Ok(new RefreshResult(
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
    public async Task<EditOutcome<LibraryActionResult>> ActAsync(LibraryActionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Actions.Contains(action))
            return EditOutcome<LibraryActionResult>.Fail(new InvalidArgument("action", $"expected one of {string.Join(", ", Actions)}"));
        var hasIds = request.ItemIds is { Count: > 0 };
        if (hasIds == (request.Filter is not null))
        {
            return EditOutcome<LibraryActionResult>.Fail(new InvalidArgument(
                "itemIds", "pass itemIds or filters (sourceId, states, spec, text, box or point), not both and not neither"));
        }

        EditOutcome<LibraryActionResult> outcome = default;
        await _dispatch(async () => outcome = await ActOnUiThreadAsync(action, request, ct).ConfigureAwait(true)).ConfigureAwait(false);
        return outcome;
    }

    /// <inheritdoc />
    public Task CancelAllDownloadsAsync(CancellationToken ct = default) => _dispatch(() =>
    {
        _panel.Downloader.CancelAll();
        return Task.CompletedTask;
    });

    private async Task<EditOutcome<LibraryActionResult>> ActOnUiThreadAsync(string action, LibraryActionRequest request, CancellationToken ct)
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
                return EditOutcome<LibraryActionResult>.Fail(new InvalidArgument("itemIds", $"unknown item id(s): {string.Join(", ", unknown)}"));
        }
        else if (_reader.FindRows(request.Filter!) is { } found)
        {
            rows = found;
        }
        else
        {
            return EditOutcome<LibraryActionResult>.Fail(new InvalidArgument("sourceId", "no such collection or source; call list_library_sources"));
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
            return EditOutcome<LibraryActionResult>.Ok(Result(null, false));
        if (chosen.Count == 0)
            return EditOutcome<LibraryActionResult>.Ok(Result(action.StartsWith("load", StringComparison.Ordinal) ? 0 : null, false));
        if (downloads && request.MaxBytes is { } max && bytes > max)
        {
            return EditOutcome<LibraryActionResult>.Fail(new LibraryChangeRejected(
                $"the download is {LibraryItemViewModel.FormatBytes(bytes)}, more than maxBytes ({LibraryItemViewModel.FormatBytes(max)})"));
        }

        switch (action)
        {
            case "load" or "load_as_you_pan":
                // Tracked so a concurrent await_library_idle waits for the opens (#790).
                using (_activity.Begin(action == "load" ? chosen.Count : 0))
                {
                    var loaded = await _panel.LoadRowsAsync(chosen, defer: action == "load_as_you_pan").ConfigureAwait(true);
                    return EditOutcome<LibraryActionResult>.Ok(Result(loaded.Opened, false));
                }
            case "cancel":
                foreach (var row in chosen)
                    row.CancelDownloadCommand.Execute(null);
                return EditOutcome<LibraryActionResult>.Ok(Result(null, false));
            default:
                // Downloads run on in the background, as from the panel; await_library_idle waits for
                // them and for what follows (re-indexing, opening) until RunDownloadAsync ends (#790).
                var load = action == "download";
                _ = RunDownloadAsync(chosen, load, _activity.Begin(load ? chosen.Count : 0));
                return EditOutcome<LibraryActionResult>.Ok(Result(null, true));
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
    public async Task<EditOutcome<RemoveSourceResult>> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EditOutcome<RemoveSourceResult> outcome = EditOutcome<RemoveSourceResult>.Fail(
            new InvalidArgument("id", "no such collection or source; call list_library_sources"));
        await _dispatch(() =>
        {
            if (_library.Collections.FirstOrDefault(c => c.Id == id) is { } collection)
            {
                if (collection.IsSession)
                {
                    outcome = EditOutcome<RemoveSourceResult>.Fail(new LibraryChangeRejected("the session collection cannot be removed"));
                }
                else if (_library.RemoveCollection(id))
                {
                    outcome = EditOutcome<RemoveSourceResult>.Ok(new RemoveSourceResult(id, collection.Definition.Name, true, collection.ItemCount));
                }
            }
            else if (_library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == id)) is { } owner)
            {
                var source = owner.Sources.First(s => s.Id == id);
                if (owner.IsSession)
                {
                    outcome = EditOutcome<RemoveSourceResult>.Fail(new LibraryChangeRejected("a session catalogue cannot be removed this way"));
                }
                else if (_library.RemoveSource(owner.Id, id))
                {
                    outcome = EditOutcome<RemoveSourceResult>.Ok(new RemoveSourceResult(
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
