using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A titled group of choices (one tab in the dialog), as a core scope made it.</summary>
internal sealed class FacetGroupViewModel : ViewModelBase
{
    private int _selectedCount;

    /// <param name="source">The core group (e.g. "States", "Rivers", or a remote catalogue's region).</param>
    public FacetGroupViewModel(LibraryChoiceGroup source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Options = new ObservableCollection<LibraryChoice>(source.Options);
    }

    /// <summary>The core group shown.</summary>
    public LibraryChoiceGroup Source { get; }

    /// <summary>The group title (e.g. "States", "Rivers").</summary>
    public string Title => Source.Title;

    /// <summary>What the group stands for, when it is a value itself (a remote catalogue's region folder).</summary>
    public string? Key => Source.Key;

    /// <summary>The group's values as shown (those matching the filter text).</summary>
    public ObservableCollection<LibraryChoice> Options { get; }

    /// <summary>Every value of the group, including any hidden by the filter.</summary>
    public IReadOnlyList<LibraryChoice> AllOptions => Source.Options;

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

    internal void Refresh() => SelectedCount = AllOptions.Count(o => o.IsSelected);

    /// <summary>Shows the values whose label or value contains <paramref name="text"/>; ticks outside it are kept.</summary>
    internal void Filter(string text)
    {
        Options.Clear();
        foreach (var option in AllOptions)
        {
            if (text.Length == 0
                || option.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || option.Value.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                Options.Add(option);
            }
        }
    }
}

