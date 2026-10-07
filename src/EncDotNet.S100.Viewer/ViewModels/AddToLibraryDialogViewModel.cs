using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Manifests;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Collections.Usace;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>What an <see cref="AddToLibraryDialogViewModel"/> is adding.</summary>
internal enum AddToLibraryKind
{
    /// <summary>A local folder (scanned recursively).</summary>
    Folder,

    /// <summary>A single exchange set: folder, ZIP or catalogue file.</summary>
    ExchangeSet,

    /// <summary>An S-128 catalogue file.</summary>
    S128Catalogue,

    /// <summary>A scope of the NOAA ENC product catalogue feed.</summary>
    NoaaFeed,

    /// <summary>A scope of the USACE Inland ENC product catalogue feed (issue #670).</summary>
    UsaceFeed,

    /// <summary>Some or all entries of a community chart list (<c>chartcatalogs</c> format; issue #670).</summary>
    CommunityFeed,

    /// <summary>Some or all products of an S-100 feed, e.g. one served by <c>s100 feed serve</c> (issue #680).</summary>
    S100Feed,

    /// <summary>Some or all groups of a local collection manifest (<c>*.s100collection.json</c>).</summary>
    LocalManifest,

    /// <summary>Some regions and areas of a remote S-100 exchange catalogue, e.g. NOAA's S-102 on AWS (issue #685).</summary>
    S100Catalogue,

    /// <summary>Some models of an S-100 forecast feed, e.g. NOAA's S-111 on AWS (issue #685).</summary>
    S100Forecast,

    /// <summary>Some or all products of a SECOM service, read anonymously (issue #804).</summary>
    Secom,
}

/// <summary>A titled group of selectable facet values (one tab in the dialog).</summary>
internal sealed class FacetGroupViewModel : ViewModelBase
{
    private readonly Func<IEnumerable<FacetOptionViewModel>> _all;
    private int _selectedCount;

    /// <param name="title">The group title (e.g. "States", "Rivers").</param>
    /// <param name="options">The group's values as shown (a community list's are filtered).</param>
    /// <param name="all">Every value, shown or not; defaults to <paramref name="options"/>.</param>
    public FacetGroupViewModel(
        string title, ObservableCollection<FacetOptionViewModel> options, Func<IEnumerable<FacetOptionViewModel>>? all = null)
    {
        Title = title;
        Options = options;
        _all = all ?? (() => options);
    }

    /// <summary>The group title (e.g. "States", "Rivers").</summary>
    public string Title { get; }

    /// <summary>What the group stands for, when it is a value itself (a remote catalogue's region folder).</summary>
    public string? Key { get; init; }

    /// <summary>The group's values as shown.</summary>
    public ObservableCollection<FacetOptionViewModel> Options { get; }

    /// <summary>Every value of the group, including any hidden by a filter.</summary>
    public IEnumerable<FacetOptionViewModel> AllOptions => _all();

    /// <summary>How many of the group's values are ticked (shown or not).</summary>
    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>True when any value is ticked (the tab shows a count badge).</summary>
    public bool HasSelection => _selectedCount > 0;

    internal void Refresh() => SelectedCount = _all().Count(o => o.IsSelected);
}

