using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Library;

/// <summary>
/// <c>add_library_source</c> for every host (#792): resolves what is being
/// added (a known catalogue, a URL or a local path), reads its catalogue
/// through a <see cref="LibrarySourceDraft"/>, applies the request's choices,
/// and previews or adds the source, in the same scopes and words as the
/// viewer's Add-to-Library dialog.
/// </summary>
public sealed class LibrarySourceAdder
{
    private readonly LibraryCatalogueReaders _readers;
    private readonly Func<IReadOnlyList<KnownCatalogueSource>> _userCatalogues;
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>>? _probe;
    private readonly TimeProvider _time;

    /// <summary>Creates the adder.</summary>
    /// <param name="readers">Reads online catalogues; a catalogue without a reader is refused.</param>
    /// <param name="userCatalogues">The user's own catalogues, found by id after the curated ones; none when null.</param>
    /// <param name="probe">Recognises a catalogue URL's format, for adding by URL; URLs are refused without.</param>
    /// <param name="time">The clock (catalogue age, forecast end); the system clock when null.</param>
    public LibrarySourceAdder(
        LibraryCatalogueReaders? readers = null,
        Func<IReadOnlyList<KnownCatalogueSource>>? userCatalogues = null,
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null,
        TimeProvider? time = null)
    {
        _readers = readers ?? new LibraryCatalogueReaders();
        _userCatalogues = userCatalogues ?? (() => []);
        _probe = probe;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Previews or adds the source <paramref name="request"/> describes.</summary>
    /// <param name="request">The request.</param>
    /// <param name="library">The Library.</param>
    /// <param name="onLibraryThread">Runs work that reads or changes the Library on the thread that owns it; inline when null.</param>
    /// <param name="ct">Cancels reading the catalogue.</param>
    public async Task<LibraryEditOutcome<AddSourceResult>> AddAsync(
        AddSourceRequest request, CollectionLibrary library, Func<Func<Task>, Task>? onLibraryThread = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(library);
        onLibraryThread ??= work => work();

        IReadOnlyList<LibraryCollection> collections = [];
        await onLibraryThread(() =>
        {
            collections = [.. library.Collections.Where(c => !c.IsSession)];
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        if (request.CollectionId is { } collectionId && !collections.Any(c => c.Id == collectionId))
            return Fail(new InvalidArgument("collectionId", "no such collection; call list_library_sources"));

        var (draft, error) = await DraftAsync(request, ct).ConfigureAwait(false);
        if (error is not null)
            return Fail(error);

        if (request.Preview)
            return LibraryEditOutcome<AddSourceResult>.Ok(Describe(draft!, collections, request.CollectionId, added: false, null, null));
        if (!draft!.CanBuild)
        {
            return Fail(new LibraryChangeRejected(draft.IncludeAll
                ? "this source cannot be added as it stands"
                : "nothing is selected; pass choices or includeAll"));
        }

        (Guid Collection, Guid Source) added = default;
        await onLibraryThread(() =>
        {
            added = draft.AddTo(library, request.CollectionId, request.CollectionName);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return LibraryEditOutcome<AddSourceResult>.Ok(Describe(draft, collections, request.CollectionId, added: true, added.Collection, added.Source));

        static LibraryEditOutcome<AddSourceResult> Fail(ToolError error) => LibraryEditOutcome<AddSourceResult>.Fail(error);
    }

    /// <summary>
    /// Resolves what <paramref name="request"/> adds, reads its catalogue and
    /// applies its choices, shape, resolution, sync and map options, without
    /// adding it (its collection options are not looked at).
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels reading the catalogue.</param>
    /// <returns>The draft, or why there is none.</returns>
    public async Task<LibraryEditOutcome<LibrarySourceDraft>> DraftAsync(AddSourceRequest request, CancellationToken ct = default)
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

        // Choices, shape and resolution are applied for a preview too, so it
        // shows the resulting scope.
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
            var catalogue = draft.Scope as S100CatalogueScope;
            if (catalogue?.Resolutions.FirstOrDefault(o => Matches(o.Value, o.Label, request.Resolution)) is not { } resolution)
                return Fail(new InvalidArgument("resolution", "not a resolution this catalogue offers; preview to list them"));
            catalogue.SelectedResolution = resolution;
        }
        if (request.Sync is { } sync)
        {
            if (!draft.CanKeepDownloaded)
                return Fail(new InvalidArgument("sync", "only an online source can be kept downloaded"));
            draft.KeepDownloaded = sync;
        }
        draft.ShowOnMap = request.ShowOnMap;
        return LibraryEditOutcome<LibrarySourceDraft>.Ok(draft);

        static LibraryEditOutcome<LibrarySourceDraft> Fail(ToolError error) => LibraryEditOutcome<LibrarySourceDraft>.Fail(error);
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
}