/// <summary>
/// View model for the "Add to Library" dialog: confirms which collection a
/// new source goes into (a new one, named, or an existing one) and, for an
/// online catalogue or a collection manifest, which part to include. The
/// catalogue is read, its choices offered and the source built by the core's
/// <see cref="LibrarySourceDraft"/> (#792), as headless hosts do; nothing is
/// loaded or downloaded except the catalogue itself.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel : ViewModelBase
{
    private readonly CollectionLibrary _library;
    private readonly LibraryCatalogueReaders _readers;
    private readonly TimeProvider _time;
    private string _searchText = string.Empty;
    private KnownCatalogueSource? _known;
    private DateOnly? _catalogueDate;
    private IReadOnlyList<FacetGroupViewModel> _facetGroups = [];
    private FacetGroupViewModel? _selectedFacetGroup;
    private bool _includeAll = true;
    private bool _nameEdited;

    private LibrarySourceKind _kind;
    private string? _path;
    private bool _createNew = true;
    private string _newCollectionName = string.Empty;
    private LibraryCollection? _selectedCollection;
    private bool _isLoading;
    private string? _loadError;
    // The source being added; null for an online catalogue this host cannot read.
    private LibrarySourceDraft? _draft;
    private string _selectionSummary = string.Empty;

    /// <param name="library">The Library the source is added to.</param>
    /// <param name="readers">Reads online catalogues; a catalogue without a reader cannot be added.</param>
    /// <param name="timeProvider">The clock (catalogue age, forecast end); the system clock when null.</param>
    public AddToLibraryDialogViewModel(CollectionLibrary library, LibraryCatalogueReaders? readers = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _readers = readers ?? new LibraryCatalogueReaders();
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
    public LibrarySourceKind Kind => _kind;

    /// <summary>The core's scope of the catalogue or manifest being added, once there is one.</summary>
    private LibraryCatalogueScope? Scope => _draft?.Scope;

    /// <summary>True when adding a scope of an online catalogue.</summary>
    public bool IsOnlineFeed => LibrarySourceKinds.IsOnline(_kind);

    /// <summary>True when the choices can be filtered by text (community lists, manifests).</summary>
    public bool IsSearchable => _kind is LibrarySourceKind.CommunityFeed or LibrarySourceKind.LocalManifest;

    /// <summary>The placeholder of the filter box.</summary>
    public string SearchPlaceholder => IsManifest ? Strings.Manifest_FilterPlaceholder : Strings.Wizard_FilterDownloads;

    /// <summary>The choice tabs, once the catalogue has been read.</summary>
    public IReadOnlyList<FacetGroupViewModel> FacetGroups => _facetGroups;

    /// <summary>True when the catalogue has more than one choice group, shown as tabs (a remote catalogue's regions are a list instead).</summary>
    public bool HasFacetTabs => _facetGroups.Count > 1 && !IsS100Catalogue;

    /// <summary>True when the one group's title stands in for tabs (a community list shows its filter instead).</summary>
    public bool ShowsGroupTitle => _facetGroups.Count == 1 && (!IsSearchable || IsManifest);

    /// <summary>The tab being shown.</summary>
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

    /// <summary>The dialog title.</summary>
    public string Title => LibrarySourceText.Title(_kind, _known, IsEditing);

    /// <summary>The path being added, or the catalogue's URL.</summary>
    public string SourceDescription => IsOnlineFeed ? CatalogUri.AbsoluteUri : _path ?? string.Empty;

    /// <summary>The online catalogue being read.</summary>
    public Uri CatalogUri => LibrarySourceText.CatalogUri(_kind, _known);

    /// <summary>"Catalogue dated 2026-09-17", once the catalogue is loaded and declares a date.</summary>
    public string? CatalogueDateText => _catalogueDate is { } date
        ? string.Format(CultureInfo.CurrentCulture, Strings.Library_CatalogueDatedFormat, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        : null;

    /// <summary>True when the loaded catalogue is more than a year old (it may no longer be maintained).</summary>
    public bool IsCatalogueStale => LibrarySourceText.IsStale(_catalogueDate, _time.GetUtcNow());

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

    /// <summary>True while the catalogue is being read.</summary>
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

    /// <summary>Why the catalogue could not be read, if it failed.</summary>
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

    /// <summary>Filters the shown choices by label or value; ticks outside the filter are kept.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ShowMatchingChoices();
        }
    }

    /// <summary>"N cells · X MB" for the current selection.</summary>
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

            if (_draft is not null)
                _draft.IncludeAll = value;
            OnPropertyChanged(nameof(OnlySelected));
            UpdateSelection();
            OnScopeChanged();
        }
    }

    /// <summary>Sets <see cref="IncludeAll"/> (and the draft's) without the notifications, while initializing.</summary>
    private void SetIncludeAll(bool value)
    {
        _includeAll = value;
        if (_draft is not null)
            _draft.IncludeAll = value;
    }

    /// <summary>The inverse of <see cref="IncludeAll"/>, for radio-button binding.</summary>
    public bool OnlySelected
    {
        get => !_includeAll;
        set => IncludeAll = !value;
    }

    /// <summary>True once the catalogue has been read.</summary>
    public bool IsLoaded => _draft?.IsLoaded == true;

    /// <summary>True when the catalogue has been read and it lists more than one choice.</summary>
    public bool ShowsChoices => IsLoaded && !_isLoading && !IsSingleEntry;

    /// <summary>Every choice, shown or not, across all groups.</summary>
    private IEnumerable<LibraryChoice> AllOptions => _facetGroups.SelectMany(g => g.AllOptions);

    /// <summary>How many choices are ticked, across all groups.</summary>
    public int SelectedCount => AllOptions.Count(o => o.IsSelected);

    /// <summary>True when any choice is ticked.</summary>
    public bool HasSelection => AllOptions.Any(o => o.IsSelected);

    /// <summary>
    /// True when the loaded catalogue lists exactly one download (the USACE
    /// buoy overlay, a one-entry community list): there is nothing to choose.
    /// </summary>
    public bool IsSingleEntry => SingleEntry is not null;

    /// <summary>The catalogue's only download, when it lists just one; otherwise <see langword="null"/>.</summary>
    public LibraryChoice? SingleEntry => Scope?.SingleEntry;

    /// <summary>"12,345 cells · 1.2 GB" (or "N downloads · sizes unknown") for the whole catalogue.</summary>
    public string EverythingSummary => Scope is { IsLoaded: true } scope ? scope.EverythingSummary : string.Empty;

    /// <summary>The "Only what I select" sub-line: the selection summary, or "Nothing selected yet".</summary>
    public string OnlySelectedSummary => HasSelection ? _selectionSummary : Strings.Wizard_NothingSelected;

    /// <summary>The summary under the choices.</summary>
    public string ScopeSummary => _draft?.ScopeSummary ?? string.Empty;

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
    public string CatalogueDetail => LibrarySourceText.CatalogueDetail(CatalogUri, _catalogueDate);

    /// <summary>" · over a year old; may no longer be maintained" for a stale catalogue, else empty.</summary>
    public string CatalogueStaleSuffix => IsCatalogueStale ? Strings.Wizard_StaleSuffix : string.Empty;

    /// <summary>The review's "Includes" line.</summary>
    public string ReviewIncludes =>
        _includeAll || IsSingleEntry
            ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_Review_EverythingFormat, EverythingSummary)
            : string.Format(CultureInfo.CurrentCulture, Strings.Wizard_Review_SelectionFormat, DescribeSelection(), _selectionSummary);

    private KnownCatalogueCoverage Coverage => _known?.Coverage ?? KnownCatalogueCoverage.Polygons;

    /// <summary>The review's "Shown on the map" line.</summary>
    public string ReviewCoverage => Coverage switch
    {
        KnownCatalogueCoverage.Polygons => Strings.Wizard_Review_CoveragePolygons,
        KnownCatalogueCoverage.BoundingBoxes => Strings.Wizard_Review_CoverageBoxes,
        _ => Strings.Wizard_Review_CoverageNone,
    };

    /// <summary>True when nothing is shown on the map until an entry is downloaded.</summary>
    public bool IsReviewCoverageWarning => Coverage == KnownCatalogueCoverage.None;

    private bool HasEditions => _known?.Editions ?? true;

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
            : IsSearchable && _searchText.Trim().Length > 0 ? Strings.Wizard_SelectShown : Strings.Wizard_SelectAll;

    /// <summary>Ticks every shown value of the shown tab, or unticks them when all are ticked.</summary>
    public ICommand ToggleShownCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Clears every tick (meaning "everything").</summary>
    public ICommand SelectNoneCommand { get; }

    /// <summary>
    /// Prepares the dialog for adding the local <paramref name="path"/>,
    /// preselecting <paramref name="targetCollectionId"/> when it names a collection.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is an online kind (use the known-catalogue overload).</exception>
    public void Initialize(LibrarySourceKind kind, string path, Guid? targetCollectionId)
        => Initialize(kind, path, targetCollectionId, known: null, LibrarySourceDraft.ForPath(kind, path));

    /// <summary>
    /// Prepares the dialog for adding a scope of the known online catalogue
    /// <paramref name="known"/> (issue #670).
    /// </summary>
    public void Initialize(KnownCatalogueSource known, Guid? targetCollectionId)
    {
        ArgumentNullException.ThrowIfNull(known);
        Initialize(LibrarySourceKinds.Of(known.Format), null, targetCollectionId, known, LibrarySourceDraft.ForCatalogue(known, _readers, _time));
    }

    private void Initialize(LibrarySourceKind kind, string? path, Guid? targetCollectionId, KnownCatalogueSource? known, LibrarySourceDraft? draft)
    {
        if (Scope is { } previous)
            previous.Changed -= OnScopeSizesChanged;
        _kind = kind;
        _path = path;
        _known = known;
        _draft = draft;
        if (Scope is { } scope)
            scope.Changed += OnScopeSizesChanged;
        _editing = null;
        _searchText = string.Empty;
        _catalogueDate = null;
        _includeAll = true;
        _nameEdited = false;
        _facetGroups = [];
        _selectedFacetGroup = null;
        _selectionSummary = string.Empty;
        LoadError = null;
        ExistingCollections = _library.Collections.Where(c => !c.IsSession).ToArray();
        _selectedCollection = targetCollectionId is { } id
            ? ExistingCollections.FirstOrDefault(c => c.Id == id)
            : ExistingCollections.FirstOrDefault();
        _createNew = targetCollectionId is null || _selectedCollection is null;
        _newCollectionName = FeedName ?? DefaultName(path);

        OnPropertyChanged(string.Empty);
        RefreshCanConfirm();
    }

    /// <summary>Reads the catalogue (or manifest) and shows its choices; nothing to read for a folder, exchange set or S-128 catalogue.</summary>
    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (Scope is not { } scope)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            var error = await scope.LoadAsync(cancellationToken).ConfigureAwait(true);

            // A manifest shows every problem in it rather than a single error, and no groups.
            if (IsManifest || error is null)
                ShowGroups(scope);
            if (!IsManifest)
                LoadError = error;
            SetCatalogueDate(scope.CatalogueDate);
            UpdateSelection();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>Re-reads what a scope changed on its own (a remote catalogue's sizes arriving).</summary>
    private void OnScopeSizesChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, Scope))
            return;
        UpdateSelection();
        OnScopeChanged();
    }

    /// <summary>Shows the scope's choice groups, following their ticks, and keeps the shown tab where it still exists.</summary>
    private void ShowGroups(LibraryCatalogueScope scope)
    {
        foreach (var option in AllOptions)
            option.PropertyChanged -= OnFacetChanged;

        var shown = _selectedFacetGroup?.Title;
        _facetGroups = [.. scope.Groups.Select(g => new FacetGroupViewModel(g))];
        foreach (var option in AllOptions)
            option.PropertyChanged += OnFacetChanged;
        _selectedFacetGroup = _facetGroups.FirstOrDefault(g => g.Title == shown) ?? _facetGroups.FirstOrDefault();
        ShowMatchingChoices();

        OnPropertyChanged(nameof(FacetGroups));
        OnPropertyChanged(nameof(HasFacetTabs));
        OnPropertyChanged(nameof(ShowsGroupTitle));
        OnPropertyChanged(nameof(SelectedFacetGroup));
        RaiseRegionsChanged();
    }

    private void ShowMatchingChoices()
    {
        var text = IsSearchable ? _searchText.Trim() : string.Empty;
        foreach (var group in _facetGroups)
            group.Filter(text);
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

    /// <summary>The default collection name: the online catalogue's, the manifest's title, or null for other local sources.</summary>
    private string? FeedName =>
        LibrarySourceText.FeedName(_kind, _known) ?? (IsManifest ? ManifestBaseName : null);

    private bool CanConfirm =>
        !_isLoading && _draft?.CanBuild == true
        && (IsEditing || (_createNew ? !string.IsNullOrWhiteSpace(_newCollectionName) : _selectedCollection is not null));

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

    /// <summary>True when the source being added is online and not SECOM (which has its own sync option), so it can be kept downloaded (#809).</summary>
    public bool CanKeepDownloaded => IsOnlineFeed && !IsSecom;

    /// <summary>True to keep the new source's items downloaded and current on each refresh (#809).</summary>
    public bool KeepDownloaded
    {
        get => CanKeepDownloaded && _draft?.KeepDownloaded == true;
        set
        {
            if (!CanKeepDownloaded || _draft is null || _draft.KeepDownloaded == value)
                return;
            _draft.KeepDownloaded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the new source is shown on the map (#809): <see langword="null"/>
    /// for the kind's default (on for a synced SECOM service, otherwise off).
    /// </summary>
    internal bool? ShowOnMap
    {
        get => _draft?.ShowOnMap;
        set
        {
            if (_draft is not null)
                _draft.ShowOnMap = value;
        }
    }

    /// <summary>Builds the source the dialog describes.</summary>
    /// <exception cref="InvalidOperationException">The source cannot be built yet.</exception>
    internal CollectionSource BuildSource()
    {
        if (_draft is null)
            throw new InvalidOperationException($"Cannot build a {_kind} source.");

        // Choosing a manifest source's groups again keeps the source, with the new path and groups.
        if (_editing is { Source: var existing } && ManifestScope is { } manifest)
        {
            var edited = existing with { Path = manifest.ManifestPath, Filter = manifest.CurrentFilter };
            return ShowOnMap is { } show ? edited with { ShowOnMap = show } : edited;
        }

        // A manifest source is named like the collection would be, so an added-to-existing
        // source reads "IC-ENC — Belgium"; a new collection's source takes its name.
        return _draft.Build(_createNew ? _newCollectionName : null);
    }

    /// <summary>Describes the ticked values ("Alaska, Hawaii"), or <see langword="null"/> when nothing is ticked.</summary>
    private string? DescribeSelection() => Scope?.DescribeSelection();

    private void OnFacetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryChoice.IsSelected))
            return;

        // Ticking a value means "only what I select"; unticking leaves the choice alone.
        if (sender is LibraryChoice { IsSelected: true })
            IncludeAll = false;
        UpdateSelection();
        OnScopeChanged();
    }

    private void ClearFacetSelection()
    {
        foreach (var option in AllOptions.ToArray())
            option.IsSelected = false;
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
        if (IsS100Forecast)
        {
            OnPropertyChanged(nameof(HasForecastShapes));
            OnPropertyChanged(nameof(ForecastEndedNote));
            OnPropertyChanged(nameof(HasForecastEndedNote));
        }
        if (IsSecom)
        {
            OnPropertyChanged(nameof(SecomSync));
            OnPropertyChanged(nameof(SecomSyncHint));
        }
    }

    /// <summary>Sums the selection and, until the user names the collection, follows it in the suggested name.</summary>
    private void UpdateSelection()
    {
        if (Scope is not { IsLoaded: true } scope)
            return;

        SelectionSummary = scope.SelectionSummary;

        // In "Choose groups…" mode the collection keeps its name.
        if (FeedName is null || IsEditing || !_createNew || _nameEdited)
            return;
        if (SetProperty(ref _newCollectionName, _draft!.SuggestedName, nameof(NewCollectionName)))
            RefreshCanConfirm();
    }

    private static string DefaultName(string? path) => LibrarySourceText.DefaultName(path);
}
