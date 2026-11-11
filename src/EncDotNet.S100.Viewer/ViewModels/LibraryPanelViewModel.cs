using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Which datasets the Library list shows, by where their data is and what is new.</summary>
internal enum LibraryStateFilter
{
    /// <summary>Every dataset.</summary>
    All,

    /// <summary>On disk and current: local, loaded or loading on pan.</summary>
    Local,

    /// <summary>Downloadable, not yet downloaded.</summary>
    Online,

    /// <summary>Downloaded, with a newer edition or update available.</summary>
    Updates,
}

/// <summary>
/// View model for the Library panel (issue #655), which replaces the former
/// S-128 Catalog panel. Shows the user's dataset collections as a tree of
/// collections and sources, the indexed datasets of the selected node as a
/// filterable list, and the selected dataset's metadata — without loading any
/// dataset.
/// </summary>
internal sealed class LibraryPanelViewModel : ViewModelBase, ILibraryTapList, IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly LibraryTreeViewModel _tree;
    private readonly ILibraryLoader _loader;
    private readonly ILibraryDownloader _downloader;
    private readonly LibraryRows _rows;
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly DownloadLibraryItemsWorkflow _download;
    private readonly LibraryDetailsViewModel _details;
    private readonly LibraryTapViewModel _tap;
    private bool _availabilityRefreshPosted;
    private bool _progressRefreshPosted;
    private readonly object _gate = new();
    private readonly HashSet<(Guid Source, string Package)> _expandedPackages = [];
    private readonly Dictionary<(Guid Source, string Package), LibraryItemViewModel> _packageHeaders = [];
    private readonly HashSet<Guid> _collapsedAml = [];
    private string? _amlFilter;
    private readonly Action<Action> _dispatch;

    // Runs a coalesced refresh. Unlike _dispatch it always defers, even on the
    // UI thread, so a burst of changes raised there (a pass that evicts or loads
    // many cells) collapses into one refresh instead of one per cell.
    private readonly Action<Action> _coalesce;
    private readonly TimeProvider _time;
    private readonly ITimer? _clock;
    private readonly Services.GlobalTimeService? _viewTime;
    private readonly Func<TimeFormat>? _runFormat;
    private bool _atViewTime;
    private IReadOnlyList<LibraryItemViewModel> _textMatched = [];

    private IReadOnlyList<SourceIndex?>? _itemsBasis;
    private IReadOnlyList<LibraryItemViewModel> _allItems = [];
    private IReadOnlyList<LibraryItemViewModel> _items = [];
    private LibraryItemViewModel? _selectedItem;
    private string _filterText = string.Empty;
    private bool _showCancelled;
    private LibraryStateFilter _stateFilter;
    private IReadOnlyList<LibraryItemViewModel> _textFiltered = [];
    private int _listedDatasets;
    private bool _showCoverage = true;

    /// <param name="library">The Library the panel shows.</param>
    /// <param name="tree">The collections and sources; the list shows the selected node's datasets.</param>
    /// <param name="details">The selected dataset's actions.</param>
    /// <param name="loader">Loads datasets and reports their state.</param>
    /// <param name="downloader">Says whether items are downloaded, and how their downloads go.</param>
    /// <param name="rows">Builds the list's rows.</param>
    /// <param name="open">Opens the listed items (Load as you pan).</param>
    /// <param name="download">Downloads the listed items (Download listed), and says which packages it unpacked.</param>
    /// <param name="dispatch">Runs a change on the UI thread (the app passes <see cref="Services.UiThread.RunOrPost"/>).</param>
    /// <param name="coalesce">Queues a refresh for later on the UI thread, even from it, so a burst of changes refreshes once (the app passes <see cref="Services.UiThread.Post"/>).</param>
    /// <param name="time">The clock (forecast ages, staleness).</param>
    /// <param name="viewTime">The time the map shows; <see langword="null"/> without a timeline.</param>
    /// <param name="timeFormat">The Local/UTC setting; <see langword="null"/> shows UTC.</param>
    public LibraryPanelViewModel(
        CollectionLibrary library,
        LibraryTreeViewModel tree,
        LibraryDetailsViewModel details,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        LibraryRows rows,
        OpenLibraryItemsWorkflow open,
        DownloadLibraryItemsWorkflow download,
        Action<Action> dispatch,
        Action<Action> coalesce,
        TimeProvider time,
        Services.GlobalTimeService? viewTime,
        Services.ITimeFormatProvider? timeFormat)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(coalesce);
        _time = time;
        _viewTime = viewTime;
        _runFormat = timeFormat is null ? null : () => timeFormat.Current;
        if (timeFormat is not null)
            timeFormat.TimeFormatChanged += _ => dispatch(OnTimeFormatChanged);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(details);
        _downloader = downloader;
        _rows = rows;
        _open = open;
        _download = download;
        _details = details;
        _tap = new LibraryTapViewModel(this);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(dispatch);
        _library = library;
        _tree = tree;
        _loader = loader;
        _dispatch = dispatch;
        _coalesce = coalesce;

        SetAmlFilterCommand = new RelayCommand<string>(product => AmlFilter = product switch
        {
            null or AmlFilterOff => null,
            AmlFilterAllProducts => string.Empty,
            _ => product,
        });
        LoadAsYouPanCommand = new AsyncRelayCommand(LoadListedAsYouPanAsync, () => _items.Count > 0);
        DownloadListedCommand = new AsyncRelayCommand(DownloadListedAsync, () => DownloadableCount > 0);
        CancelDownloadsCommand = new RelayCommand(() => _downloader.CancelAll(), () => IsBulkDownloading);
        if (_viewTime is not null)
        {
            // The "At view time" facet follows the Timeline (#711, handoff G2).
            _viewTime.CurrentTimeChanged += _ => _dispatch(OnViewTimeChanged);
            _viewTime.RangeChanged += () => _dispatch(OnViewTimeChanged);
        }
        _loader.Changed += OnLoaderChanged;
        _downloader.Changed += OnLoaderChanged;
        _downloader.ProgressChanged += OnDownloadProgress;
        _download.PackagesUnpacked += OnPackagesUnpacked;
        _tree.SelectedNodeChanged += OnSelectedNodeChanged;
        _tree.Synced += OnTreeSynced;
        _tree.SecomDetailsChanged += OnSecomDetailsChanged;
        _tree.SecomPickerOpening += OnSecomPickerOpening;

        // Forecast runs age by the minute: time left, and Expired (#685).
        _clock = _time.CreateTimer(_ => _dispatch(OnClockTick), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        RebuildItems(force: true);
    }

    /// <summary>The collections and sources; the list shows the selected node's datasets.</summary>
    public LibraryTreeViewModel Tree => _tree;

    /// <summary>The details pane: the selected dataset's actions.</summary>
    public LibraryDetailsViewModel Details => _details;

    /// <summary>The map tap's banner and the list at a map location.</summary>
    public LibraryTapViewModel Tap => _tap;

    private void OnSelectedNodeChanged(object? sender, EventArgs e)
    {
        _tap.EndTap();
        if (_tap.HasLocation)
            _tap.SetLocation(null);
        else
            RebuildItems(force: true);
    }

    private void OnTreeSynced(object? sender, EventArgs e) => RebuildItems(force: false);

    private void OnSecomDetailsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ShowsSecomDetails));
        OnPropertyChanged(nameof(ShowsItemPlaceholder));
    }

    private void OnSecomPickerOpening(object? sender, EventArgs e) => SelectedItem = null;

    /// <summary>The datasets of the tree's selected node passing the filters.</summary>
    public IReadOnlyList<LibraryItemViewModel> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    /// <summary>The selected dataset.</summary>
    public LibraryItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
                OnPropertyChanged(nameof(ShowsSecomDetails));
                OnPropertyChanged(nameof(ShowsItemPlaceholder));
                _tap.OnSelectionChanged();
                _details.Item = value;
            }
        }
    }

    /// <summary>
    /// The progress line for background chart preparation (#1266), shown under
    /// the bulk bar; <see langword="null"/> where the host runs no warmer.
    /// </summary>
    public PortrayalWarmStatusViewModel? PortrayalWarm { get; set; }

    /// <summary>True when the details pane shows a SECOM source's Connection: one is selected and no dataset is.</summary>
    public bool ShowsSecomDetails => _tree.SecomDetails?.SourceId is not null && _selectedItem is null;

    /// <summary>True when the details pane shows its "select a dataset" placeholder.</summary>
    public bool ShowsItemPlaceholder => _selectedItem is null && !ShowsSecomDetails;

    /// <summary>True when a dataset is selected.</summary>
    public bool HasSelectedItem => _selectedItem is not null;

    /// <summary>Free text matched against name, title, spec and properties.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                _tap.EndTap();
                ApplyFilter();
            }
        }
    }

    /// <summary>Whether cancelled datasets are listed.</summary>
    public bool ShowCancelled
    {
        get => _showCancelled;
        set
        {
            if (SetProperty(ref _showCancelled, value))
            {
                _tap.EndTap();
                ApplyFilter();
            }
        }
    }

    /// <summary>Which datasets are listed by state (the segments under the filter box).</summary>
    public LibraryStateFilter StateFilter
    {
        get => _stateFilter;
        set
        {
            if (SetProperty(ref _stateFilter, value))
            {
                OnPropertyChanged(nameof(IsStateAll));
                OnPropertyChanged(nameof(IsStateLocal));
                OnPropertyChanged(nameof(IsStateOnline));
                OnPropertyChanged(nameof(IsStateUpdates));
                _tap.EndTap();
                ApplyFilter();
            }
        }
    }

    public bool IsStateAll { get => _stateFilter == LibraryStateFilter.All; set { if (value) StateFilter = LibraryStateFilter.All; } }

    public bool IsStateLocal { get => _stateFilter == LibraryStateFilter.Local; set { if (value) StateFilter = LibraryStateFilter.Local; } }

    public bool IsStateOnline { get => _stateFilter == LibraryStateFilter.Online; set { if (value) StateFilter = LibraryStateFilter.Online; } }

    public bool IsStateUpdates { get => _stateFilter == LibraryStateFilter.Updates; set { if (value) StateFilter = LibraryStateFilter.Updates; } }

    /// <summary><see cref="SetAmlFilterCommand"/>'s parameter that turns the AML filter off.</summary>
    internal const string AmlFilterOff = "off";

    /// <summary><see cref="SetAmlFilterCommand"/>'s parameter for every AML product.</summary>
    internal const string AmlFilterAllProducts = "all";

    /// <summary>
    /// The AML filter (#1370, decision 5): <see langword="null"/> when off,
    /// empty for every AML product, else a product code or
    /// <see cref="AmlLibraryRows.Aml30"/>. While on, only AML cells are listed.
    /// </summary>
    internal string? AmlFilter
    {
        get => _amlFilter;
        set
        {
            if (string.Equals(_amlFilter, value, StringComparison.Ordinal))
                return;
            _amlFilter = value;
            RaiseAmlFilter();
            _tap.EndTap();
            ApplyFilter();
        }
    }

    /// <summary>Sets the AML filter: <see cref="AmlFilterOff"/>, <see cref="AmlFilterAllProducts"/> or a product.</summary>
    public ICommand SetAmlFilterCommand { get; }

    /// <summary>True when the listed node holds AML cells, so the AML chip shows (decision 5).</summary>
    public bool HasAml { get; private set; }

    /// <summary>True while the AML filter narrows the list.</summary>
    public bool IsAmlFilterActive => _amlFilter is not null;

    /// <summary>The AML chip: "AML", or "AML · All products" / "AML · CLB" while it filters.</summary>
    public string AmlFilterText => _amlFilter switch
    {
        null => Strings.Aml_Chip,
        "" => $"{Strings.Aml_Chip} · {Strings.Aml_Filter_AllProducts}",
        var product => $"{Strings.Aml_Chip} · {product}",
    };

    private void RaiseAmlFilter()
    {
        OnPropertyChanged(nameof(AmlFilter));
        OnPropertyChanged(nameof(IsAmlFilterActive));
        OnPropertyChanged(nameof(AmlFilterText));
    }

    /// <summary>Whether an item passes the AML filter (everything does while it is off).</summary>
    private bool PassesAml(LibraryItemViewModel item) =>
        _amlFilter is null
        || (item.Aml is { } aml && AmlLibraryRows.MatchesProduct(aml, _amlFilter.Length == 0 ? null : _amlFilter));

    /// <summary>
    /// Lists only data valid at the Timeline's view time (#711, handoff G2):
    /// a toggle beside the filter box that narrows whichever segment is chosen.
    /// </summary>
    public bool IsAtViewTime
    {
        get => _atViewTime;
        set
        {
            if (SetProperty(ref _atViewTime, value))
            {
                _tap.EndTap();
                ApplyFilter();
            }
        }
    }

    /// <summary>"Only data valid at the view time · 12".</summary>
    public string AtViewTimeTooltip => string.Format(CultureInfo.CurrentCulture, Strings.Tooltip_LibraryAtViewTimeFormat, AtViewTimeCount);

    /// <summary>True when the Timeline has a view time, so "At view time" and the row's Timeline actions apply.</summary>
    public bool HasViewTime => _viewTime?.CurrentTime is not null && _viewTime.IsActive;

    /// <summary>Of the datasets matching the text, how many hold data at the view time.</summary>
    public int AtViewTimeCount { get; private set; }

    /// <summary>Raised after <see cref="Reveal"/> selected a dataset, so the view can show the panel and scroll to it.</summary>
    public event EventHandler<LibraryItemViewModel>? Revealed;

    /// <summary>
    /// Selects a dataset by its source and key, as "Reveal in Library" does
    /// from the Timeline (#711, handoff G3): its source node, every state,
    /// no text filter, and its model's group opened.
    /// </summary>
    internal bool Reveal(Guid sourceId, string key)
    {
        var node = _tree.Nodes.SelectMany(n => n.SelfAndDescendants())
            .FirstOrDefault(n => !n.IsGroup && n.Source?.Id == sourceId && n.EnumerateItems().Any(p => p.Item.Key == key))
            ?? _tree.Nodes.FirstOrDefault(n => n.EnumerateItems().Any(p => p.Source.Id == sourceId && p.Item.Key == key));
        if (node is null)
            return false;
        if (_tap.HasLocation)
            _tap.SetLocation(null);
        _filterText = string.Empty;
        OnPropertyChanged(nameof(FilterText));
        _stateFilter = LibraryStateFilter.All;
        _atViewTime = false;
        _amlFilter = null;
        _collapsedAml.Remove(sourceId);
        RaiseAmlFilter();
        OnPropertyChanged(nameof(StateFilter));
        OnPropertyChanged(nameof(IsStateAll));
        OnPropertyChanged(nameof(IsStateLocal));
        OnPropertyChanged(nameof(IsStateOnline));
        OnPropertyChanged(nameof(IsStateUpdates));
        OnPropertyChanged(nameof(IsAtViewTime));
        if (_tree.SelectedNode != node)
            _tree.SelectedNode = node;
        else
            ApplyFilter();
        var row = _allItems.FirstOrDefault(i => i.Source.Id == sourceId && i.Item.Key == key);
        if (row is not null && PackageOf(row) is { } package && _expandedPackages.Add(package))
            ApplyFilter();
        SelectedItem = _items.FirstOrDefault(i => i.Source.Id == sourceId && i.Item.Key == key);
        if (_selectedItem is { } selected)
            Revealed?.Invoke(this, selected);
        return _selectedItem is not null;
    }

    private void OnViewTimeChanged()
    {
        OnPropertyChanged(nameof(HasViewTime));
        _details.Refresh();
        if (_atViewTime)
            ApplyFilter();
        else
            Recount();
    }

    /// <summary>Datasets passing the text filter (the "All" segment's count).</summary>
    public int AllCount => _textFiltered.Count;

    /// <summary>Of those, how many are local and current (local, loaded or on pan).</summary>
    public int LocalCount { get; private set; }

    /// <summary>Of those, how many are online.</summary>
    public int OnlineCount { get; private set; }

    /// <summary>Of those, how many have an update available.</summary>
    public int UpdatesCount { get; private set; }

    /// <summary>Whether the listed datasets' coverage is drawn on the map.</summary>
    public bool ShowCoverage
    {
        get => _showCoverage;
        set => SetProperty(ref _showCoverage, value);
    }

    /// <summary>"116", or "8 of 116" when filtered, shown inside the filter box.</summary>
    public string ItemsSummary =>
        ListedCount == _allItems.Count
            ? string.Format(CultureInfo.CurrentCulture, Strings.Library_ItemCountFormat, _allItems.Count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_FilteredItemCountFormat, ListedCount, _allItems.Count);

    /// <summary>
    /// How many datasets pass the filters — including those inside a collapsed
    /// package group, and never the groups' header rows.
    /// </summary>
    public int ListedCount => _listedDatasets;

    /// <summary>Registers every listed local dataset to load as it comes into view.</summary>
    public ICommand LoadAsYouPanCommand { get; }

    /// <summary>Downloads every listed online (or outdated) dataset.</summary>
    public ICommand DownloadListedCommand { get; }

    /// <summary>Cancels the running bulk download.</summary>
    public ICommand CancelDownloadsCommand { get; }

    /// <summary>True while a bulk download runs (the bulk bar shows its progress and Cancel).</summary>
    public bool IsBulkDownloading => _downloader.Progress is not null;

    /// <summary>True when there are listed datasets for the bulk bar to act on.</summary>
    public bool HasBulkBar => _items.Count > 0 || IsBulkDownloading;

    /// <summary>
    /// The bulk bar's first line: "6 to download · 16,4 MB" (or, while
    /// downloading, what is left).
    /// </summary>
    public string BulkSummary
    {
        get
        {
            if (_downloader.Progress is { } progress)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkToDownloadFormat,
                    progress.Remaining, LibraryItemViewModel.FormatBytes(progress.BytesLeft));
            }

            if (IsBulkOffline)
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkOfflineFormat, LocalListedCount());

            var c = CultureInfo.CurrentCulture;
            if (NewRunModels() is { Count: > 0 } newRuns)
            {
                var bytes = LibraryItemViewModel.FormatBytes(Downloadable().Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0));
                return string.Format(c, newRuns.Count == 1 ? Strings.Library_BulkNewRunFormat : Strings.Library_BulkNewRunsFormat, newRuns.Count, bytes);
            }

            if (ExpiredModels() is { Count: > 0 } expired)
                return string.Format(c, expired.Count == 1 ? Strings.Library_BulkRunExpired : Strings.Library_BulkRunsExpiredFormat, expired.Count);

            var downloadable = Downloadable().ToArray();
            if (IsUpdateMode)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkUpdatesFormat, downloadable.Length,
                    LibraryItemViewModel.FormatBytes(downloadable.Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0)));
            }

            return downloadable.Length == 0
                ? string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkListedFormat, ListedCount)
                : string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkToDownloadFormat, downloadable.Length,
                    LibraryItemViewModel.FormatBytes(downloadable.Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0)));
        }
    }

    /// <summary>
    /// The bulk bar's second line: what the buttons act on ("Filtered set · 2
    /// already local"), or the download's progress.
    /// </summary>
    public string BulkScope
    {
        get
        {
            if (_downloader.Progress is { } progress)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkDownloadingFormat,
                    Math.Min(progress.Total, progress.Completed + progress.Failed + 1), progress.Total, progress.Fraction);
            }

            if (IsBulkOffline)
                return Strings.Library_BulkOfflineScope;

            if (NewRunModels() is { Count: > 0 } newRuns)
            {
                // "cbofs 18:00Z replaces 12:00Z" for one model; the models otherwise.
                var first = ListedDatasets().First(i => i.IsForecast && i.Availability == LibraryAvailability.Outdated);
                return newRuns.Count == 1
                    && EncDotNet.S100.Collections.Indexing.S100ForecastFeedIndexer.RunOf(first.Item) is { } onlineRun
                    && _downloader.LocalPublishedAtOf(first.Item) is { } localRun
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkRunReplacesFormat, newRuns[0],
                        ShortRun(onlineRun),
                        ShortRun(localRun))
                    : string.Join(", ", newRuns);
            }

            if (ExpiredModels() is { Count: > 0 })
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkLastCheckedFormat, LastChecked());

            if (IsUpdateMode)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkUpdatesScopeFormat,
                    LocalListedCount(), _items.Count(i => !i.IsGroupHeader));
            }

            if (DownloadableCount == 0)
                return Strings.Library_BulkNothing;

            var scope = IsFiltered ? Strings.Library_BulkScopeFiltered : Strings.Library_BulkScopeAll;
            var local = ListedDatasets().Count(i => i.PrimaryAvailability == LibraryPrimaryAvailability.Local && !_downloader.IsOutdated(i.Item));
            return local == 0 ? scope : string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkAlreadyLocalFormat, scope, local);
        }
    }

    /// <summary>True when the list is narrowed by text, state or a map tap.</summary>
    private bool IsFiltered => _filterText.Trim().Length > 0 || _stateFilter != LibraryStateFilter.All || _tap.HasLocation || _amlFilter is not null;

    /// <summary>How many listed datasets can be downloaded.</summary>
    public int DownloadableCount => Downloadable().Count();

    private IEnumerable<LibraryItemViewModel> Downloadable() =>
        IsBulkOffline ? []
        : NewRunModels().Count > 0
            ? ListedDatasets().Where(i => i.IsForecast && i.Availability == LibraryAvailability.Outdated && _downloader.CanDownload(i.Item))
        : ExpiredModels().Count > 0 ? []
        : IsUpdateMode ? ListedDatasets().Where(i => i.QuietUpdates && i.Availability == LibraryAvailability.Outdated && _downloader.CanDownload(i.Item))
        : ListedDatasets().Where(i => _downloader.CanDownload(i.Item) && (i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item)));

    /// <summary>
    /// The listed datasets, including the tiles under a collapsed forecast
    /// model's row (an unpacked package's rows are local already).
    /// </summary>
    private IEnumerable<LibraryItemViewModel> ListedDatasets() =>
        _items.SelectMany(i => i.IsModelHeader || i.IsAmlSection ? i.Members : i.IsGroupHeader ? [] : [i]).Distinct();

    /// <summary>The listed forecast models with a newer run than the one downloaded (#685).</summary>
    private IReadOnlyList<string> NewRunModels() => ForecastModelsIn(LibraryAvailability.Outdated);

    /// <summary>The listed forecast models whose downloaded run has ended, with nothing newer known.</summary>
    private IReadOnlyList<string> ExpiredModels() => ForecastModelsIn(LibraryAvailability.Expired);

    private IReadOnlyList<string> ForecastModelsIn(LibraryAvailability state) =>
        ListedDatasets()
            .Where(i => i.IsForecast && i.Availability == state)
            .Select(i => ForecastRuns.ModelOf(i.Item))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// True when listed forecast runs have ended and no newer run is known:
    /// the bulk bar offers "Check for new runs" (Refresh) instead of a download.
    /// </summary>
    public bool IsCheckForRuns => !IsBulkDownloading && !IsBulkOffline && NewRunModels().Count == 0 && ExpiredModels().Count > 0;

    /// <summary>When the selected node's catalogues were last checked ("21:04" today, else the date).</summary>
    private string LastChecked()
    {
        var sources = _tree.SelectedNode is { } node ? node.Source is { } s ? [s] : node.Collection.Sources : [];
        var checkedAt = sources
            .Select(x => _tree.HealthOf(x.Definition)?.CheckedAt ?? x.Index?.IndexedAt)
            .OfType<DateTimeOffset>()
            .DefaultIfEmpty(_time.GetUtcNow())
            .Max()
            .ToLocalTime();
        return checkedAt.Date == _time.GetLocalNow().Date
            ? checkedAt.ToString("t", CultureInfo.CurrentCulture)
            : checkedAt.ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// True when listed remote-catalogue datasets have newer editions online:
    /// the bulk bar then offers "Update downloaded (N)" for those alone (#685),
    /// rather than mixing updates into a download of everything listed.
    /// </summary>
    public bool IsUpdateMode => ListedDatasets().Any(i => i.QuietUpdates && !i.IsForecast && i.Availability == LibraryAvailability.Outdated);

    /// <summary>
    /// True when the selected node is remote catalogues whose server could not
    /// be reached: the cached index still lists and loads, but nothing downloads.
    /// </summary>
    public bool IsBulkOffline =>
        !IsBulkDownloading
        && _tree.SelectedNode is { } node
        && (node.Source is { } source ? [source] : node.Collection.Sources) is { Count: > 0 } sources
        && sources.All(s => s.Definition is S100CatalogueFeedSource or S100ForecastFeedSource
            && _tree.HealthOf(s.Definition) is { IsReachable: false });

    /// <summary>How many listed datasets have a copy on disk (current or not).</summary>
    private int LocalListedCount() =>
        ListedDatasets().Count(i => i.PrimaryAvailability is LibraryPrimaryAvailability.Local
            or LibraryPrimaryAvailability.Update or LibraryPrimaryAvailability.Expired);

    /// <summary>True when some listed dataset can be downloaded (and no bulk download is running).</summary>
    public bool HasDownloadable => !IsBulkDownloading && DownloadableCount > 0;

    /// <summary>"Download 1,193 (203 MB)" for the bulk download button.</summary>
    public string DownloadListedText
    {
        get
        {
            if (NewRunModels() is { Count: > 0 } newRuns)
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_UpdateDownloadedFormat, newRuns.Count);
            return string.Format(CultureInfo.CurrentCulture,
                IsUpdateMode ? Strings.Library_UpdateDownloadedFormat : Strings.Library_DownloadListedFormat, DownloadableCount);
        }
    }

    /// <summary>
    /// Selects the tree node of a remote catalogue's area (a map tap on it
    /// when zoomed out; #685): its group node, or the source when it shows no
    /// area nodes. The collection and source open so the node is visible.
    /// </summary>
    public void SelectArea(Guid sourceId, string folder)
    {
        foreach (var collection in _tree.Nodes)
        {
            if (collection.Children.FirstOrDefault(c => c.Id == sourceId) is not { } source)
                continue;

            collection.IsExpanded = true;
            source.IsExpanded = true;
            _tree.SelectedNode = source.Children.FirstOrDefault(c => c.GroupId == folder) ?? source;
            return;
        }
    }

    public void Dispose()
    {
        _clock?.Dispose();
        _tree.SelectedNodeChanged -= OnSelectedNodeChanged;
        _tree.Synced -= OnTreeSynced;
        _tree.SecomDetailsChanged -= OnSecomDetailsChanged;
        _tree.SecomPickerOpening -= OnSecomPickerOpening;
        _loader.Changed -= OnLoaderChanged;
        _downloader.Changed -= OnLoaderChanged;
        _downloader.ProgressChanged -= OnDownloadProgress;
    }

    private void OnDownloadProgress(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_progressRefreshPosted)
                return;
            _progressRefreshPosted = true;
        }

        _dispatch(() =>
        {
            lock (_gate)
                _progressRefreshPosted = false;
            RefreshDownloadProgress();
        });
    }

    /// <summary>Updates rows, the bulk bar and the tree's download lines — without re-resolving availability.</summary>
    private void RefreshDownloadProgress()
    {
        foreach (var item in _items)
            item.RefreshDownload();
        OnPropertyChanged(nameof(IsBulkDownloading));
        OnPropertyChanged(nameof(HasDownloadable));
        OnPropertyChanged(nameof(HasBulkBar));
        OnPropertyChanged(nameof(BulkSummary));
        OnPropertyChanged(nameof(BulkScope));
        OnPropertyChanged(nameof(IsBulkOffline));
        OnPropertyChanged(nameof(IsCheckForRuns));
        OnPropertyChanged(nameof(DownloadListedText));
        ((RelayCommand)CancelDownloadsCommand).NotifyCanExecuteChanged();
        _tree.RefreshDownloadStatus();
    }

    /// <summary>
    /// Every minute: forecast runs lose time and may expire (#685), so their
    /// rows, the counts, the status lines and the bulk bar are re-evaluated.
    /// </summary>
    private void OnClockTick()
    {
        var forecasts = _allItems.Where(i => i.IsForecast).ToArray();
        if (forecasts.Length == 0)
            return;

        foreach (var item in forecasts)
            item.RefreshClock();
        foreach (var header in _packageHeaders.Values.Where(h => h.IsModelHeader))
            header.Members = header.Members;
        _tree.RefreshStatus();
        if (_stateFilter == LibraryStateFilter.All)
            Recount();
        else
            ApplyFilter();
        RefreshDownloadProgress();
        ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasDownloadable));
    }

    private void OnLoaderChanged(object? sender, EventArgs e)
    {
        // Coalesce bursts (e.g. one notification per downloaded cell).
        lock (_gate)
        {
            if (_availabilityRefreshPosted)
                return;
            _availabilityRefreshPosted = true;
        }

        _coalesce(() =>
        {
            lock (_gate)
                _availabilityRefreshPosted = false;
            // Every row, not only the listed ones: the state segments count them all.
            // (A row whose availability was never resolved returns at once.)
            foreach (var item in _allItems)
                item.RefreshAvailability();
            foreach (var header in _packageHeaders.Values.Where(h => h.IsModelHeader))
                header.Members = header.Members;
            _tree.RefreshStatus();
            _details.Refresh();
            RefreshDownloadProgress();
            // A dataset whose state changed may now belong to another segment.
            if (_stateFilter == LibraryStateFilter.All)
                Recount();
            else
                ApplyFilter();
            ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(DownloadListedText));
            OnPropertyChanged(nameof(HasDownloadable));
            OnPropertyChanged(nameof(BulkSummary));
            OnPropertyChanged(nameof(BulkScope));
            // The coverage overlay styles by availability; let it redraw.
            OnPropertyChanged(nameof(Items));
        });
    }

    private Task LoadListedAsYouPanAsync() =>
        LoadRowsAsync(ListedDatasets().ToArray(), defer: true);

    /// <summary>Opens <paramref name="rows"/> now, or as the map pans to them when <paramref name="defer"/> is true.</summary>
    private Task<LibraryLoadResult> LoadRowsAsync(IReadOnlyList<LibraryItemViewModel> rows, bool defer) =>
        _open.RunAsync([.. rows.Select(row => row.State)], defer);

    private Task DownloadListedAsync() =>
        _download.RunAsync([.. Downloadable().Select(row => (row.Item, row.Source))], load: false);

    /// <summary>Opens the groups of packages whose datasets the Library now lists (once).</summary>
    private void OnPackagesUnpacked(object? sender, IReadOnlyList<(Guid Source, string Package)> packages)
    {
        foreach (var package in packages)
            _expandedPackages.Add(package);
        _dispatch(() => RebuildItems(force: true));
    }

    /// <inheritdoc/>
    public void ListAt(GeoPosition? position)
    {
        var selected = _selectedItem;
        _itemsBasis = null;
        _allItems = position is { } p ? HitsAt(p) : BuildNodeItems(_tree.SelectedNode);
        UpdateHasAml();
        ApplyFilter();
        SelectedItem = selected is null ? null : _items.FirstOrDefault(i => LibraryTapViewModel.SameItem(i, selected));
    }

    /// <summary>
    /// Every library dataset covering <paramref name="position"/>, most
    /// detailed (highest usage band, then smallest extent) first.
    /// </summary>
    internal List<LibraryItemViewModel> HitsAt(GeoPosition position) =>
        LibraryQuery.HitsAt(_library.Collections, position)
            .Select(p => _rows.Create(p.Item, p.Source))
            .ToList();

    /// <summary>The collections the Library holds now, sources and indexes included.</summary>
    internal IReadOnlyList<LibraryCollection> Collections => _library.Collections;

    private IReadOnlyList<LibraryItemViewModel> BuildNodeItems(LibraryNodeViewModel? node) =>
        node is null
            ? []
            : node.EnumerateItems()
                .Select(p => _rows.Create(p.Item, p.Source, node.Collection.Definition.Name))
                .ToArray();

    private void RebuildItems(bool force)
    {
        if (_tap.Location is { } location)
        {
            // Listing datasets at a map location: refresh the hits only when
            // the library itself changed.
            if (force)
                return;
            ListAt(location);
            return;
        }

        var node = _tree.SelectedNode;
        var basis = node?.ItemIndexes;
        if (!force && basis is not null && _itemsBasis is not null
            && basis.Count == _itemsBasis.Count && basis.Zip(_itemsBasis).All(p => ReferenceEquals(p.First, p.Second)))
        {
            return;
        }

        _itemsBasis = basis;
        var selectedKey = _selectedItem is { } sel ? (sel.Source.Id, sel.Item.Key) : default;
        _allItems = BuildNodeItems(node);
        UpdateHasAml();
        ApplyFilter();

        SelectedItem = selectedKey == default
            ? null
            : _items.FirstOrDefault(i => i.Source.Id == selectedKey.Id && i.Item.Key == selectedKey.Key);
    }

    private void ApplyFilter()
    {
        var filter = _filterText.Trim();
        _textMatched = _allItems
            .Where(i => _showCancelled || !i.IsCancelled)
            .Where(i => filter.Length == 0 || i.Matches(filter))
            .Where(PassesAml)
            .ToArray();
        var viewTime = ViewTime;
        _textFiltered = _atViewTime ? [.. _textMatched.Where(i => IsValidAt(i, viewTime))] : _textMatched;
        var listed = _textFiltered.Where(i => InState(i, _stateFilter)).ToArray();
        _listedDatasets = listed.Length;
        Items = SectionAml(GroupPackages(listed, expandAll: filter.Length > 0), expandAll: filter.Length > 0);
        Recount();
        _tap.OnListChanged();
        OnPropertyChanged(nameof(ItemsSummary));
        OnPropertyChanged(nameof(HasBulkBar));
        OnPropertyChanged(nameof(IsBulkOffline));
        OnPropertyChanged(nameof(IsUpdateMode));
        OnPropertyChanged(nameof(IsCheckForRuns));
        OnPropertyChanged(nameof(BulkSummary));
        OnPropertyChanged(nameof(BulkScope));
        ((AsyncRelayCommand)LoadAsYouPanCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DownloadListedText));
        OnPropertyChanged(nameof(HasDownloadable));

        if (_selectedItem is not null && !_items.Contains(_selectedItem))
            SelectedItem = null;
    }

    /// <summary>
    /// Lists the datasets an unpacked community-list package holds under a
    /// header row named by the package (collapsed unless it was just
    /// unpacked, or the text filter matched inside it), so the entry the user
    /// downloaded does not simply disappear.
    /// </summary>
    private IReadOnlyList<LibraryItemViewModel> GroupPackages(IEnumerable<LibraryItemViewModel> items, bool expandAll)
    {
        var rows = new List<LibraryItemViewModel>();
        var groups = new Dictionary<(Guid, string), List<LibraryItemViewModel>>();
        foreach (var item in items)
        {
            if (PackageOf(item) is { } key)
            {
                if (!groups.TryGetValue(key, out var members))
                {
                    groups[key] = members = [];
                    rows.Add(item);  // the group's place in the list
                }

                members.Add(item);
            }
            else
            {
                rows.Add(item);
            }
        }

        if (groups.Count == 0)
            return rows;

        var result = new List<LibraryItemViewModel>(rows.Count + groups.Count);
        foreach (var row in rows)
        {
            if (PackageOf(row) is not { } key)
            {
                result.Add(row);
                continue;
            }

            var members = groups[key];
            var expanded = expandAll || _expandedPackages.Contains(key);
            if (row.IsForecast)
            {
                if (!_packageHeaders.TryGetValue(key, out var model) || !ReferenceEquals(model.Source, row.Source))
                {
                    _packageHeaders[key] = model = LibraryItemViewModel.ForModelGroup(
                        row.Source, members, expanded, TogglePackage, _loader.StateOf, _downloader, _time, _runFormat);
                }
                else
                {
                    model.Members = members;
                }

                model.IsExpanded = expanded;
                result.Add(model);
                if (expanded)
                {
                    foreach (var member in members)
                        member.IsGroupChild = true;
                    result.AddRange(members);
                }

                continue;
            }

            if (!_packageHeaders.TryGetValue(key, out var header) || !ReferenceEquals(header.Source, row.Source))
            {
                _packageHeaders[key] = header = LibraryItemViewModel.ForPackageGroup(
                    row.Source, key.Item2, row.Item.Properties.GetValueOrDefault("packageTitle"), members.Count, expanded, TogglePackage, _time,
                    members.Select(m => (m.Item.Location as RemoteItemLocation)?.LastModified).Max(),
                    members.Select(m => m.Item.ProductSpec).Distinct(StringComparer.Ordinal).SingleOrDefault() ?? "S-57");
            }

            header.GroupCount = members.Count;
            header.IsExpanded = expanded;
            result.Add(header);
            if (expanded)
            {
                foreach (var member in members)
                    member.IsGroupChild = true;
                result.AddRange(members);
            }
        }

        return result;
    }

    /// <summary>
    /// Lists each source's NATO Additional Military Layers cells under a
    /// header of their own, below the source's charts (#1370, decision 5), so
    /// an ENC and an AML cell of the same area are never neighbours by
    /// accident. Rows inside a package stay where they are.
    /// </summary>
    private IReadOnlyList<LibraryItemViewModel> SectionAml(IReadOnlyList<LibraryItemViewModel> rows, bool expandAll)
    {
        if (!rows.Any(IsSectionedAml))
            return rows;

        var result = new List<LibraryItemViewModel>(rows.Count + 1);
        foreach (var bySource in rows.GroupBy(r => r.Source.Id))
        {
            var aml = bySource.Where(IsSectionedAml).ToArray();
            result.AddRange(aml.Length == 0 ? bySource : bySource.Where(r => !IsSectionedAml(r)));
            if (aml.Length == 0)
                continue;

            var key = (bySource.Key, LibraryItemViewModel.AmlSectionKey);
            var expanded = expandAll || !_collapsedAml.Contains(bySource.Key);
            if (!_packageHeaders.TryGetValue(key, out var header) || !ReferenceEquals(header.Source, aml[0].Source))
            {
                _packageHeaders[key] = header = LibraryItemViewModel.ForAmlSection(aml[0].Source, aml, expanded, ToggleAmlSection, _time);
            }
            else
            {
                header.GroupCount = aml.Length;
                header.Members = aml;
            }

            header.IsExpanded = expanded;
            result.Add(header);
            if (expanded)
                result.AddRange(aml);
        }

        return result;

        static bool IsSectionedAml(LibraryItemViewModel row) => row.IsAml && !row.IsGroupChild;
    }

    /// <summary>
    /// Shows the AML chip only while the listed datasets hold AML cells; with
    /// none, its filter is turned off so it can't empty the list unseen.
    /// </summary>
    private void UpdateHasAml()
    {
        var hasAml = _allItems.Any(i => i.IsAml);
        if (hasAml != HasAml)
        {
            HasAml = hasAml;
            OnPropertyChanged(nameof(HasAml));
        }

        if (!hasAml && _amlFilter is not null)
        {
            _amlFilter = null;
            RaiseAmlFilter();
        }
    }

    private void ToggleAmlSection(LibraryItemViewModel header)
    {
        if (!_collapsedAml.Remove(header.Source.Id))
            _collapsedAml.Add(header.Source.Id);
        var selected = _selectedItem;
        ApplyFilter();
        SelectedItem = selected is null ? null : _items.FirstOrDefault(i => LibraryTapViewModel.SameItem(i, selected));
    }

    /// <summary>
    /// The row a dataset is listed under: the unpacked package a community-list
    /// dataset or a SECOM exchange-set dataset (#916) came from, or the model of
    /// a forecast run's tile (#685); else <see langword="null"/>.
    /// </summary>
    private static (Guid, string)? PackageOf(LibraryItemViewModel item) =>
        item.IsForecast && item.Source.Definition is S100ForecastFeedSource { Shape: ForecastShape.Tiles }
            && ForecastRuns.ModelOf(item.Item) is { } model
            ? (item.Source.Id, "model:" + model)
        : item.Source.Definition is ChartCatalogsFeedSource or SecomSource
        && item.Item.Properties.TryGetValue("package", out var package)
        && !LibraryItemText.IsPackageEntry(item.Item)
            ? (item.Source.Id, package)
            : null;

    private void TogglePackage(LibraryItemViewModel header)
    {
        if (!_expandedPackages.Remove(header.GroupKey))
            _expandedPackages.Add(header.GroupKey);
        var selected = _selectedItem;
        ApplyFilter();
        SelectedItem = selected is null ? null : _items.FirstOrDefault(i => LibraryTapViewModel.SameItem(i, selected));
    }

    /// <summary>"18:00Z", or the local short time, as the user reads runs (#730).</summary>
    private string ShortRun(DateTimeOffset run) =>
        Services.ForecastRunNames.FormatRun(run.UtcDateTime, _runFormat?.Invoke() ?? TimeFormat.Utc, _time.LocalTimeZone, _time.GetUtcNow().UtcDateTime);

    /// <summary>The user switched Local/UTC: re-read every run time shown (#730).</summary>
    private void OnTimeFormatChanged()
    {
        foreach (var item in _items)
            item.RefreshTimeFormat();
        foreach (var header in _packageHeaders.Values)
            header.RefreshTimeFormat();
        OnPropertyChanged(nameof(BulkScope));
    }

    private DateTime? ViewTime => _viewTime?.IsActive == true ? _viewTime.CurrentTime : null;

    /// <summary>The Timeline's view time, while it has one (for the MCP <c>validAt</c> filter).</summary>
    internal DateTime? CurrentViewTime => ViewTime;

    /// <summary>True when the item's data covers <paramref name="viewTime"/>.</summary>
    private static bool IsValidAt(LibraryItemViewModel item, DateTime? viewTime) =>
        viewTime is { } at && item.ValidWindow is { } window && at >= window.Start && at <= window.End;

    private static bool InState(LibraryItemViewModel item, LibraryStateFilter state) => state switch
    {
        LibraryStateFilter.Local => item.Availability is LibraryAvailability.Local or LibraryAvailability.Loaded or LibraryAvailability.Deferred,
        LibraryStateFilter.Updates when item.IsForecast => item.Availability is LibraryAvailability.Outdated or LibraryAvailability.Expired,
        LibraryStateFilter.Online => item.Availability == LibraryAvailability.Online,
        LibraryStateFilter.Updates => item.Availability == LibraryAvailability.Outdated,
        _ => true,
    };

    /// <summary>Updates the segment counts (availability can change without the list changing).</summary>
    private void Recount()
    {
        LocalCount = _textFiltered.Count(i => InState(i, LibraryStateFilter.Local));
        OnlineCount = _textFiltered.Count(i => InState(i, LibraryStateFilter.Online));
        UpdatesCount = _textFiltered.Count(i => InState(i, LibraryStateFilter.Updates));
        var viewTime = ViewTime;
        AtViewTimeCount = viewTime is null ? 0 : _textMatched.Count(i => IsValidAt(i, viewTime));
        OnPropertyChanged(nameof(AtViewTimeCount));
        OnPropertyChanged(nameof(AtViewTimeTooltip));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(LocalCount));
        OnPropertyChanged(nameof(OnlineCount));
        OnPropertyChanged(nameof(UpdatesCount));
    }

}

