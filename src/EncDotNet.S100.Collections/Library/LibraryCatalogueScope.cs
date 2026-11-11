using System.Globalization;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections.Library;

/// <summary>A titled group of choices (one tab in the viewer's dialog), e.g. States or Rivers.</summary>
/// <param name="Title">The group's title.</param>
/// <param name="Options">Its choices, in order.</param>
public sealed record LibraryChoiceGroup(string Title, IReadOnlyList<LibraryChoice> Options)
{
    /// <summary>What the group stands for, when it is a value itself (a remote catalogue's region folder).</summary>
    public string? Key { get; init; }
}

/// <summary>
/// What part of an online catalogue a new Library source includes (#792): the
/// catalogue as read, its choices, what the ticked choices amount to, and the
/// source they make. One per catalogue kind; the viewer's Add-to-Library dialog
/// and headless hosts share them.
/// </summary>
public abstract class LibraryCatalogueScope
{
    /// <summary>Creates a scope for the catalogue at <paramref name="catalogUri"/>.</summary>
    /// <param name="catalogUri">The catalogue's URL.</param>
    protected LibraryCatalogueScope(Uri catalogUri)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        CatalogUri = catalogUri;
    }

    /// <summary>The catalogue's URL.</summary>
    public Uri CatalogUri { get; }

    /// <summary>True once the catalogue has been read.</summary>
    public abstract bool IsLoaded { get; }

    /// <summary>The date the catalogue declares, once read.</summary>
    public DateOnly? CatalogueDate { get; protected set; }

    /// <summary>The choice groups, once the catalogue has been read.</summary>
    public abstract IReadOnlyList<LibraryChoiceGroup> Groups { get; }

    /// <summary>Every choice across the groups.</summary>
    public IEnumerable<LibraryChoice> Choices => Groups.SelectMany(g => g.Options);

    /// <summary>True when nothing is ticked (the selection means the whole catalogue).</summary>
    public bool IsUnscoped => !Choices.Any(c => c.IsSelected);

    /// <summary>How many choices are ticked.</summary>
    public int SelectedCount => Choices.Count(c => c.IsSelected);

    /// <summary>
    /// True to include the whole catalogue whatever is ticked (the default);
    /// false to include only the ticked choices.
    /// </summary>
    public bool IncludeAll { get; set; } = true;

    /// <summary>True when the source will include everything: the suggested name is then the catalogue's alone.</summary>
    public virtual bool IsEverything => IncludeAll || IsUnscoped;

    /// <summary>The summary under the choices: what is included, that ticks are kept, or a prompt to tick something.</summary>
    public virtual string ScopeSummary => LibrarySourceText.ScopeSummary(IncludeAll, SelectedCount, SelectionSummary);

    /// <summary>True when the source can be built (by default, once the catalogue is read).</summary>
    public virtual bool CanBuild => IsLoaded;

    /// <summary>The catalogue's own name, when it has one (a manifest's title); otherwise null.</summary>
    public virtual string? Name => null;

    /// <summary>Raised when something the scope shows changed on its own (sizes arriving after a listing).</summary>
    public event EventHandler? Changed;

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>What the ticked choices (or, with none, the whole catalogue) amount to, e.g. "12 cells · 3 MB".</summary>
    public abstract string SelectionSummary { get; }

    /// <summary>"12,345 cells · 1.2 GB" for the whole catalogue.</summary>
    public abstract string EverythingSummary { get; }

    /// <summary>The catalogue's only download, when it lists just one (there is nothing to choose); otherwise null.</summary>
    public abstract LibraryChoice? SingleEntry { get; }

    /// <summary>Describes the ticked choices ("Alaska, Hawaii"), or null when nothing is ticked.</summary>
    public abstract string? DescribeSelection();

    /// <summary>Reads the catalogue and creates its choices.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Why the catalogue could not be read, or null when it was.</returns>
    public abstract Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>The source the scope describes.</summary>
    /// <param name="id">The new source's id.</param>
    /// <param name="name">
    /// The source's name when everything is included (the catalogue's name);
    /// a selection names it after the selection. A manifest source always takes it.
    /// </param>
    public abstract CollectionSource Build(Guid id, string? name);

    /// <summary>Runs <paramref name="load"/>, turning the expected failures of reading a catalogue into a message.</summary>
    protected static async Task<string?> TryLoadAsync(Func<Task> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        try
        {
            await load().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Xml.XmlException or TaskCanceledException
            or System.Text.Json.JsonException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    /// <summary>"Download (1.7 MB)"-style size text for a single entry, or empty when unknown.</summary>
    protected static string Size(long? bytes) => bytes is { } b ? LibraryTextFormat.Bytes(b) : string.Empty;
}

/// <summary>A scope of the NOAA ENC product catalogue: by state, Coast Guard district or region.</summary>
public sealed class NoaaEncScope : LibraryCatalogueScope
{
    private readonly Func<Uri, CancellationToken, Task<NoaaEncProductCatalog>> _load;
    private NoaaEncProductCatalog? _catalog;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];

    /// <summary>Creates a scope of the NOAA catalogue at <paramref name="catalogUri"/>.</summary>
    /// <param name="catalogUri">The catalogue's URL.</param>
    /// <param name="load">Reads the catalogue.</param>
    public NoaaEncScope(Uri catalogUri, Func<Uri, CancellationToken, Task<NoaaEncProductCatalog>> load)
        : base(catalogUri)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <inheritdoc />
    public override bool IsLoaded => _catalog is not null;

    /// <inheritdoc />
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The states in the catalogue.</summary>
    public IReadOnlyList<LibraryChoice> States => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>The Coast Guard districts in the catalogue.</summary>
    public IReadOnlyList<LibraryChoice> CoastGuardDistricts => _groups.Count > 1 ? _groups[1].Options : [];

    /// <summary>The regions in the catalogue.</summary>
    public IReadOnlyList<LibraryChoice> Regions => _groups.Count > 2 ? _groups[2].Options : [];

    /// <summary>The filter for the ticked choices.</summary>
    public NoaaEncFilter CurrentFilter => new()
    {
        States = States.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
        CoastGuardDistricts = CoastGuardDistricts.Where(o => o.IsSelected)
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture)).ToArray(),
        Regions = Regions.Where(o => o.IsSelected)
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture)).ToArray(),
    };

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        var catalog = await _load(CatalogUri, cancellationToken).ConfigureAwait(false);
        var facets = NoaaEncFacets.Compute(catalog);
        _groups =
        [
            new(LibraryText.Get("Library_NoaaStates"),
                [.. facets.States.OrderBy(f => UsStateNames.SortKey(f.Value), StringComparer.CurrentCulture)
                    .Select(f => new LibraryChoice(f, UsStateNames.Describe(f.Value)))]),
            new(LibraryText.Get("Library_NoaaDistricts"),
                [.. facets.CoastGuardDistricts.OrderBy(f => int.Parse(f.Value, CultureInfo.InvariantCulture))
                    .Select(f => new LibraryChoice(f, string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_DistrictFormat"), f.Value)))]),
            new(LibraryText.Get("Library_NoaaRegions"),
                [.. facets.Regions.OrderBy(f => int.Parse(f.Value, CultureInfo.InvariantCulture))
                    .Select(f => new LibraryChoice(f, string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_RegionFormat"), f.Value)))]),
        ];
        CatalogueDate = catalog.Header.ValidAt is { } valid ? DateOnly.FromDateTime(valid.UtcDateTime) : null;
        _catalog = catalog;
    });

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_catalog is null)
                return string.Empty;
            var filter = CurrentFilter;
            var (count, bytes) = NoaaEncFacets.Summarize(_catalog, filter);
            return LibrarySourceText.Cells(filter.IsUnscoped, count, bytes);
        }
    }

    /// <inheritdoc />
    public override string EverythingSummary
    {
        get
        {
            if (_catalog is null)
                return string.Empty;
            var (count, bytes) = NoaaEncFacets.Summarize(_catalog, new NoaaEncFilter());
            return LibrarySourceText.EverythingCells(count, bytes);
        }
    }

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry =>
        _catalog?.Cells is [var cell] ? new LibraryChoice(cell.Name, cell.LongName ?? cell.Name, Size(cell.ZipSize)) : null;

    /// <inheritdoc />
    public override string? DescribeSelection() =>
        CurrentFilter.IsUnscoped
            ? null
            : LibrarySourceText.List(
            [
                .. States.Where(o => o.IsSelected).Select(o => UsStateNames.SortKey(o.Value)),
                .. CoastGuardDistricts.Where(o => o.IsSelected).Select(o => o.Label),
                .. Regions.Where(o => o.IsSelected).Select(o => o.Label),
            ]);

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name) => IncludeAll
        ? new NoaaEncFeedSource(id, name, CatalogUri, new NoaaEncFilter())
        : new NoaaEncFeedSource(id, DescribeSelection() ?? name, CatalogUri, CurrentFilter);
}

/// <summary>A scope of the USACE Inland ENC product catalogue: by river.</summary>
public sealed class UsaceIencScope : LibraryCatalogueScope
{
    private readonly Func<Uri, CancellationToken, Task<UsaceIencProductCatalog>> _load;
    private UsaceIencProductCatalog? _catalog;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];

    /// <summary>Creates a scope of the USACE catalogue at <paramref name="catalogUri"/>.</summary>
    /// <param name="catalogUri">The catalogue's URL.</param>
    /// <param name="load">Reads the catalogue.</param>
    public UsaceIencScope(Uri catalogUri, Func<Uri, CancellationToken, Task<UsaceIencProductCatalog>> load)
        : base(catalogUri)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <inheritdoc />
    public override bool IsLoaded => _catalog is not null;

    /// <inheritdoc />
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The rivers in the catalogue.</summary>
    public IReadOnlyList<LibraryChoice> Rivers => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>The filter for the ticked rivers.</summary>
    public UsaceIencFilter CurrentFilter => new()
    {
        Rivers = Rivers.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
    };

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        var catalog = await _load(CatalogUri, cancellationToken).ConfigureAwait(false);
        _groups = [new(LibraryText.Get("Library_UsaceRivers"), [.. UsaceIencFeedIndexer.Rivers(catalog).Select(f => new LibraryChoice(f, f.Value))])];
        CatalogueDate = catalog.CreatedOn;
        _catalog = catalog;
    });

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_catalog is null)
                return string.Empty;
            var filter = CurrentFilter;
            var selected = _catalog.Cells.Where(filter.Matches).ToArray();
            return LibrarySourceText.Cells(filter.IsUnscoped, selected.Length, selected.Sum(c => c.ZipSize ?? 0));
        }
    }

    /// <inheritdoc />
    public override string EverythingSummary => _catalog is null
        ? string.Empty
        : LibrarySourceText.EverythingCells(_catalog.Cells.Count, _catalog.Cells.Sum(c => c.ZipSize ?? 0));

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry =>
        _catalog?.Cells is [var cell] ? new LibraryChoice(cell.Name, cell.Name, Size(cell.ZipSize)) : null;

    /// <inheritdoc />
    public override string? DescribeSelection()
    {
        var filter = CurrentFilter;
        return filter.IsUnscoped ? null : LibrarySourceText.List([.. filter.Rivers]);
    }

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name) => IncludeAll
        ? new UsaceIencFeedSource(id, name, CatalogUri, new UsaceIencFilter())
        : new UsaceIencFeedSource(id, DescribeSelection() ?? name, CatalogUri, CurrentFilter);
}
