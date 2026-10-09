using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// Reads the online catalogues a <see cref="LibrarySourceDraft"/> scopes; a
/// kind whose reader is missing cannot be added.
/// </summary>
public sealed record LibraryCatalogueReaders
{
    /// <summary>Reads a NOAA ENC product catalogue.</summary>
    public Func<Uri, CancellationToken, Task<NoaaEncProductCatalog>>? NoaaEnc { get; init; }

    /// <summary>Reads a USACE Inland ENC product catalogue.</summary>
    public Func<Uri, CancellationToken, Task<UsaceIencProductCatalog>>? UsaceIenc { get; init; }

    /// <summary>Reads an S-100 feed.</summary>
    public Func<Uri, CancellationToken, Task<S100FeedDocument>>? S100Feed { get; init; }

    /// <summary>Reads a community chart list.</summary>
    public Func<Uri, CancellationToken, Task<ChartCatalogsProductCatalog>>? CommunityList { get; init; }

    /// <summary>Reads a remote S-100 exchange catalogue.</summary>
    public Func<Uri, CancellationToken, Task<RemoteS100Catalogue>>? S100Catalogue { get; init; }

    /// <summary>Lists a remote S-100 catalogue's folders for their file sizes; sizes stay unknown without.</summary>
    public Func<RemoteS100Catalogue, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<Uri, S3Object>?>>? ListS100Folders { get; init; }

    /// <summary>Reads each forecast model's latest run.</summary>
    public Func<Uri, IReadOnlyList<ForecastModel>, CancellationToken, Task<IReadOnlyList<ForecastModelSummary>>>? ForecastModels { get; init; }

    /// <summary>Reads what a SECOM service offers, optionally within an area (WKT).</summary>
    public Func<Uri, string?, CancellationToken, Task<SecomServiceDescription>>? Secom { get; init; }

    /// <summary>The map view a SECOM service can be narrowed to; it cannot be without.</summary>
    public Func<GeoBounds?>? CurrentMapView { get; init; }
}

/// <summary>
/// A Library source being added, for a host without the viewer's dialog
/// (#792): what it is, the part of its catalogue it includes, the collection
/// name it suggests, and the source it makes. Uses the same scopes and words
/// as the viewer's Add-to-Library dialog.
/// </summary>
public sealed class LibrarySourceDraft
{
    private readonly TimeProvider _time;
    private bool _keepDownloaded;