/// <summary>
/// Adds sources to the library interactively: picks a path (or online feed)
/// and confirms the target collection. Implemented by
/// <see cref="Services.LibraryImportCoordinator"/>.
/// </summary>
internal interface ILibraryImporter
{
    /// <summary>Picks a folder to scan for exchange sets and datasets.</summary>
    Task AddFolderAsync(Guid? targetCollectionId);

    /// <summary>Picks a zipped exchange set.</summary>
    Task AddExchangeSetZipAsync(Guid? targetCollectionId);

    /// <summary>
    /// Opens the directory of known online catalogues (issue #670), then the
    /// chosen catalogue's scope picker.
    /// </summary>
    Task AddOnlineCatalogueAsync(Guid? targetCollectionId);

    /// <summary>
    /// Asks for the URL of a feed served by <c>s100 feed serve</c> on another
    /// computer, checks it, then opens its product picker (issue #680).
    /// </summary>
    Task AddSharedFeedAsync(Guid? targetCollectionId);

    /// <summary>Opens the scope picker for a known online catalogue directly.</summary>
    Task AddKnownCatalogueAsync(EncDotNet.S100.Collections.KnownSources.KnownCatalogueSource source, Guid? targetCollectionId);

    /// <summary>Picks an S-128 catalogue file.</summary>
    Task AddS128CatalogueAsync(Guid? targetCollectionId);

    /// <summary>Picks a collection manifest (<c>*.s100collection.json</c>) and opens its group picker.</summary>
    Task AddCollectionManifestAsync(Guid? targetCollectionId);

    /// <summary>Reopens a collection manifest's group picker to change an existing source's selection.</summary>
    Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source);

    /// <summary>
    /// Opens the S-111 surface-currents catalogue at its models step with the
    /// models covering <paramref name="area"/> ticked (#685, handoff B8).
    /// </summary>
    Task AddCurrentsForAreaAsync(GeoBounds area, Guid? targetCollectionId) => Task.CompletedTask;

    /// <summary>Adds a known path (for example a dropped folder), confirming the target collection.</summary>
    Task AddPathAsync(string path, Guid? targetCollectionId);

    /// <summary>
    /// True when <paramref name="path"/> is already a library source or lies
    /// inside a folder source.
    /// </summary>
    bool IsInLibrary(string path);
}
