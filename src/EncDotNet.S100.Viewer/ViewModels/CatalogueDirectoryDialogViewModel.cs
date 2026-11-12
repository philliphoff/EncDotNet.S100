using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the source step of "Add online source" (issues #670,
/// online-catalogue-dialog D6/D7): lists the curated known sources by
/// category (charts; depth, tides and currents; weather and ocean; SECOM
/// services) and by region within a category, plus the sources the user added
/// by URL (recognised by their root element and kept in a
/// <see cref="UserCatalogueStore"/>) under My sources. A search spans every
/// category; the wizard reads <see cref="SelectedEntry"/>.
/// </summary>
internal sealed class CatalogueDirectoryDialogViewModel : ViewModelBase
{
    private readonly IReadOnlyList<KnownCatalogueSource> _known;
    private readonly Action<Uri> _openUrl;
    private readonly AddCatalogueByUrlWorkflow _addByUrl;
    private readonly SecomServiceDirectory _secom;
    private readonly List<KnownCatalogueSource> _user;
    private IReadOnlyList<CatalogueEntryViewModel> _entries = [];
    private IReadOnlyList<CatalogueEntryViewModel> _rows = [];
    private CatalogueEntryViewModel? _selected;
    private string _searchText = string.Empty;
    private KnownCatalogueCategory _selectedCategory = KnownCatalogueCategory.Charts;
    private bool _keepCategory;
    private string _catalogueUrl = string.Empty;
    private string? _urlError;
    private string? _urlSuccess;
    private bool _isChecking;
    private List<KnownCatalogueSource> _registry = [];
    private bool _registryLoaded;
    private bool _isLoadingRegistry;
    private string? _registryText;
    private string? _registryError;

    /// <summary>Creates the directory.</summary>
    /// <param name="sources">The curated sources (<see cref="KnownCatalogueSources.All"/>).</param>
    /// <param name="urls">Opens a provider's page.</param>
    /// <param name="addByUrl">Adds sources by URL and keeps the user's own.</param>
    /// <param name="secomServices">Lists and probes services from the SECOM service registry.</param>
    /// <param name="session">The last category and source, for the app's lifetime (Q5).</param>
    public CatalogueDirectoryDialogViewModel(
        IReadOnlyList<KnownCatalogueSource> sources,
        IUrlOpener urls,
        AddCatalogueByUrlWorkflow addByUrl,
        SecomServiceDirectory secomServices,
        CatalogueDirectorySession session)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(addByUrl);
        ArgumentNullException.ThrowIfNull(secomServices);
        ArgumentNullException.ThrowIfNull(session);
        _secom = secomServices;
        ShowRegistryCommand = new AsyncRelayCommand(() => LoadRegistryAsync(refresh: false), () => CanShowRegistry);
        RefreshRegistryCommand = new AsyncRelayCommand(() => LoadRegistryAsync(refresh: true), () => CanRefreshRegistry);
        _known = sources;
        _openUrl = uri => urls.Open(uri.AbsoluteUri);
        _addByUrl = addByUrl;
        _user = [.. addByUrl.Added];

        Categories = [.. CategoryOrder.Select(c => new CatalogueCategoryViewModel(c, () => SelectCategory(c)))];
        AddUrlCommand = new AsyncRelayCommand(AddUrlAsync, () => CanAddUrl);
        ShowAddByUrlCommand = new RelayCommand(ShowAddByUrl);

        // Reopened in the same app session: back to the last category and its source (Q5).
        _lastSourceIds = session.LastSourceIds;
        var lastCategory = session.LastCategory;
        Rebuild(selectId: lastCategory is { } last ? _lastSourceIds.GetValueOrDefault(last) : null);
        if (lastCategory is { } category)
            SelectCategory(category);
        _session = session;
        Remember();
    }

    /// <summary>The session, once the dialog has opened on its last choice (so opening doesn't overwrite it).</summary>
    private readonly CatalogueDirectorySession? _session;

    /// <summary>The source last selected in each category (the session's, when there is one).</summary>
    private readonly Dictionary<KnownCatalogueCategory, string> _lastSourceIds;

    /// <summary>Records the open category for the next time the dialog opens.</summary>
    private void Remember()
    {
        if (_session is not null)
            _session.LastCategory = _selectedCategory;
    }

    /// <summary>The categories in the order the sidebar lists them.</summary>
    private static readonly KnownCatalogueCategory[] CategoryOrder =
    [
        KnownCatalogueCategory.Charts,
        KnownCatalogueCategory.Depth,
        KnownCatalogueCategory.Weather,
        KnownCatalogueCategory.Secom,
        KnownCatalogueCategory.User,
    ];

    /// <summary>
    /// Every source (not filtered by the search or category): by category,
    /// then by region in the order the curated list first names each region,
    /// by name within a region; registry services follow the curated SECOM
    /// services, and the user's own come last.
    /// </summary>
    public IReadOnlyList<CatalogueEntryViewModel> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    /// <summary>
    /// The list as shown: the open category's sources under non-selectable
    /// region headers, or while searching, the matches from every category
    /// under category headers.
    /// </summary>
    public IReadOnlyList<CatalogueEntryViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>
    /// The highlighted catalogue. It is kept while the search hides it, so a
    /// header or the list clearing its selection never loses the choice.
    /// </summary>
    public CatalogueEntryViewModel? SelectedEntry
    {
        get => _selected;
        set
        {
            if (value is null || value.IsGroupHeader || ReferenceEquals(value, _selected))
            {
                // Put the list's highlight back on the kept choice.
                OnPropertyChanged();
                return;
            }

            if (_selected is not null)
                _selected.IsSelected = false;
            _selected = value;
            _selected.IsSelected = true;
            _lastSourceIds[value.Category] = value.Source.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanContinueWithSelection));
            OnPropertyChanged(nameof(HasSelection));

            // The open category follows the choice, so clearing a search (or
            // coming back to the step) shows it in its own category.
            if (value.Category != _selectedCategory && !_keepCategory)
            {
                _selectedCategory = value.Category;
                OnPropertyChanged(nameof(SelectedCategory));
                if (!IsSearching)
                    ShowMatches();
                else
                    UpdateCategories();
            }
            if (value.IsRegistry && value.Reachability is null && !value.IsProbing)
                _ = ProbeAsync(value);
            Remember();
        }
    }

    /// <summary>Selects nothing, for a category with no sources (My sources before any is added).</summary>
    private void ClearSelection()
    {
        if (_selected is null)
            return;
        _selected.IsSelected = false;
        _selected = null;
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(CanContinueWithSelection));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>
    /// True when a catalogue is chosen. A registry service that can't be read
    /// yet continues too: the Connection step chooses its identity, and it can
    /// be added to wait (#883).
    /// </summary>
    public bool CanContinueWithSelection => _selected is not null;

    /// <summary>Lists SECOM services from the MCP service registry (#822); the registry is only asked when this runs.</summary>
    public ICommand ShowRegistryCommand { get; }

    /// <summary>Reads the registry again, past its cache, once it is listed.</summary>
    public ICommand RefreshRegistryCommand { get; }

    /// <summary>True when the listed registry can be read again.</summary>
    public bool CanRefreshRegistry => _registryLoaded && !_isLoadingRegistry;

    /// <summary>
    /// Raised when the user follows a registry service's fix link
    /// ("Set up an identity…"), with the Keys &amp; certificates tab to open (#880).
    /// </summary>
    public event EventHandler<Keys.KeysTab>? KeysRequested;

    private void OnFixRequested(Keys.KeysTab tab) => KeysRequested?.Invoke(this, tab);

    /// <summary>Raised with the text to put on the clipboard (a source's catalogue URL); the view copies it.</summary>
    public event EventHandler<string>? CopyRequested;

    private void OnCopyRequested(string text) => CopyRequested?.Invoke(this, text);

    /// <summary>True when there is a chosen source to show in the details pane.</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>True when the registry can be listed and is not listed yet.</summary>
    public bool CanShowRegistry => !_registryLoaded && !_isLoadingRegistry;

    /// <summary>True while the registry is being read.</summary>
    public bool IsLoadingRegistry
    {
        get => _isLoadingRegistry;
        private set
        {
            if (SetProperty(ref _isLoadingRegistry, value))
            {
                OnPropertyChanged(nameof(CanShowRegistry));
                OnPropertyChanged(nameof(CanRefreshRegistry));
                UpdateCategories();
                ((AsyncRelayCommand)ShowRegistryCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)RefreshRegistryCommand).NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"43 SECOM services from the MCP registry · 18 unusable entries hidden", once listed.</summary>
    public string? RegistryText
    {
        get => _registryText;
        private set => SetProperty(ref _registryText, value);
    }

    /// <summary>Why the registry could not be listed, or <see langword="null"/>.</summary>
    public string? RegistryError
    {
        get => _registryError;
        private set
        {
            if (SetProperty(ref _registryError, value))
            {
                OnPropertyChanged(nameof(HasRegistryError));
                ShowMatches(); // the registry row shows it
            }
        }
    }

    /// <summary>True when <see cref="RegistryError"/> is set.</summary>
    public bool HasRegistryError => _registryError is not null;

    /// <summary>True when the registry has been listed.</summary>
    public bool IsRegistryLoaded => _registryLoaded;

    private async Task LoadRegistryAsync(bool refresh)
    {
        IsLoadingRegistry = true;
        RegistryError = null;
        try
        {
            // S-100 data services only: the registry also lists route exchange, ship
            // reporting and other services the Library has nothing to show for.
            var listing = await _secom.ListS100Async(refresh, cancellationToken: CancellationToken.None).ConfigureAwait(true);
            _registry = [.. listing.Services.Select(KnownCatalogueSources.FromRegistry)];
            _registryLoaded = true;
            OnPropertyChanged(nameof(IsRegistryLoaded));
            RegistryText = string.Format(CultureInfo.CurrentCulture,
                listing.Stale is null ? Strings.Library_RegistrySummaryFormat : Strings.Library_RegistrySummaryStaleFormat,
                listing.Services.Count, listing.Hidden, listing.FetchedAt.ToLocalTime());
            Rebuild(_selected?.Source.Id);
        }
        catch (HttpRequestException ex)
        {
            RegistryError = string.Format(CultureInfo.CurrentCulture, Strings.Library_RegistryErrorFormat, ex.Message);
        }
        finally
        {
            IsLoadingRegistry = false;
        }
    }

    /// <summary>Finds out whether a registry service can be read, and shows it on its row.</summary>
    private async Task ProbeAsync(CatalogueEntryViewModel entry)
    {
        entry.IsProbing = true;
        try
        {
            entry.SetReachability(await _secom.ProbeAsync(entry.Source.CatalogUri, CancellationToken.None).ConfigureAwait(true));
        }
        finally
        {
            entry.IsProbing = false;
        }

        if (ReferenceEquals(entry, _selected))
        {
            OnPropertyChanged(nameof(SelectedEntry));
            OnPropertyChanged(nameof(CanContinueWithSelection));
        }
    }

    /// <summary>Filters every category by name, provider, region or format.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsSearching));
                ShowMatches();
            }
        }
    }

    /// <summary>True while <see cref="SearchText"/> filters the list.</summary>
    public bool IsSearching => _searchText.Trim().Length > 0;

    /// <summary>The sidebar: each category with its count (its match count while searching).</summary>
    public IReadOnlyList<CatalogueCategoryViewModel> Categories { get; }

    /// <summary>
    /// The open category. Choosing one clears the search and selects the
    /// source last selected in it, or its first; it follows
    /// <see cref="SelectedEntry"/> so the choice is shown in its own category.
    /// </summary>
    public KnownCatalogueCategory SelectedCategory
    {
        get => _selectedCategory;
        set => SelectCategory(value);
    }

    private void SelectCategory(KnownCatalogueCategory category)
    {
        var changed = category != _selectedCategory;
        _selectedCategory = category;
        if (changed)
            OnPropertyChanged(nameof(SelectedCategory));
        Remember();

        // The selection always belongs to the open category, so the details and
        // Next never act on a source the list doesn't show.
        if (_selected?.Category != category)
        {
            var inCategory = _entries.Where(e => e.Category == category).ToArray();
            var next = _lastSourceIds.TryGetValue(category, out var lastId)
                ? inCategory.FirstOrDefault(e => e.Source.Id == lastId) ?? inCategory.FirstOrDefault()
                : inCategory.FirstOrDefault();
            if (next is not null)
                SelectedEntry = next;
            else
                ClearSelection();
        }
        if (_searchText.Length > 0)
        {
            _searchText = string.Empty;
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(IsSearching));
        }
        else if (!changed)
        {
            return;
        }

        ShowMatches();
    }

    /// <summary>True when the search matches no source in any category.</summary>
    public bool HasNoMatches => IsSearching && _rows.Count == 0;

    /// <summary>"No source matches “weather fax”."</summary>
    public string NoMatchesText => string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryNoMatchesFormat, _searchText.Trim());

    /// <summary>True when the open category lists nothing (My sources before anything is added).</summary>
    public bool IsCategoryEmpty => !IsSearching && _rows.Count == 0;

    /// <summary>What an empty category says: My sources points at the URL form above it.</summary>
    public string EmptyCategoryText => ShowsUrlForm ? Strings.Library_DirectoryNoSourcesAddHint : Strings.Library_DirectoryNoSources;

    /// <summary>
    /// Opens My sources, where the URL form is (D10), from the sidebar's "Add
    /// by URL" link or the empty search ("Add it by URL"). A search that is a
    /// URL is carried into the form.
    /// </summary>
    public ICommand ShowAddByUrlCommand { get; }

    /// <summary>Raised when the URL form should take keyboard focus.</summary>
    public event EventHandler? UrlEntryRequested;

    private void ShowAddByUrl()
    {
        var text = _searchText.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var typed) && (typed.Scheme == Uri.UriSchemeHttps || typed.Scheme == Uri.UriSchemeHttp))
            CatalogueUrl = text;
        SelectCategory(KnownCatalogueCategory.User);
        UrlEntryRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True when the URL form heads the list: My sources is open and nothing is searched.</summary>
    public bool ShowsUrlForm => !IsSearching && _selectedCategory == KnownCatalogueCategory.User;

    /// <summary>The URL of a catalogue to add.</summary>
    public string CatalogueUrl
    {
        get => _catalogueUrl;
        set
        {
            if (SetProperty(ref _catalogueUrl, value ?? string.Empty))
            {
                UrlError = null;
                if (_catalogueUrl.Length > 0)
                    UrlSuccess = null;
                ((AsyncRelayCommand)AddUrlCommand).NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the URL could not be added, if it could not.</summary>
    public string? UrlError
    {
        get => _urlError;
        private set
        {
            if (SetProperty(ref _urlError, value))
            {
                OnPropertyChanged(nameof(HasUrlError));
                OnPropertyChanged(nameof(ShowsUrlHelp));
            }
        }
    }

    public bool HasUrlError => _urlError is not null;

    /// <summary>"Recognised as chartcatalogs and added under Custom.", once a URL has been added.</summary>
    public string? UrlSuccess
    {
        get => _urlSuccess;
        private set
        {
            if (SetProperty(ref _urlSuccess, value))
                OnPropertyChanged(nameof(ShowsUrlHelp));
        }
    }

    /// <summary>True when neither an error nor a success message replaces the help text.</summary>
    public bool ShowsUrlHelp => _urlError is null && _urlSuccess is null;

    /// <summary>True while a URL is being fetched and recognised.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
                ((AsyncRelayCommand)AddUrlCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Fetches <see cref="CatalogueUrl"/>, recognises its format and adds it to the user's catalogues.</summary>
    public ICommand AddUrlCommand { get; }

    private bool CanAddUrl => !_isChecking && !string.IsNullOrWhiteSpace(_catalogueUrl);

    private async Task AddUrlAsync()
    {
        IsChecking = true;
        UrlError = null;
        AddCatalogueByUrlOutcome outcome;
        try
        {
            outcome = await _addByUrl.RunAsync(new AddCatalogueByUrlRequest(_catalogueUrl, [.. _known, .. _user]), CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsChecking = false;
        }

        if (outcome.Error is { } error)
        {
            UrlError = error;
            return;
        }

        var source = outcome.Source!;
        if (outcome.AlreadyListed)
        {
            SearchText = string.Empty;
            SelectedEntry = _entries.FirstOrDefault(e => e.Source.Id == source.Id);
            CatalogueUrl = string.Empty;
            return;
        }

        _user.RemoveAll(s => s.Id == source.Id);
        _user.Add(source);
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Rebuild(source.Id);
        CatalogueUrl = string.Empty;
        UrlSuccess = string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryUrlRecognisedFormat,
            CatalogueEntryViewModel.FormatLabel(source.Format));
    }

    /// <summary>
    /// Selects <paramref name="source"/> (matched by id or catalogue URL),
    /// listing it under Custom for this session when it is not listed yet
    /// (a shared feed the user connected to).
    /// </summary>
    public void Preselect(KnownCatalogueSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_known.Concat(_user).FirstOrDefault(s => s.Id == source.Id || s.CatalogUri == source.CatalogUri) is { } listed)
        {
            SearchText = string.Empty;
            SelectedEntry = _entries.First(e => e.Source.Id == listed.Id);
            return;
        }

        _user.Add(source);
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Rebuild(source.Id);
    }

    private void Remove(KnownCatalogueSource source)
    {
        // Stay in the category the source was removed from, on another of the user's own if any.
        var category = _selectedCategory;
        var next = _selected?.Source.Id == source.Id ? _user.FirstOrDefault(s => s.Id != source.Id)?.Id : _selected?.Source.Id;
        _user.RemoveAll(s => s.Id == source.Id);
        _addByUrl.Remove(source.Id);
        Rebuild(next);
        if (_selectedCategory != category && !IsSearching)
            SelectCategory(category);
    }

    private void Rebuild(string? selectId)
    {
        // Within a category: regions in the order the curated list first names them,
        // registry services next (by product), then the user's own.
        var regionOrder = _known.Select(s => s.Region.FirstOrDefault() ?? string.Empty).Distinct().ToList();
        var previous = _entries.Where(e => e.IsRegistry).ToDictionary(e => e.Source.Id, StringComparer.Ordinal);
        Entries = _known
            .OrderBy(s => regionOrder.IndexOf(s.Region.FirstOrDefault() ?? string.Empty))
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new CatalogueEntryViewModel(s, _openUrl, copyText: OnCopyRequested, remove: null, openFix: null))
            .Concat(_registry
                .OrderBy(s => s.Pilot)
                .ThenBy(s => s.Product, StringComparer.Ordinal)
                .ThenBy(s => s.Name, StringComparer.CurrentCulture)
                // Keep what was already found out about a service.
                .Select(s => previous.TryGetValue(s.Id, out var kept) ? kept : new CatalogueEntryViewModel(s, _openUrl, isRegistry: true, openFix: OnFixRequested, copyText: OnCopyRequested, remove: null)))
            .Concat(_user.Select(s => new CatalogueEntryViewModel(s, _openUrl, Remove, copyText: OnCopyRequested, openFix: null)))
            .OrderBy(e => Array.IndexOf(CategoryOrder, e.Category)) // stable: keeps the order above within a category
            .ToArray();

        // Opens on the first category's first source (NOAA ENC in the built-in list).
        // Keeping the same choice (listing the registry, say) keeps the open category too.
        var keep = _entries.FirstOrDefault(e => e.Source.Id == selectId) ?? _entries.FirstOrDefault();
        _keepCategory = keep is not null && keep.Source.Id == _selected?.Source.Id;
        _selected = null;
        if (keep is not null)
            SelectedEntry = keep;
        else
        {
            OnPropertyChanged(nameof(SelectedEntry));
            OnPropertyChanged(nameof(HasSelection));
        }

        _keepCategory = false;
        ShowMatches();
    }

    private void ShowMatches()
    {
        var text = _searchText.Trim();
        var rows = new List<CatalogueEntryViewModel>();
        if (text.Length > 0)
        {
            // Every category's matches, under the category's name.
            foreach (var group in _entries.Where(e => e.Matches(text)).GroupBy(e => e.Category))
            {
                rows.Add(CatalogueEntryViewModel.Header(CatalogueCategoryViewModel.LabelOf(group.Key)));
                foreach (var entry in group)
                    entry.ShowsPlace = true; // no region header says where
                rows.AddRange(group);
            }
        }
        else
        {
            // The open category, by region; registry services under their own header,
            // after the registry row (D10). The user's own sources need no header:
            // the category says whose they are.
            foreach (var group in _entries.Where(e => e.Category == _selectedCategory && !e.IsRegistry)
                .GroupBy(e => e.IsUser ? string.Empty : e.Source.Region.FirstOrDefault() ?? string.Empty))
            {
                var entries = group.ToArray();
                var title = GroupTitle(group.Key, entries);
                if (group.Key.Length > 0)
                    rows.Add(CatalogueEntryViewModel.Header(title));

                // The second line names the country unless the header already does (A8).
                foreach (var entry in entries)
                    entry.ShowsPlace = entry.Place is { } place && !title.Contains(place, StringComparison.CurrentCulture);
                rows.AddRange(entries);
            }

            if (_selectedCategory == KnownCatalogueCategory.Secom)
            {
                if (_registryLoaded)
                {
                    rows.Add(CatalogueEntryViewModel.Header(Strings.Library_RegistryRegion));
                    rows.Add(CatalogueEntryViewModel.RegistryRow(this));
                    rows.AddRange(_entries.Where(e => e.IsRegistry));
                }
                else
                {
                    rows.Add(CatalogueEntryViewModel.RegistryRow(this));
                }
            }
        }

        Rows = rows;
        UpdateCategories();
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(IsCategoryEmpty));
        OnPropertyChanged(nameof(ShowsUrlForm));
        OnPropertyChanged(nameof(NoMatchesText));
        OnPropertyChanged(nameof(EmptyCategoryText));

        // Replacing the rows clears the list's highlight; restore it on the kept choice.
        OnPropertyChanged(nameof(SelectedEntry));
    }

    /// <summary>Updates the sidebar's counts, the open category and the dimmed (no match) ones.</summary>
    private void UpdateCategories()
    {
        var text = _searchText.Trim();
        var searching = text.Length > 0;
        foreach (var category in Categories)
        {
            var count = _entries.Count(e => e.Category == category.Category && (!searching || e.Matches(text)));
            // Until the registry is listed, SECOM services may hold more than the curated ones.
            var more = !searching && category.Category == KnownCatalogueCategory.Secom && CanShowRegistry;
            category.Update(count, more, isOpen: !searching && category.Category == _selectedCategory, isDimmed: searching && count == 0);
        }
    }

    /// <summary>"Europe", or "North America · United States" when a region's catalogues all share the country.</summary>
    private static string GroupTitle(string region, IReadOnlyList<CatalogueEntryViewModel> entries)
    {
        if (entries.Count > 1
            && entries[0].Source.Region.Count > 1
            && entries.All(e => e.Source.Region.Count > 1 && e.Source.Region[1] == entries[0].Source.Region[1]))
        {
            return $"{region} · {entries[0].Source.Region[1]}";
        }

        return region;
    }
}
