using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Noaa;
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
}

/// <summary>
/// A Library source being added, for a host without the viewer's dialog
/// (#792): what it is, the part of its catalogue it includes, the collection
/// name it suggests, and the source it makes. Uses the same scopes and words
/// as the viewer's Add-to-Library dialog.
/// </summary>
/// <remarks>
/// Kinds move here from the dialog one at a time; <see cref="IsSupported"/>
/// says which a draft can add so far.
/// </remarks>
public sealed class LibrarySourceDraft
{
    private readonly TimeProvider _time;

    private LibrarySourceDraft(LibrarySourceKind kind, string? path, KnownCatalogueSource? known, LibraryCatalogueScope? scope, TimeProvider? time)
    {
        Kind = kind;
        Path = path;
        Known = known;
        Scope = scope;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True for the kinds a draft can add so far.</summary>
    /// <param name="kind">The kind.</param>
    public static bool IsSupported(LibrarySourceKind kind) => kind is LibrarySourceKind.Folder or LibrarySourceKind.ExchangeSet
        or LibrarySourceKind.S128Catalogue or LibrarySourceKind.NoaaFeed or LibrarySourceKind.UsaceFeed;

    /// <summary>A draft for a local path, or null when its kind is not supported yet.</summary>
    /// <param name="kind">What the path is.</param>
    /// <param name="path">The full path.</param>
    public static LibrarySourceDraft? ForPath(LibrarySourceKind kind, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return IsSupported(kind) && !LibrarySourceKinds.IsOnline(kind) ? new LibrarySourceDraft(kind, path, null, null, null) : null;
    }

    /// <summary>A draft for a known online catalogue, or null when its kind is not supported yet or has no reader.</summary>
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
    public bool IncludeAll { get; set; } = true;

    /// <summary>How many choices are ticked.</summary>
    public int SelectedCount => Scope?.Choices.Count(c => c.IsSelected) ?? 0;

    /// <summary>What the ticks amount to, then whether they are kept, or a prompt to tick something.</summary>
    public string ScopeSummary => LibrarySourceText.ScopeSummary(IncludeAll, SelectedCount, Scope?.SelectionSummary ?? string.Empty);

    /// <summary>"host · catalogue dated 2026-09-17" once the catalogue is read; null before, or for a local path.</summary>
    public string? CatalogueDetail => Scope is { IsLoaded: true } scope ? LibrarySourceText.CatalogueDetail(scope.CatalogUri, scope.CatalogueDate) : null;

    /// <summary>True when the catalogue is more than a year old.</summary>
    public bool IsCatalogueStale => LibrarySourceText.IsStale(Scope?.CatalogueDate, _time.GetUtcNow());

    /// <summary>The collection name suggested: the catalogue's, following the selection, or the path's.</summary>
    public string SuggestedName => LibrarySourceText.FeedName(Kind, Known) is { } feed
        ? LibrarySourceText.SuggestedName(feed, IncludeAll || Scope?.IsUnscoped != false, () => Scope!.DescribeSelection()!)
        : LibrarySourceText.DefaultName(Path);

    /// <summary>True when the source is online, so its items can be kept downloaded (#809).</summary>
    public bool CanKeepDownloaded => LibrarySourceKinds.IsOnline(Kind) && Kind != LibrarySourceKind.Secom;

    /// <summary>True to keep the source's items downloaded and current on each refresh.</summary>
    public bool KeepDownloaded { get; set; }

    /// <summary>Whether the source is shown on the map; null for the kind's default.</summary>
    public bool? ShowOnMap { get; set; }

    /// <summary>True when the source can be built: its catalogue is read, or it has a path.</summary>
    public bool CanBuild => Scope is not null ? Scope.IsLoaded : !string.IsNullOrEmpty(Path);

    /// <summary>Reads the catalogue, for an online source.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Why the catalogue could not be read, or null.</returns>
    public Task<string?> LoadAsync(CancellationToken cancellationToken = default) =>
        Scope?.LoadAsync(cancellationToken) ?? Task.FromResult<string?>(null);

    /// <summary>Builds the source the draft describes.</summary>
    /// <exception cref="InvalidOperationException">The draft cannot be built yet (<see cref="CanBuild"/> is false).</exception>
    public CollectionSource Build()
    {
        if (!CanBuild)
            throw new InvalidOperationException("The source cannot be built yet.");
        var id = Guid.NewGuid();
        CollectionSource source = Kind switch
        {
            LibrarySourceKind.Folder => new LocalFolderSource(id, null, Path!),
            LibrarySourceKind.ExchangeSet => new ExchangeSetSource(id, null, Path!),
            LibrarySourceKind.S128Catalogue => new S128CatalogueSource(id, null, Path!),
            _ => Scope!.Build(id, IncludeAll, LibrarySourceText.FeedName(Kind, Known)),
        };
        if (CanKeepDownloaded && KeepDownloaded)
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
        var source = Build();
        if (collectionId is { } target)
        {
            if (!library.AddSources(target, [source]))
                throw new InvalidOperationException($"No collection has id '{target}'.");
            return (target, source.Id);
        }

        var name = string.IsNullOrWhiteSpace(collectionName) ? SuggestedName : collectionName.Trim();
        return (library.AddCollection(name, [source]).Id, source.Id);
    }
}