/// <summary>
/// View model for the "Add to Library" dialog: confirms which collection a
/// new source goes into (a new one, named, or an existing one) and, for an
/// online feed, which part to include: NOAA ENC by state, Coast Guard district
/// or region; USACE Inland ENC by river; a community chart list by entry
/// (searchable, as lists run to over a thousand). Nothing is loaded or downloaded
/// except the feed's catalogue itself.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel : ViewModelBase
{
    private readonly CollectionLibrary _library;
    private readonly Func<Uri, CancellationToken, Task<NoaaEncProductCatalog>>? _loadCatalog;
    private readonly Func<Uri, CancellationToken, Task<UsaceIencProductCatalog>>? _loadUsaceCatalog;
    private readonly Func<Uri, CancellationToken, Task<ChartCatalogsProductCatalog>>? _loadCommunityCatalog;
    private readonly Func<Uri, CancellationToken, Task<S100FeedDocument>>? _loadS100Feed;
    private S100FeedDocument? _s100Feed;
    private readonly TimeProvider _time;
    private readonly List<FacetOptionViewModel> _allCharts = [];
    private ChartCatalogsProductCatalog? _communityCatalog;
    private string _chartSearchText = string.Empty;
    private KnownCatalogueSource? _known;
    private DateOnly? _catalogueDate;
    private IReadOnlyList<FacetGroupViewModel> _facetGroups = [];
    private FacetGroupViewModel? _selectedFacetGroup;
    private bool _includeAll = true;
    private bool _nameEdited;

    private AddToLibraryKind _kind;
    private string? _path;
    private bool _createNew = true;
    private string _newCollectionName = string.Empty;
    private LibraryCollection? _selectedCollection;
    private bool _isLoading;
    private string? _loadError;
    private NoaaEncProductCatalog? _catalog;
    private UsaceIencProductCatalog? _usaceCatalog;
    private string _selectionSummary = string.Empty;

    public AddToLibraryDialogViewModel(
        CollectionLibrary library,
        Func<Uri, CancellationToken, Task<NoaaEncProductCatalog>>? loadNoaaCatalog,
        Func<Uri, CancellationToken, Task<UsaceIencProductCatalog>>? loadUsaceCatalog = null,
        TimeProvider? timeProvider = null,
        Func<Uri, CancellationToken, Task<ChartCatalogsProductCatalog>>? loadCommunityCatalog = null,
        Func<Uri, CancellationToken, Task<S100FeedDocument>>? loadS100Feed = null,
        Func<Uri, CancellationToken, Task<RemoteS100Catalogue>>? loadS100Catalogue = null,
        Func<RemoteS100Catalogue, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<Uri, S3Object>?>>? listS100Folders = null,
        Func<Uri, IReadOnlyList<ForecastModel>, CancellationToken, Task<IReadOnlyList<ForecastModelSummary>>>? loadForecastModels = null,
        Func<Uri, string?, CancellationToken, Task<SecomServiceDescription>>? describeSecom = null,
        Func<GeoBounds?>? currentMapView = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _loadCatalog = loadNoaaCatalog;
        _loadUsaceCatalog = loadUsaceCatalog;
        _loadCommunityCatalog = loadCommunityCatalog;
        _loadS100Feed = loadS100Feed;
        _loadS100Catalogue = loadS100Catalogue;
        _listS100Folders = listS100Folders;
        _loadForecastModels = loadForecastModels;
        _describeSecom = describeSecom;
        _currentMapView = currentMapView;
        _time = timeProvider ?? TimeProvider.System;

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, false));
        SelectNoneCommand = new RelayCommand(ClearFacetSelection);
        ToggleShownCommand = new RelayCommand(ToggleShown);
    }

    /// <summary>Raised with <see langword="true"/> when confirmed, <see langword="false"/> when cancelled.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>The persisted collections a source can be added to.</summary>
    public IReadOnlyList<LibraryCollection> ExistingCollections { get; private set; } = [];

    /// <summary>What is being added.</summary>
    public AddToLibraryKind Kind => _kind;

    /// <summary>True when adding a NOAA feed scope.</summary>
    public bool IsNoaaFeed => _kind == AddToLibraryKind.NoaaFeed;

    /// <summary>True when adding a scope of an online feed (NOAA, USACE or a community list).</summary>
    public bool IsOnlineFeed => _kind is AddToLibraryKind.NoaaFeed or AddToLibraryKind.UsaceFeed
        or AddToLibraryKind.CommunityFeed or AddToLibraryKind.S100Feed or AddToLibraryKind.S100Catalogue
        or AddToLibraryKind.S100Forecast or AddToLibraryKind.Secom;

    /// <summary>True when the feed's values can be filtered by text (community lists).</summary>
    public bool IsSearchable => _kind is AddToLibraryKind.CommunityFeed or AddToLibraryKind.LocalManifest;

    /// <summary>The placeholder of the filter box.</summary>
    public string SearchPlaceholder => IsManifest ? Strings.Manifest_FilterPlaceholder : Strings.Wizard_FilterDownloads;

    /// <summary>The facet tabs for the current feed.</summary>
    public IReadOnlyList<FacetGroupViewModel> FacetGroups => _facetGroups;

    /// <summary>True when the feed has more than one facet group, shown as tabs (a remote catalogue's regions are a list instead).</summary>
    public bool HasFacetTabs => _facetGroups.Count > 1 && !IsS100Catalogue;

    /// <summary>True when the one facet group's title stands in for tabs (a community list shows its filter instead).</summary>
    public bool ShowsGroupTitle => _facetGroups.Count == 1 && (!IsSearchable || IsManifest);

    /// <summary>The facet tab being shown.</summary>
    public FacetGroupViewModel? SelectedFacetGroup
    {
        get => _selectedFacetGroup;
        set
        {
            if (SetProperty(ref _selectedFacetGroup, value))
            {
                OnPropertyChanged(nameof(ToggleShownText));
                OnRegionShown();
            }
        }
    }

    private IReadOnlyList<FacetGroupViewModel> CreateFacetGroups() => _kind switch
    {
        AddToLibraryKind.NoaaFeed =>
        [
            new(Strings.Library_NoaaStates, States),
            new(Strings.Library_NoaaDistricts, CoastGuardDistricts),
            new(Strings.Library_NoaaRegions, Regions),
        ],
        AddToLibraryKind.UsaceFeed => [new(Strings.Library_UsaceRivers, Rivers)],
        AddToLibraryKind.CommunityFeed => [new(Strings.Library_CommunityCharts, Charts, () => _allCharts)],
        AddToLibraryKind.S100Feed or AddToLibraryKind.Secom => [new(Strings.Library_FeedProducts, Products)],
        AddToLibraryKind.LocalManifest => [new(Strings.Manifest_GroupsTitle, Groups, () => _allGroups)],
        AddToLibraryKind.S100Forecast => [new(Strings.Wizard_ForecastModelsTitle, ForecastModels)],
        _ => [],
    };

    /// <summary>The dialog title.</summary>
    public string Title => _known?.Name ?? _kind switch
    {
        AddToLibraryKind.NoaaFeed => Strings.Library_AddNoaaTitle,
        AddToLibraryKind.UsaceFeed => Strings.Library_AddUsaceTitle,
        AddToLibraryKind.LocalManifest => IsEditing ? Strings.Manifest_ChooseGroupsTitle : Strings.Manifest_DialogTitle,
        _ => Strings.Library_AddTitle,
    };

    /// <summary>The path being added, or the feed URL.</summary>
    public string SourceDescription => _kind switch
    {
        AddToLibraryKind.NoaaFeed or AddToLibraryKind.UsaceFeed or AddToLibraryKind.CommunityFeed or AddToLibraryKind.S100Feed
            or AddToLibraryKind.S100Catalogue or AddToLibraryKind.S100Forecast or AddToLibraryKind.Secom
            => CatalogUri.AbsoluteUri,
        _ => _path ?? string.Empty,
    };

    /// <summary>The online catalogue being read: the known source's, else the feed's default.</summary>
    public Uri CatalogUri => _known?.CatalogUri ?? (_kind == AddToLibraryKind.UsaceFeed
        ? UsaceIencFeedSource.RiversCatalogUri
        : NoaaEncFeedSource.DefaultCatalogUri);

    /// <summary>"Catalogue dated 2026-09-17", once the catalogue is loaded and declares a date.</summary>
    public string? CatalogueDateText => _catalogueDate is { } date
        ? string.Format(CultureInfo.CurrentCulture, Strings.Library_CatalogueDatedFormat, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        : null;

    /// <summary>True when the loaded catalogue is more than a year old (it may no longer be maintained).</summary>
    public bool IsCatalogueStale =>
        _catalogueDate is { } date && DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).DayNumber - date.DayNumber > 365;

    /// <summary>True to create a new collection; false to add to <see cref="SelectedCollection"/>.</summary>
    public bool CreateNew
    {
        get => _createNew;
        set
        {
            if (SetProperty(ref _createNew, value))
            {
                OnPropertyChanged(nameof(AddToExisting));
                OnPropertyChanged(nameof(TargetDescription));
                RefreshCanConfirm();
            }
        }
    }

    /// <summary>The inverse of <see cref="CreateNew"/>, for radio-button binding.</summary>
    public bool AddToExisting
    {
        get => !_createNew;
        set => CreateNew = !value;
    }

    /// <summary>True when there is at least one existing collection to add to.</summary>
    public bool HasExistingCollections => ExistingCollections.Count > 0;

    /// <summary>
    /// The name of the new collection. Typing one selects "New collection"
    /// and stops the name following the selection (until it is cleared).
    /// </summary>
    public string NewCollectionName
    {
        get => _newCollectionName;
        set
        {
            if (!SetProperty(ref _newCollectionName, value ?? string.Empty))
                return;

            _nameEdited = !string.IsNullOrWhiteSpace(_newCollectionName);
            OnPropertyChanged(nameof(IsNameEdited));
            OnPropertyChanged(nameof(NameHint));
            CreateNew = true;
            RefreshCanConfirm();
        }
    }

    /// <summary>True once the user has typed their own collection name.</summary>
    public bool IsNameEdited => _nameEdited;

    /// <summary>Whether the suggested name follows the selection, or the user's name is kept.</summary>
    public string NameHint => IsManifest
        ? _nameEdited ? Strings.Manifest_NameKept : Strings.Manifest_NameFollows
        : _nameEdited ? Strings.Wizard_NameKept : Strings.Wizard_NameFollows;

    /// <summary>The existing collection to add to; picking one selects "Existing collection".</summary>
    public LibraryCollection? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (!SetProperty(ref _selectedCollection, value))
                return;

            if (value is not null)
                CreateNew = false;
            OnPropertyChanged(nameof(TargetDescription));
            RefreshCanConfirm();
        }
    }

    /// <summary>"New collection", or the name of the existing collection the source goes into.</summary>
    public string TargetDescription => _createNew
        ? Strings.Library_NewCollection
        : _selectedCollection?.Definition.Name ?? Strings.Library_ExistingCollection;

    /// <summary>True while the NOAA catalogue is being fetched.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCanConfirm();
                OnScopeChanged();
            }
        }
    }

    /// <summary>Why the NOAA catalogue could not be fetched, if it failed.</summary>
    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
                OnPropertyChanged(nameof(HasLoadError));
        }
    }

    /// <summary>True when <see cref="LoadError"/> is set.</summary>
    public bool HasLoadError => _loadError is not null;

    /// <summary>States present in the NOAA catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> States { get; } = [];

    /// <summary>Coast Guard districts present in the NOAA catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> CoastGuardDistricts { get; } = [];

    /// <summary>Regions present in the NOAA catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> Regions { get; } = [];

    /// <summary>Rivers present in the USACE catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> Rivers { get; } = [];

    /// <summary>Products present in an S-100 feed.</summary>
    public ObservableCollection<FacetOptionViewModel> Products { get; } = [];

    /// <summary>The community list's entries matching <see cref="ChartSearchText"/>.</summary>
    public ObservableCollection<FacetOptionViewModel> Charts { get; } = [];

    /// <summary>Filters <see cref="Charts"/> by label or number; selections outside the filter are kept.</summary>
    public string ChartSearchText
    {
        get => _chartSearchText;
        set
        {
            if (SetProperty(ref _chartSearchText, value ?? string.Empty))
                ShowMatchingCharts();
        }
    }

    /// <summary>"N cells · X MB" for the current NOAA selection.</summary>
    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    /// <summary>
    /// True to include the whole catalogue whatever is ticked (the default);
    /// false to include only the ticked values. Ticking a value clears it;
    /// setting it keeps the ticks for switching back.
    /// </summary>
    public bool IncludeAll
    {
        get => _includeAll;
        set
        {
            if (!SetProperty(ref _includeAll, value))
                return;

            OnPropertyChanged(nameof(OnlySelected));
            UpdateSelection();
            OnScopeChanged();
        }
    }

    /// <summary>The inverse of <see cref="IncludeAll"/>, for radio-button binding.</summary>
    public bool OnlySelected
    {
        get => !_includeAll;
        set => IncludeAll = !value;
    }

    /// <summary>True once the catalogue has been read.</summary>
    public bool IsLoaded => _kind switch
    {
        AddToLibraryKind.NoaaFeed => _catalog is not null,
        AddToLibraryKind.UsaceFeed => _usaceCatalog is not null,
        AddToLibraryKind.CommunityFeed => _communityCatalog is not null,
        AddToLibraryKind.S100Feed => _s100Feed is not null,
        AddToLibraryKind.LocalManifest => _manifest is not null,
        AddToLibraryKind.S100Catalogue => _s100Catalogue is not null,
        AddToLibraryKind.S100Forecast => _forecastModels is not null,
        AddToLibraryKind.Secom => _secom is not null,
        _ => false,
    };

    /// <summary>True when the catalogue has been read and it lists more than one choice.</summary>
    public bool ShowsChoices => IsLoaded && !_isLoading && !IsSingleEntry;

    /// <summary>Every facet value, shown or not, across all groups.</summary>
    private IEnumerable<FacetOptionViewModel> AllOptions => _facetGroups.SelectMany(g => g.AllOptions);

    /// <summary>How many facet values are ticked, across all groups.</summary>
    public int SelectedCount => AllOptions.Count(o => o.IsSelected);

    /// <summary>True when any facet value is ticked.</summary>
    public bool HasSelection => AllOptions.Any(o => o.IsSelected);

    /// <summary>
    /// True when the loaded catalogue lists exactly one download (the USACE
    /// buoy overlay, a one-entry community list): there is nothing to choose.
    /// </summary>
    public bool IsSingleEntry => SingleEntry is not null;

    /// <summary>The catalogue's only download, when it lists just one; otherwise <see langword="null"/>.</summary>
    public FacetOptionViewModel? SingleEntry
    {
        get
        {
            string Size(long? bytes) => bytes is { } b ? LibraryItemViewModel.FormatBytes(b) : string.Empty;
            return _kind switch
            {
                AddToLibraryKind.NoaaFeed when _catalog?.Cells is [var cell] =>
                    new(cell.Name, cell.LongName ?? cell.Name, Size(cell.ZipSize)),
                AddToLibraryKind.UsaceFeed when _usaceCatalog?.Cells is [var cell] =>
                    new(cell.Name, cell.Name, Size(cell.ZipSize)),
                AddToLibraryKind.CommunityFeed when _communityCatalog is not null && _allCharts is [var chart] => chart,
                AddToLibraryKind.S100Feed when _s100Feed?.Items is [var item] =>
                    new(item.Key, item.Name ?? item.Key, Size((item.Location as RemoteItemLocation)?.SizeBytes)),
                AddToLibraryKind.S100Catalogue when _s100Catalogue?.Items is [var item] =>
                    new(item.Key, item.Title ?? item.Name, string.Empty),
                AddToLibraryKind.S100Forecast when _forecastModels is [var model] => new(
                    model.Model.Id,
                    model.Model.Name,
                    string.Join(" · ", new[]
                    {
                        string.Format(CultureInfo.CurrentCulture, Strings.Wizard_TilesFormat, model.TileCount),
                        model.TileBytes is { } bytes ? LibraryItemViewModel.FormatBytes(bytes) : null,
                    }.OfType<string>())),
                _ => null,
            };
        }
    }

    /// <summary>"12,345 cells · 1.2 GB" (or "N downloads · sizes unknown") for the whole catalogue.</summary>
    public string EverythingSummary
    {
        get
        {
            switch (_kind)
            {
                case AddToLibraryKind.NoaaFeed when _catalog is not null:
                    var (count, bytes) = NoaaEncFacets.Summarize(_catalog, new NoaaEncFilter());
                    return CellsSummary(count, bytes);
                case AddToLibraryKind.UsaceFeed when _usaceCatalog is not null:
                    return CellsSummary(_usaceCatalog.Cells.Count, _usaceCatalog.Cells.Sum(c => c.ZipSize ?? 0));
                case AddToLibraryKind.S100Feed when _s100Feed is not null:
                    return CellsSummary(_s100Feed.Items.Count,
                        _s100Feed.Items.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0));
                case AddToLibraryKind.CommunityFeed when _communityCatalog is not null:
                    return string.Format(CultureInfo.CurrentCulture, Strings.Wizard_EverythingDownloadsFormat, _allCharts.Count);
                case AddToLibraryKind.LocalManifest when _manifest is not null:
                    return ManifestEverythingSummary;
                case AddToLibraryKind.S100Catalogue when _s100Catalogue is not null:
                    return S100EverythingSummary;
                case AddToLibraryKind.S100Forecast when _forecastModels is not null:
                    return ForecastEverythingSummary;
                case AddToLibraryKind.Secom when _secom is not null:
                    return SecomEverythingSummary;
                default:
                    return string.Empty;
            }

            static string CellsSummary(int count, long bytes) => string.Format(
                CultureInfo.CurrentCulture, Strings.Wizard_EverythingCellsFormat, count, LibraryItemViewModel.FormatBytes(bytes));
        }
    }

    /// <summary>The "Only what I select" sub-line: the selection summary, or "Nothing selected yet".</summary>
    public string OnlySelectedSummary => HasSelection ? _selectionSummary : Strings.Wizard_NothingSelected;

    /// <summary>The summary under the facet list.</summary>
    public string ScopeSummary =>
        IsManifest ? ManifestScopeSummary
        : (IsS100Catalogue || IsS100Forecast) && (_includeAll || HasSelection)
            ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_NothingDownloadsFormat, _selectionSummary)
        : _includeAll
            ? HasSelection
                ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_KeptPicksFormat, SelectedCount)
                : _selectionSummary
            : HasSelection ? _selectionSummary : Strings.Wizard_TickAtLeastOne;

    /// <summary>True when <see cref="ScopeSummary"/> asks the user to tick something.</summary>
    public bool IsScopeSummaryWarning => IsManifest ? IsManifestSummaryWarning : !_includeAll && !HasSelection;

    /// <summary>What is included: "Everything", "Its one download", or the selection ("Alaska, Hawaii").</summary>
    public string ScopeDescription =>
        IsSingleEntry ? Strings.Wizard_SingleEntryValue
        : _includeAll ? Strings.Wizard_Everything
        : DescribeSelection() ?? Strings.Wizard_NothingSelected;

    /// <summary>True when the scope step is complete: the catalogue is read and something is included.</summary>
    public bool CanContinueFromScope => IsLoaded && !_isLoading && (_includeAll || IsSingleEntry || HasSelection);

    /// <summary>The catalogue's name.</summary>
    public string CatalogueName => _known?.Name ?? Title;

    /// <summary>"host · catalogue dated 2026-09-17", or just the host until the catalogue is read.</summary>
    public string CatalogueDetail => _catalogueDate is { } date
        ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_DatedFormat, CatalogUri.Host,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        : CatalogUri.Host;

    /// <summary>" · over a year old; may no longer be maintained" for a stale catalogue, else empty.</summary>
    public string CatalogueStaleSuffix => IsCatalogueStale ? Strings.Wizard_StaleSuffix : string.Empty;

    /// <summary>The review's "Includes" line.</summary>
    public string ReviewIncludes =>
        _includeAll || IsSingleEntry
            ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_Review_EverythingFormat, EverythingSummary)
            : string.Format(CultureInfo.CurrentCulture, Strings.Wizard_Review_SelectionFormat, DescribeSelection(), _selectionSummary);

    private KnownCatalogueCoverage Coverage => _known?.Coverage ?? _kind switch
    {
        AddToLibraryKind.UsaceFeed => KnownCatalogueCoverage.BoundingBoxes,
        AddToLibraryKind.CommunityFeed => KnownCatalogueCoverage.None,
        _ => KnownCatalogueCoverage.Polygons,
    };

    /// <summary>The review's "Shown on the map" line.</summary>
    public string ReviewCoverage => Coverage switch
    {
        KnownCatalogueCoverage.Polygons => Strings.Wizard_Review_CoveragePolygons,
        KnownCatalogueCoverage.BoundingBoxes => Strings.Wizard_Review_CoverageBoxes,
        _ => Strings.Wizard_Review_CoverageNone,
    };

    /// <summary>True when nothing is shown on the map until an entry is downloaded.</summary>
    public bool IsReviewCoverageWarning => Coverage == KnownCatalogueCoverage.None;

    private bool HasEditions => _known?.Editions ?? _kind != AddToLibraryKind.CommunityFeed;

    /// <summary>The review's "Updates" line.</summary>
    public string ReviewUpdates => IsS100Forecast ? Strings.Wizard_Review_UpdatesForecast
        : HasEditions ? Strings.Wizard_Review_UpdatesDetected : Strings.Wizard_Review_UpdatesNotDetected;

    /// <summary>True when new editions are not detected.</summary>
    public bool IsReviewUpdatesWarning => !HasEditions;

    /// <summary>True when the review shows a "Use" row: the provider marks the data as not for navigation.</summary>
    public bool HasReviewUse => _known?.NotForNavigation == true;

    /// <summary>"Select all", "Select shown" (when filtered) or "Deselect shown", for the shown tab.</summary>
    public string ToggleShownText =>
        _selectedFacetGroup is { Options.Count: > 0 } group && group.Options.All(o => o.IsSelected)
            ? Strings.Wizard_DeselectShown
            : IsSearchable && _chartSearchText.Trim().Length > 0 ? Strings.Wizard_SelectShown : Strings.Wizard_SelectAll;

    /// <summary>Ticks every shown value of the shown tab, or unticks them when all are ticked.</summary>
    public ICommand ToggleShownCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Clears every NOAA facet selection (meaning "all cells").</summary>
    public ICommand SelectNoneCommand { get; }

    /// <summary>
    /// Prepares the dialog for adding <paramref name="path"/> (or, for
    /// <see cref="AddToLibraryKind.NoaaFeed"/>, the feed), preselecting
    /// <paramref name="targetCollectionId"/> when it names a collection.
    /// </summary>
    public void Initialize(AddToLibraryKind kind, string? path, Guid? targetCollectionId)
        => Initialize(kind, path, targetCollectionId, known: null);

    /// <summary>
    /// Prepares the dialog for adding a scope of the known online catalogue
    /// <paramref name="known"/> (issue #670).
    /// </summary>
    public void Initialize(KnownCatalogueSource known, Guid? targetCollectionId)
    {
        ArgumentNullException.ThrowIfNull(known);
        var kind = known.Format switch
        {
            KnownCatalogueFormat.UsaceIenc => AddToLibraryKind.UsaceFeed,
            KnownCatalogueFormat.ChartCatalogs => AddToLibraryKind.CommunityFeed,
            KnownCatalogueFormat.S100Feed => AddToLibraryKind.S100Feed,
            KnownCatalogueFormat.S100ExchangeCatalogue => AddToLibraryKind.S100Catalogue,
            KnownCatalogueFormat.S100ForecastModels => AddToLibraryKind.S100Forecast,
            KnownCatalogueFormat.Secom => AddToLibraryKind.Secom,
            _ => AddToLibraryKind.NoaaFeed,
        };
        Initialize(kind, null, targetCollectionId, known);
    }

    private void Initialize(AddToLibraryKind kind, string? path, Guid? targetCollectionId, KnownCatalogueSource? known)
    {
        _kind = kind;
        _path = path;
        _known = known;
        ResetManifest();
        ResetS100Catalogue();
        _forecastModels = null;
        _secom = null;
        _secomArea = null;
        _secomSync = false;
        _secomSyncEdited = false;
        _selectedForecastShape = null;
        ForecastModels.Clear();
        _catalogueDate = null;
        _includeAll = true;
        _nameEdited = false;
        _facetGroups = CreateFacetGroups();
        _selectedFacetGroup = _facetGroups.FirstOrDefault();
        ExistingCollections = _library.Collections.Where(c => !c.IsSession).ToArray();
        _selectedCollection = targetCollectionId is { } id
            ? ExistingCollections.FirstOrDefault(c => c.Id == id)
            : ExistingCollections.FirstOrDefault();
        _createNew = targetCollectionId is null || _selectedCollection is null;
        _newCollectionName = FeedName ?? DefaultName(path);

        OnPropertyChanged(string.Empty);
        RefreshCanConfirm();
    }

    /// <summary>Fetches the feed's catalogue and populates the facet lists.</summary>
    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_kind == AddToLibraryKind.UsaceFeed)
        {
            await LoadUsaceCatalogAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_kind == AddToLibraryKind.CommunityFeed)
        {
            await LoadCommunityCatalogAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_kind == AddToLibraryKind.S100Feed)
        {
            await LoadS100FeedAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_kind == AddToLibraryKind.LocalManifest)
        {
            await LoadManifestAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_kind == AddToLibraryKind.S100Catalogue)
        {
            await LoadS100CatalogueAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_kind == AddToLibraryKind.S100Forecast)
        {
            await LoadForecastModelsAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_kind == AddToLibraryKind.Secom)
        {
            await LoadSecomAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_loadCatalog is null || _kind != AddToLibraryKind.NoaaFeed)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _catalog = await _loadCatalog(CatalogUri, cancellationToken).ConfigureAwait(true);
            SetCatalogueDate(_catalog.Header.ValidAt is { } valid ? DateOnly.FromDateTime(valid.UtcDateTime) : null);
            var facets = NoaaEncFacets.Compute(_catalog);

            Populate(States, facets.States.OrderBy(f => UsStateNames.SortKey(f.Value), StringComparer.CurrentCulture),
                f => UsStateNames.Describe(f.Value));
            Populate(CoastGuardDistricts, facets.CoastGuardDistricts.OrderBy(f => int.Parse(f.Value, CultureInfo.InvariantCulture)),
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_DistrictFormat, f.Value));
            Populate(Regions, facets.Regions.OrderBy(f => int.Parse(f.Value, CultureInfo.InvariantCulture)),
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_RegionFormat, f.Value));
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadUsaceCatalogAsync(CancellationToken cancellationToken)
    {
        if (_loadUsaceCatalog is null)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _usaceCatalog = await _loadUsaceCatalog(CatalogUri, cancellationToken).ConfigureAwait(true);
            SetCatalogueDate(_usaceCatalog.CreatedOn);
            Populate(Rivers, EncDotNet.S100.Collections.Indexing.UsaceIencFeedIndexer.Rivers(_usaceCatalog), f => f.Value);
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCommunityCatalogAsync(CancellationToken cancellationToken)
    {
        if (_loadCommunityCatalog is null)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _communityCatalog = await _loadCommunityCatalog(CatalogUri, cancellationToken).ConfigureAwait(true);
            SetCatalogueDate(_communityCatalog.ValidAt is { } valid ? DateOnly.FromDateTime(valid.UtcDateTime) : null);

            foreach (var option in _allCharts)
                option.PropertyChanged -= OnFacetChanged;
            _allCharts.Clear();
            foreach (var chart in ChartCatalogsFeedIndexer.Selected(_communityCatalog, ChartCatalogsFilter.All))
            {
                var detail = chart.PublishedAt is { } published
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_PublishedFormat,
                        published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : string.Empty;
                var option = new FacetOptionViewModel(chart.Number, chart.Title ?? chart.Number, detail);
                option.PropertyChanged += OnFacetChanged;
                _allCharts.Add(option);
            }

            ShowMatchingCharts();
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadS100FeedAsync(CancellationToken cancellationToken)
    {
        if (_loadS100Feed is null)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _s100Feed = await _loadS100Feed(CatalogUri, cancellationToken).ConfigureAwait(true);
            SetCatalogueDate(DateOnly.FromDateTime(_s100Feed.GeneratedAt.UtcDateTime));
            Populate(Products, S100FeedIndexer.Products(_s100Feed), f => f.Value,
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_FeedFacetDetailFormat,
                    f.CellCount, LibraryItemViewModel.FormatBytes(f.TotalBytes)));
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException
            or NotSupportedException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ShowMatchingCharts()
    {
        var text = _chartSearchText.Trim();
        var (shown, all) = IsManifest ? (Groups, _allGroups) : (Charts, _allCharts);
        shown.Clear();
        foreach (var option in all)
        {
            if (text.Length == 0
                || option.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || option.Value.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                shown.Add(option);
            }
        }

        OnPropertyChanged(nameof(ToggleShownText));
    }

    private void SetCatalogueDate(DateOnly? date)
    {
        _catalogueDate = date;
        OnPropertyChanged(nameof(CatalogueDateText));
        OnPropertyChanged(nameof(CatalogueDetail));
        OnPropertyChanged(nameof(IsCatalogueStale));
        OnPropertyChanged(nameof(CatalogueStaleSuffix));
    }

    /// <summary>The USACE filter for the current river selection.</summary>
    public UsaceIencFilter CurrentUsaceFilter => new()
    {
        Rivers = Rivers.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
    };

    /// <summary>The S-100 feed filter for the current product selection.</summary>
    public S100FeedFilter CurrentS100FeedFilter => new()
    {
        ProductSpecs = Products.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
    };

    /// <summary>The community-list filter for the current entry selection.</summary>
    public ChartCatalogsFilter CurrentCommunityFilter => new()
    {
        Charts = _allCharts.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
    };

    /// <summary>The default collection name for an online feed, or <see langword="null"/> for local sources.</summary>
    private string? FeedName => _known?.Name ?? _kind switch
    {
        AddToLibraryKind.NoaaFeed => Strings.Library_NoaaFeed,
        AddToLibraryKind.UsaceFeed => Strings.Library_UsaceFeed,
        AddToLibraryKind.LocalManifest => ManifestBaseName,
        _ => null,
    };

    /// <summary>The NOAA filter for the current facet selection.</summary>
    public NoaaEncFilter CurrentFilter => new()
    {
        States = States.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
        CoastGuardDistricts = CoastGuardDistricts.Where(o => o.IsSelected)
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture)).ToArray(),
        Regions = Regions.Where(o => o.IsSelected)
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture)).ToArray(),
    };

    private bool CanConfirm =>
        IsManifest ? CanConfirmManifest
        : (_createNew ? !string.IsNullOrWhiteSpace(_newCollectionName) : _selectedCollection is not null)
        && _kind switch
        {
            AddToLibraryKind.NoaaFeed => _catalog is not null && !_isLoading,
            AddToLibraryKind.UsaceFeed => _usaceCatalog is not null && !_isLoading,
            AddToLibraryKind.CommunityFeed => _communityCatalog is not null && !_isLoading,
            AddToLibraryKind.S100Feed => _s100Feed is not null && !_isLoading,
            AddToLibraryKind.S100Catalogue => _s100Catalogue is not null && !_isLoading,
            AddToLibraryKind.S100Forecast => _forecastModels is not null && !_isLoading,
            AddToLibraryKind.Secom => _secom is not null && !_isLoading,
            _ => !string.IsNullOrEmpty(_path),
        };

    private void RefreshCanConfirm() => ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();

    private void Confirm()
    {
        if (!CanConfirm)
            return;

        if (_editing is { } editing)
        {
            _library.UpdateSource(editing.CollectionId, BuildSource());
            Closed?.Invoke(this, true);
            return;
        }

        var source = BuildSource();
        if (_createNew)
            _library.AddCollection(_newCollectionName, [source]);
        else
            _library.AddSources(_selectedCollection!.Id, [source]);

        Closed?.Invoke(this, true);
    }

    /// <summary>Builds the source the dialog describes.</summary>
    internal CollectionSource BuildSource()
    {
        var id = Guid.NewGuid();
        return _kind switch
        {
            AddToLibraryKind.Folder => new LocalFolderSource(id, null, _path!),
            AddToLibraryKind.ExchangeSet => new ExchangeSetSource(id, null, _path!),
            AddToLibraryKind.S128Catalogue => new S128CatalogueSource(id, null, _path!),
            AddToLibraryKind.LocalManifest => BuildManifestSource(id),
            AddToLibraryKind.UsaceFeed => _includeAll
                ? new UsaceIencFeedSource(id, FeedName, CatalogUri, new UsaceIencFilter())
                : new UsaceIencFeedSource(id, DescribeUsaceFilter(CurrentUsaceFilter) ?? FeedName, CatalogUri, CurrentUsaceFilter),
            AddToLibraryKind.CommunityFeed => _includeAll
                ? new ChartCatalogsFeedSource(id, FeedName, CatalogUri, ChartCatalogsFilter.All)
                : new ChartCatalogsFeedSource(id, DescribeCommunitySelection() ?? FeedName, CatalogUri, CurrentCommunityFilter),
            AddToLibraryKind.S100Feed => _includeAll
                ? new S100FeedSource(id, FeedName, CatalogUri, new S100FeedFilter())
                : new S100FeedSource(id, DescribeProducts(CurrentS100FeedFilter) ?? FeedName, CatalogUri, CurrentS100FeedFilter),
            AddToLibraryKind.S100Forecast => BuildForecastSource(id),
            AddToLibraryKind.Secom => BuildSecomSource(id),
            AddToLibraryKind.S100Catalogue => new S100CatalogueFeedSource(
                id, DescribeS100Selection(CurrentS100CatalogueFilter) ?? FeedName, CatalogUri, CurrentS100CatalogueFilter),
            _ => _includeAll
                ? new NoaaEncFeedSource(id, FeedName, CatalogUri, new NoaaEncFilter())
                : new NoaaEncFeedSource(id, DescribeFilter(CurrentFilter) ?? FeedName, CatalogUri, CurrentFilter),
        };
    }

    /// <summary>Describes the ticked values ("Alaska, Hawaii"), or <see langword="null"/> when nothing is ticked.</summary>
    private string? DescribeSelection() => _kind switch
    {
        AddToLibraryKind.NoaaFeed => DescribeFilter(CurrentFilter),
        AddToLibraryKind.UsaceFeed => DescribeUsaceFilter(CurrentUsaceFilter),
        AddToLibraryKind.CommunityFeed => DescribeCommunitySelection(),
        AddToLibraryKind.S100Feed => DescribeProducts(CurrentS100FeedFilter),
        AddToLibraryKind.LocalManifest => DescribeManifestSelection(),
        AddToLibraryKind.S100Catalogue => DescribeS100Selection(CurrentS100CatalogueFilter),
        AddToLibraryKind.S100Forecast => DescribeForecastSelection(),
        AddToLibraryKind.Secom => DescribeSecomProducts(CurrentSecomFilter),
        _ => null,
    };

    private void Populate(
        ObservableCollection<FacetOptionViewModel> target,
        IEnumerable<CatalogFacetValue> values,
        Func<CatalogFacetValue, string> label,
        Func<CatalogFacetValue, string>? detail = null)
    {
        foreach (var option in target)
            option.PropertyChanged -= OnFacetChanged;
        target.Clear();

        foreach (var value in values)
        {
            var option = detail is null
                ? new FacetOptionViewModel(value, label(value))
                : new FacetOptionViewModel(value.Value, label(value), detail(value));
            option.PropertyChanged += OnFacetChanged;
            target.Add(option);
        }
    }

    private void OnFacetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FacetOptionViewModel.IsSelected))
            return;

        // Ticking a value means "only what I select"; unticking leaves the choice alone.
        if (sender is FacetOptionViewModel { IsSelected: true })
            IncludeAll = false;
        UpdateSelection();
        OnScopeChanged();
    }

    private void ClearFacetSelection()
    {
        foreach (var option in States.Concat(CoastGuardDistricts).Concat(Regions).Concat(Rivers).Concat(_allCharts).Concat(Products)
            .Concat(_allGroups).Concat(ForecastModels).Concat(IsS100Catalogue ? AllOptions.ToArray() : []))
        {
            option.IsSelected = false;
        }
    }

    private void ToggleShown()
    {
        if (_selectedFacetGroup is not { } group)
            return;

        var select = !group.Options.All(o => o.IsSelected);
        foreach (var option in group.Options.ToArray())
            option.IsSelected = select;
    }

    /// <summary>Raises change notifications for everything derived from the scope (catalogue, choice and ticks).</summary>
    private void OnScopeChanged()
    {
        foreach (var group in _facetGroups)
            group.Refresh();

        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(ShowsChoices));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleEntry));
        OnPropertyChanged(nameof(SingleEntry));
        OnPropertyChanged(nameof(EverythingSummary));
        OnPropertyChanged(nameof(OnlySelectedSummary));
        OnPropertyChanged(nameof(ScopeSummary));
        OnPropertyChanged(nameof(IsScopeSummaryWarning));
        OnPropertyChanged(nameof(ScopeDescription));
        OnPropertyChanged(nameof(CanContinueFromScope));
        OnPropertyChanged(nameof(ReviewIncludes));
        OnPropertyChanged(nameof(ToggleShownText));
        if (IsManifest)
            RaiseManifestChanged();
    }

    private void UpdateSelection()
    {
        if (_kind == AddToLibraryKind.UsaceFeed)
        {
            UpdateUsaceSelection();
            return;
        }

        if (_kind == AddToLibraryKind.CommunityFeed)
        {
            UpdateCommunitySelection();
            return;
        }

        if (_kind == AddToLibraryKind.S100Feed)
        {
            UpdateS100FeedSelection();
            return;
        }

        if (_kind == AddToLibraryKind.Secom)
        {
            UpdateSecomSelection();
            return;
        }

        if (_kind == AddToLibraryKind.LocalManifest)
        {
            UpdateManifestSelection();
            return;
        }

        if (_kind == AddToLibraryKind.S100Catalogue)
        {
            UpdateS100CatalogueSelection();
            return;
        }

        if (_kind == AddToLibraryKind.S100Forecast)
        {
            UpdateForecastSelection();
            return;
        }

        if (_catalog is null)
            return;

        var filter = CurrentFilter;
        var (count, bytes) = NoaaEncFacets.Summarize(_catalog, filter);
        SelectionSummary = string.Format(
            CultureInfo.CurrentCulture,
            filter.IsUnscoped ? Strings.Library_NoaaSelectionAllFormat : Strings.Library_NoaaSelectionFormat,
            count,
            LibraryItemViewModel.FormatBytes(bytes));

        // Follow the selection in the suggested name until the user edits it.
        FollowSelectionInName(_includeAll || filter.IsUnscoped, () => DescribeFilter(filter)!);
    }

    private void UpdateUsaceSelection()
    {
        if (_usaceCatalog is null)
            return;

        var filter = CurrentUsaceFilter;
        var selected = _usaceCatalog.Cells.Where(filter.Matches).ToArray();
        SelectionSummary = string.Format(
            CultureInfo.CurrentCulture,
            filter.IsUnscoped ? Strings.Library_NoaaSelectionAllFormat : Strings.Library_NoaaSelectionFormat,
            selected.Length,
            LibraryItemViewModel.FormatBytes(selected.Sum(c => c.ZipSize ?? 0)));

        FollowSelectionInName(_includeAll || filter.IsUnscoped, () => DescribeUsaceFilter(filter)!);
    }

    /// <summary>
    /// Keeps the suggested collection name in step with the selection
    /// ("NOAA ENC — Alaska") until the user types their own name.
    /// </summary>
    private void FollowSelectionInName(bool unscoped, Func<string> describe)
    {
        var baseName = FeedName;
        if (baseName is null || !_createNew || _nameEdited)
            return;

        var name = unscoped ? baseName : $"{baseName} — {describe()}";
        if (SetProperty(ref _newCollectionName, name, nameof(NewCollectionName)))
            RefreshCanConfirm();
    }

    private void UpdateS100FeedSelection()
    {
        if (_s100Feed is null)
            return;

        var filter = CurrentS100FeedFilter;
        var selected = _s100Feed.Items.Where(filter.Matches).ToArray();
        SelectionSummary = string.Format(
            CultureInfo.CurrentCulture,
            filter.IsUnscoped ? Strings.Library_FeedSelectionAllFormat : Strings.Library_FeedSelectionFormat,
            selected.Length,
            LibraryItemViewModel.FormatBytes(selected.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0)));

        FollowSelectionInName(_includeAll || filter.IsUnscoped, () => DescribeProducts(filter)!);
    }

    /// <summary>The selected products, or <see langword="null"/> for all (the source is then named after the catalogue).</summary>
    private static string? DescribeProducts(S100FeedFilter filter) =>
        filter.IsUnscoped ? null : string.Join(", ", filter.ProductSpecs);

    private void UpdateCommunitySelection()
    {
        if (_communityCatalog is null)
            return;

        var selected = _allCharts.Count(o => o.IsSelected);
        SelectionSummary = selected == 0
            ? string.Format(CultureInfo.CurrentCulture, Strings.Library_CommunitySelectionAllFormat, _allCharts.Count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_CommunitySelectionFormat, selected);

        FollowSelectionInName(_includeAll || selected == 0, () => DescribeCommunitySelection()!);
    }

    private string? DescribeCommunitySelection()
    {
        var labels = _allCharts.Where(o => o.IsSelected).Select(o => o.Label).ToArray();
        return labels.Length switch
        {
            0 => null,
            <= 3 => string.Join(", ", labels),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", labels.Take(2)), labels.Length - 2),
        };
    }

    private static string? DescribeUsaceFilter(UsaceIencFilter filter)
    {
        if (filter.IsUnscoped)
            return null;

        var rivers = filter.Rivers.ToArray();
        return rivers.Length <= 3
            ? string.Join(", ", rivers)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", rivers.Take(2)), rivers.Length - 2);
    }

    private string? DescribeFilter(NoaaEncFilter filter)
    {
        if (filter.IsUnscoped)
            return null;

        var parts = States.Where(o => o.IsSelected).Select(o => UsStateNames.SortKey(o.Value))
            .Concat(CoastGuardDistricts.Where(o => o.IsSelected).Select(o => o.Label))
            .Concat(Regions.Where(o => o.IsSelected).Select(o => o.Label))
            .ToArray();
        return parts.Length <= 3
            ? string.Join(", ", parts)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", parts.Take(2)), parts.Length - 2);
    }

    private static string DefaultName(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = trimmed.EndsWith(CollectionManifest.FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(trimmed)[..^CollectionManifest.FileSuffix.Length]
            : Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}

/// <summary>One selectable facet value (a NOAA state, district or region, a USACE river, or a community list entry).</summary>
internal sealed class FacetOptionViewModel : ViewModelBase
{
    private bool _isSelected;
    private string _detail;

    public FacetOptionViewModel(CatalogFacetValue value, string label)
        : this(value.Value, label, string.Format(CultureInfo.CurrentCulture, Strings.Library_FacetDetailFormat,
            value.CellCount, LibraryItemViewModel.FormatBytes(value.TotalBytes)))
    {
    }

    public FacetOptionViewModel(string value, string label, string detail)
    {
        Value = value;
        Label = label;
        _detail = detail;
    }

    /// <summary>The facet value (state code, district/region number, or river name).</summary>
    public string Value { get; }

    /// <summary>The display label.</summary>
    public string Label { get; }

    /// <summary>"N cells · X MB", or other detail (a community entry's publication date; a remote area's size once listed).</summary>
    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value ?? string.Empty);
    }

    /// <summary>A collection-manifest group's first path, as written in the manifest; otherwise <see langword="null"/>.</summary>
    public string? PathText { get; init; }

    /// <summary>"+2" when a manifest group lists more paths than <see cref="PathText"/>; otherwise <see langword="null"/>.</summary>
    public string? MorePathsText { get; init; }

    /// <summary>Every path of a manifest group, one per line, for the tooltip.</summary>
    public string? PathsTooltip { get; init; }

    /// <summary>True when some path of a manifest group does not exist on disk.</summary>
    public bool IsMissing { get; init; }

    /// <summary>True when the row shows paths (a manifest group) rather than <see cref="Detail"/>.</summary>
    public bool HasPaths => PathText is not null;

    /// <summary>True when <see cref="MorePathsText"/> is set.</summary>
    public bool HasMorePaths => MorePathsText is not null;

    /// <summary>A second line under the label (a regional forecast model's "Overlaps cbofs, dbofs"), if any.</summary>
    public string? Note { get; init; }

    /// <summary>True when <see cref="Note"/> is set.</summary>
    public bool HasNote => Note is not null;

    /// <summary>Whether the value is included.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