    private LibrarySourceDraft(LibrarySourceKind kind, string? path, KnownCatalogueSource? known, LibraryCatalogueScope? scope, TimeProvider? time)
    {
        Kind = kind;
        Path = path;
        Known = known;
        Scope = scope;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>A draft for a local path.</summary>
    /// <param name="kind">What the path is.</param>
    /// <param name="path">The full path.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is an online kind.</exception>
    public static LibrarySourceDraft ForPath(LibrarySourceKind kind, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (LibrarySourceKinds.IsOnline(kind))
            throw new ArgumentException($"A {kind} source is not added by path.", nameof(kind));
        var scope = kind == LibrarySourceKind.LocalManifest ? new CollectionManifestScope(path) : null;
        return new LibrarySourceDraft(kind, path, null, scope, null);
    }

    /// <summary>A draft for a known online catalogue, or null when its kind has no reader.</summary>
    /// <param name="known">The catalogue.</param>
    /// <param name="readers">Reads catalogues.</param>
    /// <param name="time">The clock (catalogue age); the system clock when null.</param>
    public static LibrarySourceDraft? ForCatalogue(KnownCatalogueSource known, LibraryCatalogueReaders readers, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(readers);
        var kind = LibrarySourceKinds.Of(known.Format);
        var uri = LibrarySourceText.CatalogUri(kind, known);
        LibraryCatalogueScope? scope = kind switch
        {
            LibrarySourceKind.NoaaFeed when readers.NoaaEnc is { } noaa => new NoaaEncScope(uri, noaa),
            LibrarySourceKind.UsaceFeed when readers.UsaceIenc is { } usace => new UsaceIencScope(uri, usace),
            LibrarySourceKind.S100Feed when readers.S100Feed is { } feed => new S100FeedScope(uri, feed),
            LibrarySourceKind.CommunityFeed when readers.CommunityList is { } list => new CommunityListScope(uri, list),
            LibrarySourceKind.S100Catalogue when readers.S100Catalogue is { } catalogue => new S100CatalogueScope(uri, catalogue, readers.ListS100Folders),
            LibrarySourceKind.S100Forecast when readers.ForecastModels is { } forecast && known.Models.Count > 0
                => new S100ForecastScope(uri, known.Models, forecast, time),
            LibrarySourceKind.Secom when readers.Secom is { } secom => new SecomScope(uri, secom, readers.CurrentMapView),
            _ => null,
        };
        return scope is null ? null : new LibrarySourceDraft(kind, null, known, scope, time);
    }

    /// <summary>What is being added.</summary>
    public LibrarySourceKind Kind { get; }

    /// <summary>The local path being added, or null for an online catalogue.</summary>
    public string? Path { get; }

    /// <summary>The known catalogue being added, or null for a local path.</summary>
    public KnownCatalogueSource? Known { get; }

    /// <summary>The part of the online catalogue included, or null for a local path.</summary>
    public LibraryCatalogueScope? Scope { get; }

    /// <summary>The title, as the viewer's dialog shows it.</summary>
    public string Title => LibrarySourceText.Title(Kind, Known);

    /// <summary>True once the catalogue has been read (always false for a local path).</summary>
    public bool IsLoaded => Scope?.IsLoaded == true;

    /// <summary>The choice groups, once the catalogue has been read.</summary>
    public IReadOnlyList<LibraryChoiceGroup> Groups => Scope?.Groups ?? [];

    /// <summary>True to include the whole catalogue whatever is ticked (the default).</summary>
    public bool IncludeAll
    {
        get => Scope?.IncludeAll ?? true;
        set
        {
            if (Scope is not null)
                Scope.IncludeAll = value;
        }
    }

    /// <summary>How many choices are ticked.</summary>
    public int SelectedCount => Scope?.SelectedCount ?? 0;

    /// <summary>What the ticks amount to, then whether they are kept, or a prompt to tick something.</summary>
    public string ScopeSummary => Scope?.ScopeSummary ?? string.Empty;

    /// <summary>"host · catalogue dated 2026-09-17" once an online catalogue is read; null before, or for a local source.</summary>
    public string? CatalogueDetail => LibrarySourceKinds.IsOnline(Kind) && Scope is { IsLoaded: true } scope
        ? LibrarySourceText.CatalogueDetail(scope.CatalogUri, scope.CatalogueDate)
        : null;

    /// <summary>True when the catalogue is more than a year old.</summary>
    public bool IsCatalogueStale => LibrarySourceText.IsStale(Scope?.CatalogueDate, _time.GetUtcNow());

    /// <summary>The collection name suggested: the catalogue's, following the selection, or the path's.</summary>
    public string SuggestedName => (LibrarySourceText.FeedName(Kind, Known) ?? Scope?.Name) is { } feed && Scope is not null
        ? LibrarySourceText.SuggestedName(feed, Scope.IsEverything, () => Scope.DescribeSelection()!)
        : LibrarySourceText.DefaultName(Path);

    /// <summary>True when the source is online, so its items can be kept downloaded (#809; a SECOM service's sync, #807).</summary>
    public bool CanKeepDownloaded => LibrarySourceKinds.IsOnline(Kind);

    /// <summary>
    /// True to keep the source's items downloaded and current on each refresh.
    /// Off by default, except for a SECOM service whose selection is small (<see cref="SecomScope.Sync"/>).
    /// </summary>
    public bool KeepDownloaded
    {
        get => Scope is SecomScope secom ? secom.Sync : _keepDownloaded;
        set
        {
            if (Scope is SecomScope secom)
                secom.Sync = value;
            else
                _keepDownloaded = value;
        }
    }

    /// <summary>For a SECOM service, what keeping it downloaded means ("Downloads 9.8 MB now"); otherwise null.</summary>
    public string? KeepDownloadedHint => (Scope as SecomScope)?.SyncHint;

    /// <summary>For a forecast feed whose every run has ended, a note saying so; otherwise null.</summary>
    public string? ForecastEndedNote => (Scope as S100ForecastScope)?.ForecastEndedNote;

    /// <summary>Whether the source is shown on the map; null for the kind's default.</summary>
    public bool? ShowOnMap { get; set; }

    /// <summary>True when the source can be built: its catalogue is read, or it has a path.</summary>
    public bool CanBuild => Scope is not null ? Scope.CanBuild : !string.IsNullOrEmpty(Path);

    /// <summary>Reads the catalogue, for an online source.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Why the catalogue could not be read, or null.</returns>
    public Task<string?> LoadAsync(CancellationToken cancellationToken = default) =>
        Scope?.LoadAsync(cancellationToken) ?? Task.FromResult<string?>(null);

    /// <summary>Builds the source the draft describes.</summary>
    /// <param name="newCollectionName">The new collection's name, when one is created: a manifest source takes it.</param>
    /// <exception cref="InvalidOperationException">The draft cannot be built yet (<see cref="CanBuild"/> is false).</exception>
    public CollectionSource Build(string? newCollectionName = null)
    {
        if (!CanBuild)
            throw new InvalidOperationException("The source cannot be built yet.");
        var id = Guid.NewGuid();
        CollectionSource source = Kind switch
        {
            LibrarySourceKind.Folder => new LocalFolderSource(id, null, Path!),
            LibrarySourceKind.ExchangeSet => new ExchangeSetSource(id, null, Path!),
            LibrarySourceKind.S128Catalogue => new S128CatalogueSource(id, null, Path!),
            LibrarySourceKind.LocalManifest => Scope!.Build(id, newCollectionName),
            _ => Scope!.Build(id, LibrarySourceText.FeedName(Kind, Known)),
        };
        // A SECOM scope builds its own sync choice.
        if (CanKeepDownloaded && Scope is not SecomScope && KeepDownloaded)
            source = source with { Sync = true };
        return ShowOnMap is { } show ? source with { ShowOnMap = show } : source;
    }

    /// <summary>Adds the source to <paramref name="library"/>: to an existing collection, or a new one named <paramref name="collectionName"/>.</summary>
    /// <param name="library">The Library.</param>
    /// <param name="collectionId">An existing collection to add to, or null for a new one.</param>
    /// <param name="collectionName">The new collection's name; <see cref="SuggestedName"/> when null.</param>
    /// <returns>The collection and the new source.</returns>
    /// <exception cref="InvalidOperationException">The draft cannot be built yet, or the collection does not exist.</exception>
    public (Guid CollectionId, Guid SourceId) AddTo(CollectionLibrary library, Guid? collectionId = null, string? collectionName = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        var name = string.IsNullOrWhiteSpace(collectionName) ? SuggestedName : collectionName.Trim();
        var source = Build(collectionId is null ? name : null);
        if (collectionId is { } target)
        {
            if (!library.AddSources(target, [source]))
                throw new InvalidOperationException($"No collection has id '{target}'.");
            return (target, source.Id);
        }

        return (library.AddCollection(name, [source]).Id, source.Id);
    }
}
