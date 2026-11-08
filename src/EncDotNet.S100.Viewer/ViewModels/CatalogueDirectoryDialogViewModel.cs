using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

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
    private readonly Action<Uri>? _openUrl;
    private readonly UserCatalogueStore? _store;
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>>? _probe;
    private readonly List<KnownCatalogueSource> _user;
    private IReadOnlyList<CatalogueEntryViewModel> _entries = [];
    private IReadOnlyList<CatalogueEntryViewModel> _rows = [];
    private CatalogueEntryViewModel? _selected;
    private string _searchText = string.Empty;
    private KnownCatalogueCategory _selectedCategory = KnownCatalogueCategory.Charts;
    private string _catalogueUrl = string.Empty;
    private string? _urlError;
    private string? _urlSuccess;
    private bool _isChecking;
    private bool _isUrlPanelOpen;
    private readonly Func<CancellationToken, Task<SecomRegistryListing>>? _loadRegistry;
    private readonly Func<Uri, CancellationToken, Task<SecomProbeResult>>? _probeSecom;
    private List<KnownCatalogueSource> _registry = [];
    private bool _registryLoaded;
    private bool _isLoadingRegistry;
    private string? _registryText;
    private string? _registryError;

    public CatalogueDirectoryDialogViewModel(
        IReadOnlyList<KnownCatalogueSource> sources,
        Action<Uri>? openUrl = null,
        UserCatalogueStore? userCatalogues = null,
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null,
        Func<CancellationToken, Task<SecomRegistryListing>>? loadSecomRegistry = null,
        Func<Uri, CancellationToken, Task<SecomProbeResult>>? probeSecom = null)
    {
        _loadRegistry = loadSecomRegistry;
        _probeSecom = probeSecom;
        ShowRegistryCommand = new AsyncRelayCommand(ShowRegistryAsync, () => CanShowRegistry);
        ArgumentNullException.ThrowIfNull(sources);
        _known = sources;
        _openUrl = openUrl;
        _store = userCatalogues;
        _probe = probe;
        _user = [.. userCatalogues?.Sources ?? []];

        Categories = [.. CategoryOrder.Select(c => new CatalogueCategoryViewModel(c, () => SelectCategory(c)))];
        AddUrlCommand = new AsyncRelayCommand(AddUrlAsync, () => CanAddUrl);
        OpenUrlPanelCommand = new RelayCommand(() => IsUrlPanelOpen = true);
        CloseUrlPanelCommand = new RelayCommand(() => IsUrlPanelOpen = false);
        Rebuild(selectId: null);
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanContinueWithSelection));
            OnPropertyChanged(nameof(HasSelection));

            // The open category follows the choice, so clearing a search (or
            // coming back to the step) shows it in its own category.
            if (value.Category != _selectedCategory)
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
        }
    }

    /// <summary>
    /// True when a catalogue is chosen. A registry service that can't be read
    /// yet continues too: the Connection step chooses its identity, and it can
    /// be added to wait (#883).
    /// </summary>
    public bool CanContinueWithSelection => _selected is not null;

    /// <summary>Lists SECOM services from the MCP service registry (#822); the registry is only asked when this runs.</summary>
    public ICommand ShowRegistryCommand { get; }

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
    public bool CanShowRegistry => _loadRegistry is not null && !_registryLoaded && !_isLoadingRegistry;

    /// <summary>True while the registry is being read.</summary>
    public bool IsLoadingRegistry
    {
        get => _isLoadingRegistry;
        private set
        {
            if (SetProperty(ref _isLoadingRegistry, value))
            {
                OnPropertyChanged(nameof(CanShowRegistry));
                UpdateCategories();
                ((AsyncRelayCommand)ShowRegistryCommand).NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(HasRegistryError));
        }
    }

    /// <summary>True when <see cref="RegistryError"/> is set.</summary>
    public bool HasRegistryError => _registryError is not null;

    /// <summary>True when the registry has been listed.</summary>
    public bool IsRegistryLoaded => _registryLoaded;

    private async Task ShowRegistryAsync()
    {
        if (_loadRegistry is null)
            return;

        IsLoadingRegistry = true;
        RegistryError = null;
        try
        {
            var listing = await _loadRegistry(CancellationToken.None).ConfigureAwait(true);
            // S-100 data services only: the registry also lists route exchange, ship
            // reporting and other services the Library has nothing to show for.
            var services = listing.Services.Where(s => s.IsS100Product).ToArray();
            _registry = [.. services.Select(KnownCatalogueSources.FromRegistry)];
            _registryLoaded = true;
            OnPropertyChanged(nameof(IsRegistryLoaded));
            RegistryText = string.Format(CultureInfo.CurrentCulture,
                listing.Stale is null ? Strings.Library_RegistrySummaryFormat : Strings.Library_RegistrySummaryStaleFormat,
                services.Length, listing.Listed - services.Length, listing.FetchedAt.ToLocalTime());
            Rebuild(_selected?.Source.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
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
        if (_probeSecom is null)
            return;
        entry.IsProbing = true;
        try
        {
            var result = await _probeSecom(entry.Source.CatalogUri, CancellationToken.None).ConfigureAwait(true);
            entry.SetReachability(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or TaskCanceledException)
        {
            entry.SetReachability(new SecomProbeResult(SecomReachability.Unreachable, ex.Message));
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
    /// The open category. Choosing one clears the search; it follows
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

    /// <summary>True when the open category lists nothing (My sources before anything is added).</summary>
    public bool IsCategoryEmpty => !IsSearching && _rows.Count == 0;

    /// <summary>True while the "Add a catalogue by URL" panel is expanded.</summary>
    public bool IsUrlPanelOpen
    {
        get => _isUrlPanelOpen;
        set => SetProperty(ref _isUrlPanelOpen, value);
    }

    public ICommand OpenUrlPanelCommand { get; }

    public ICommand CloseUrlPanelCommand { get; }

    /// <summary>True when catalogues can be added by URL.</summary>
    public bool CanAddByUrl => _probe is not null;

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

    private bool CanAddUrl => _probe is not null && !_isChecking && !string.IsNullOrWhiteSpace(_catalogueUrl);

    private async Task AddUrlAsync()
    {
        if (!Uri.TryCreate(_catalogueUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            UrlError = Strings.Library_DirectoryUrlInvalid;
            return;
        }

        // A catalogue already listed is simply selected.
        if (_known.Concat(_user).FirstOrDefault(s => s.CatalogUri == uri) is { } listed)
        {
            SearchText = string.Empty;
            SelectedEntry = _entries.FirstOrDefault(e => e.Source.Id == listed.Id);
            CatalogueUrl = string.Empty;
            return;
        }

        IsChecking = true;
        UrlError = null;
        try
        {
            var probe = await _probe!(uri, CancellationToken.None).ConfigureAwait(true);
            if (probe.Format is not { } format)
            {
                UrlError = probe.IsJson ? Strings.Library_DirectoryUrlJson
                    : probe.RootElement is { } root ? string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryUrlUnsupportedFormat, root)
                    : Strings.Library_DirectoryUrlNotXml;
                return;
            }

            var source = KnownCatalogueSources.FromUrl(uri, format, probe.Title);
            _user.RemoveAll(s => s.Id == source.Id);
            _user.Add(source);
            _store?.Add(source);
            _searchText = string.Empty;
            OnPropertyChanged(nameof(SearchText));
            Rebuild(source.Id);
            CatalogueUrl = string.Empty;
            UrlSuccess = string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryUrlRecognisedFormat,
                CatalogueEntryViewModel.FormatLabel(format));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            UrlError = ex.Message;
        }
        finally
        {
            IsChecking = false;
        }
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
        _store?.Remove(source.Id);
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
            .Select(s => new CatalogueEntryViewModel(s, _openUrl, copyText: OnCopyRequested))
            .Concat(_registry
                .OrderBy(s => s.Pilot)
                .ThenBy(s => s.Product, StringComparer.Ordinal)
                .ThenBy(s => s.Name, StringComparer.CurrentCulture)
                // Keep what was already found out about a service.
                .Select(s => previous.TryGetValue(s.Id, out var kept) ? kept : new CatalogueEntryViewModel(s, _openUrl, isRegistry: true, openFix: OnFixRequested, copyText: OnCopyRequested)))
            .Concat(_user.Select(s => new CatalogueEntryViewModel(s, _openUrl, Remove, copyText: OnCopyRequested)))
            .OrderBy(e => Array.IndexOf(CategoryOrder, e.Category)) // stable: keeps the order above within a category
            .ToArray();

        // Opens on the first category's first source (NOAA ENC in the built-in list).
        var keep = _entries.FirstOrDefault(e => e.Source.Id == selectId) ?? _entries.FirstOrDefault();
        _selected = null;
        if (keep is not null)
            SelectedEntry = keep;
        else
        {
            OnPropertyChanged(nameof(SelectedEntry));
            OnPropertyChanged(nameof(HasSelection));
        }

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
            // The open category, by region; registry services under their own header.
            // The user's own sources need no header: the category says whose they are.
            foreach (var group in _entries.Where(e => e.Category == _selectedCategory)
                .GroupBy(e => e.IsUser ? string.Empty : e.IsRegistry ? Strings.Library_RegistryRegion : e.Source.Region.FirstOrDefault() ?? string.Empty))
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
        }

        Rows = rows;
        UpdateCategories();
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(IsCategoryEmpty));

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

/// <summary>
/// One row in the directory: a source, with the text its row and details
/// show, or a region or category header (<see cref="IsGroupHeader"/>) above a group.
/// </summary>
internal sealed class CatalogueEntryViewModel : ViewModelBase
{
    private static readonly KnownCatalogueSource HeaderSource =
        new(string.Empty, string.Empty, string.Empty, [], KnownCatalogueFormat.NoaaEnc,
            new Uri("about:blank"), null, KnownCatalogueCoverage.None, false, false);

    private bool _isSelected;

    private SecomProbeResult? _reachability;
    private bool _isProbing;

    public CatalogueEntryViewModel(
        KnownCatalogueSource source,
        Action<Uri>? openUrl,
        Action<KnownCatalogueSource>? remove = null,
        bool isRegistry = false,
        Action<Keys.KeysTab>? openFix = null,
        Action<string>? copyText = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        CopyUrlCommand = new RelayCommand(() => copyText?.Invoke(CatalogUrl), () => copyText is not null);
        IsRegistry = isRegistry;
        IsUser = remove is not null;
        OpenFixCommand = new RelayCommand(
            () =>
            {
                if (Services.Secom.KeysNavigator.TabFor(FixCode) is { } tab)
                    openFix?.Invoke(tab);
            },
            () => openFix is not null);
        RemoveCommand = new RelayCommand(() => remove?.Invoke(source), () => remove is not null);
        OpenHomepageCommand = new RelayCommand(
            () => openUrl?.Invoke(source.Homepage!),
            () => openUrl is not null && source.Homepage is not null);
    }

    private CatalogueEntryViewModel(string title)
        : this(HeaderSource, null)
    {
        IsGroupHeader = true;
        GroupTitle = title;
    }

    /// <summary>A region header row.</summary>
    public static CatalogueEntryViewModel Header(string title) => new(title);

    /// <summary>True for a SECOM service listed from the service registry (#822).</summary>
    public bool IsRegistry { get; }

    /// <summary>
    /// The category the entry is listed under: the user's own sources (added
    /// by URL or a shared feed) under My sources, registry services under
    /// SECOM services, curated ones as the list says.
    /// </summary>
    public KnownCatalogueCategory Category =>
        IsUser ? KnownCatalogueCategory.User : IsRegistry ? KnownCatalogueCategory.Secom : Source.Category;

    /// <summary>Whether a registry service can be read, once probed; <see langword="null"/> before.</summary>
    public SecomReachability? Reachability => _reachability?.Reachability;

    /// <summary>True while the service is being probed.</summary>
    public bool IsProbing
    {
        get => _isProbing;
        set
        {
            if (SetProperty(ref _isProbing, value))
            {
                OnPropertyChanged(nameof(ReachabilityText));
                OnPropertyChanged(nameof(HasReachability));
            }
        }
    }

    /// <summary>True when the details show whether the service can be read (a registry service).</summary>
    public bool HasReachability => IsRegistry && (IsProbing || _reachability is not null);

    /// <summary>"Checking…", "Readable without a certificate", "Needs a certificate", …</summary>
    public string? ReachabilityText => !IsRegistry ? null
        : IsProbing ? Strings.Library_Reachability_Checking
        : _reachability?.Reachability switch
        {
            SecomReachability.Open => Strings.Library_Reachability_Open,
            SecomReachability.OpenWithCertificate => Strings.Library_Reachability_OpenWithCertificate,
            SecomReachability.NeedsCertificate => Strings.Library_Reachability_NeedsCertificate,
            SecomReachability.CertificateRefused => Strings.Library_Reachability_CertificateRefused,
            SecomReachability.NeedsSecom2Search => Strings.Library_Reachability_NeedsSecom2Search,
            SecomReachability.UntrustedServer => Strings.Library_Reachability_UntrustedServer,
            SecomReachability.Unreachable => Strings.Library_Reachability_Unreachable,
            _ => null,
        };

    /// <summary>True when the service cannot be added as things stand (shown in amber, not green).</summary>
    public bool IsReachabilityLimited => _reachability is { Reachability: not (SecomReachability.Open or SecomReachability.OpenWithCertificate) };

    /// <summary>
    /// Why the service cannot be added yet, for the row, and which trust
    /// anchor its server certificate is from when it is not one the system
    /// trusts (#829); <see langword="null"/> when there is nothing to say.
    /// </summary>
    public string? ReachabilityExplanation
    {
        get
        {
            var trust = _reachability?.ServerTrust;
            var why = _reachability?.Reachability switch
            {
                SecomReachability.NeedsCertificate => Strings.Library_Reachability_NeedsCertificateExplanation,
                SecomReachability.NeedsSecom2Search => _reachability.Identity is { } signer
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_Secom2SearchRejectedFormat, signer, _reachability.Detail)
                    : Strings.Library_Reachability_NeedsSecom2SearchExplanation,
                SecomReachability.OpenWithCertificate => string.Format(
                    CultureInfo.CurrentCulture, Strings.Library_Reachability_OpenWithCertificateExplanationFormat, _reachability.Identity),
                SecomReachability.CertificateRefused => string.Format(
                    CultureInfo.CurrentCulture, Strings.Library_Reachability_CertificateRefusedExplanationFormat, _reachability.Identity),
                SecomReachability.UntrustedServer => trust switch
                {
                    { Outcome: SecomServerTrustOutcome.WrongHost } => Strings.Library_Reachability_WrongHostExplanation,
                    { Outcome: SecomServerTrustOutcome.Expired, Anchor: { } expiredAnchor } =>
                        string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_ExpiredServerExplanationFormat, expiredAnchor),
                    { Outcome: SecomServerTrustOutcome.Revoked, Anchor: { } revokedAnchor } =>
                        string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_RevokedServerExplanationFormat, revokedAnchor),
                    _ => Strings.Library_Reachability_UntrustedServerExplanation,
                },
                SecomReachability.Unreachable => _reachability.Detail is { Length: > 0 } detail
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_UnreachableExplanationFormat, detail)
                    : null,
                _ => null,
            };
            var from = trust is { Outcome: SecomServerTrustOutcome.AnchorTrusted, Anchor: { } anchor }
                ? string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_ServerAnchorFormat, anchor)
                    + (trust.Revocation == SecomRevocationStatus.NotChecked ? " " + Strings.Library_Reachability_RevocationNotCheckedExplanation : null)
                : null;
            return why is null ? from : from is null ? why : $"{why} {from}";
        }
    }

    /// <summary>True when <see cref="ReachabilityExplanation"/> is set.</summary>
    public bool HasReachabilityExplanation => ReachabilityExplanation is not null;

    /// <summary>
    /// The certificate problem a Keys &amp; certificates tab can fix, as a
    /// <see cref="SecomDiagnosticCodes"/> value (#880): none set, refused or
    /// not accepted, or a server under no trusted authority.
    /// </summary>
    internal string? FixCode => _reachability switch
    {
        { Reachability: SecomReachability.NeedsCertificate } => SecomDiagnosticCodes.NeedsCertificate,
        { Reachability: SecomReachability.NeedsSecom2Search, Identity: null } => SecomDiagnosticCodes.NeedsSignedRequest,
        { Reachability: SecomReachability.NeedsSecom2Search or SecomReachability.CertificateRefused } => SecomDiagnosticCodes.CertificateRefused,
        { Reachability: SecomReachability.UntrustedServer, ServerTrust: null or { Outcome: SecomServerTrustOutcome.NotTrusted } } =>
            SecomDiagnosticCodes.ServerUntrusted,
        _ => null,
    };

    /// <summary>The fix link, e.g. "Set up an identity…", or <see langword="null"/>.</summary>
    public string? FixText => Services.Secom.KeysNavigator.ActionText(FixCode);

    /// <summary>True when the row offers <see cref="FixText"/>.</summary>
    public bool HasFix => FixText is not null && OpenFixCommand.CanExecute(null);

    /// <summary>Opens Settings → Keys &amp; certificates on the tab that fixes this service's problem.</summary>
    public RelayCommand OpenFixCommand { get; }

    /// <summary>Records what the probe found.</summary>
    internal void SetReachability(SecomProbeResult result)
    {
        _reachability = result;
        OnPropertyChanged(nameof(Reachability));
        OnPropertyChanged(nameof(ReachabilityText));
        OnPropertyChanged(nameof(HasReachability));
        OnPropertyChanged(nameof(IsReachabilityLimited));
        OnPropertyChanged(nameof(ReachabilityExplanation));
        OnPropertyChanged(nameof(HasReachabilityExplanation));
        OnPropertyChanged(nameof(FixText));
        OnPropertyChanged(nameof(HasFix));
    }

    /// <summary>True for a region header (not selectable).</summary>
    public bool IsGroupHeader { get; }

    /// <summary>The region header's text.</summary>
    public string? GroupTitle { get; }

    /// <summary>The region header as shown (upper case).</summary>
    public string? HeaderText => GroupTitle?.ToUpper(CultureInfo.CurrentCulture);

    /// <summary>True when this catalogue is the chosen one (its details are shown).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>The known source.</summary>
    public KnownCatalogueSource Source { get; }

    /// <summary>
    /// "NOAA ENC", "USACE IENC", "chartcatalogs" or "S-100 feed"; for a
    /// catalogue of one product, the product and where it is hosted ("S-102 · AWS").
    /// </summary>
    public string FormatText => Source.Product is { } product
        ? $"{product} · {HostLabel(Source.CatalogUri)}"
        : FormatLabel(Source.Format);

    /// <summary>"AWS" for Amazon Web Services (AWS Open Data); otherwise "Web".</summary>
    private static string HostLabel(Uri uri) =>
        uri.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) ? Strings.Library_Host_Aws : Strings.Library_Host_Web;

    /// <summary>The label for a catalogue format.</summary>
    public static string FormatLabel(KnownCatalogueFormat format) => format switch
    {
        KnownCatalogueFormat.UsaceIenc => Strings.Library_Format_UsaceIenc,
        KnownCatalogueFormat.ChartCatalogs => Strings.Library_Format_ChartCatalogs,
        KnownCatalogueFormat.S100Feed => Strings.Library_Format_S100Feed,
        KnownCatalogueFormat.S100ExchangeCatalogue => Strings.Library_Format_S100Catalogue,
        KnownCatalogueFormat.S100ForecastModels => Strings.Library_Format_S100Forecast,
        KnownCatalogueFormat.Secom => Strings.Library_Format_Secom,
        KnownCatalogueFormat.Weather => Strings.Library_Format_Weather,
        _ => Strings.Library_Format_NoaaEnc,
    };

    /// <summary>True when the catalogue's name, provider, region or format contains <paramref name="text"/>.</summary>
    public bool Matches(string text) =>
        text.Length == 0
        || Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
        || Source.Provider.Contains(text, StringComparison.CurrentCultureIgnoreCase)
        || Source.Region.Any(r => r.Contains(text, StringComparison.CurrentCultureIgnoreCase))
        || FormatText.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>True for a catalogue the user added by URL (it can be removed).</summary>
    public bool IsUser { get; }

    /// <summary>Removes a user-added catalogue from the list.</summary>
    public ICommand RemoveCommand { get; }

    public string Name => Source.Name;

    /// <summary>The row's accessible name: the region for a header, else the catalogue's name.</summary>
    public string? AccessibleName => IsGroupHeader ? GroupTitle : Name;

    /// <summary>"U.S. Army Corps of Engineers · North America › United States", or "host · added by URL".</summary>
    public string ProviderAndRegion =>
        IsUser ? $"{Source.Provider} · {Strings.Library_DirectoryAddedByUrl}"
        : Source.Region.Count == 0 ? Source.Provider
        : $"{Source.Provider} · {string.Join(" › ", Source.Region)}";

    /// <summary>
    /// Where the source applies, in one word or two: the country ("United
    /// States"), else the region ("Worldwide"); "added by URL" for the user's
    /// own; <see langword="null"/> for a registry service (its region is the registry).
    /// </summary>
    public string? Place =>
        IsUser ? Strings.Library_DirectoryAddedByUrl
        : IsRegistry ? null
        : Source.Region.Count > 1 ? Source.Region[1]
        : Source.Region.FirstOrDefault();

    /// <summary>"NOAA Office of Coast Survey · United States": the details pane's second line.</summary>
    public string ProviderAndPlace => Place is { } place ? $"{Source.Provider} · {place}" : Source.Provider;

    private bool _showsPlace = true;

    /// <summary>True when the row's second line names <see cref="Place"/> (its region header doesn't already).</summary>
    internal bool ShowsPlace
    {
        get => _showsPlace;
        set
        {
            if (SetProperty(ref _showsPlace, value))
                OnPropertyChanged(nameof(RowDetail));
        }
    }

    /// <summary>The row's second line: the provider, and the country when the header doesn't say it.</summary>
    public string RowDetail => ShowsPlace ? ProviderAndPlace : Source.Provider;

    /// <summary>
    /// What the source can't tell before anything is downloaded, one plain
    /// sentence each (D14): no coverage, no editions (not for forecast feeds,
    /// which track runs instead), no sizes; then pilot and not-for-navigation
    /// warnings. Empty when there is nothing to say.
    /// </summary>
    public IReadOnlyList<string> Limitations
    {
        get
        {
            var limits = new List<string>();
            if (Source.Coverage == KnownCatalogueCoverage.None)
                limits.Add(Strings.Library_Limit_NoCoverage);
            if (!Source.Editions && !IsForecast)
                limits.Add(Strings.Library_Limit_NoEditions);
            if (!Source.Sizes)
                limits.Add(Strings.Library_Limit_NoSizes);
            if (Source.Pilot)
                limits.Add(Strings.Library_Limit_Pilot);
            if (Source.NotForNavigation)
                limits.Add(Strings.Library_Limit_NotForNavigation);
            return limits;
        }
    }

    /// <summary>True when <see cref="Limitations"/> has anything to say.</summary>
    public bool HasLimitations => Limitations.Count > 0;

    /// <summary>The limitations as one line, for the compact inline details (D9).</summary>
    public string LimitationsLine => string.Join(" · ", Limitations.Select(l => l.TrimEnd('.')));

    /// <summary>Copies the catalogue URL to the clipboard.</summary>
    public ICommand CopyUrlCommand { get; }

    public string? Note => Source.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Source.Note);

    /// <summary>True for a forecast feed (#685): it tracks runs, not editions.</summary>
    public bool IsForecast => Source.Format is KnownCatalogueFormat.S100ForecastModels or KnownCatalogueFormat.Weather;

    /// <summary>True for a pilot service (#685, NOAA's S-104), which may cover little and lapse.</summary>
    public bool IsPilot => Source.Pilot;

    /// <summary>True when the provider marks the catalogue's data as not for navigation.</summary>
    public bool IsNotForNavigation => Source.NotForNavigation;

    public string CatalogUrl => Source.CatalogUri.AbsoluteUri;

    /// <summary>Opens the provider's page (terms and notices).</summary>
    public ICommand OpenHomepageCommand { get; }

    public bool HasHomepage => Source.Homepage is not null;
}

/// <summary>One category in the directory's sidebar, with its count (#670, D6/D7).</summary>
internal sealed class CatalogueCategoryViewModel : ViewModelBase
{
    private int _count;
    private bool _hasMore;
    private bool _isOpen;
    private bool _isDimmed;

    public CatalogueCategoryViewModel(KnownCatalogueCategory category, Action select)
    {
        Category = category;
        SelectCommand = new RelayCommand(select);
    }

    /// <summary>The category.</summary>
    public KnownCatalogueCategory Category { get; }

    /// <summary>"Charts", "Depth, tides &amp; currents", …</summary>
    public string Label => LabelOf(Category);

    /// <summary>The label for <paramref name="category"/>.</summary>
    public static string LabelOf(KnownCatalogueCategory category) => category switch
    {
        KnownCatalogueCategory.Depth => Strings.Library_Category_Depth,
        KnownCatalogueCategory.Weather => Strings.Library_Category_Weather,
        KnownCatalogueCategory.Secom => Strings.Library_Category_Secom,
        KnownCatalogueCategory.User => Strings.Library_Category_User,
        _ => Strings.Library_Category_Charts,
    };

    /// <summary>"Charts", "Depth &amp; tides", …: the label in the narrow segmented strip (D2).</summary>
    public string ShortLabel => Category switch
    {
        KnownCatalogueCategory.Depth => Strings.Library_CategoryShort_Depth,
        KnownCatalogueCategory.Weather => Strings.Library_CategoryShort_Weather,
        KnownCatalogueCategory.Secom => Strings.Library_CategoryShort_Secom,
        KnownCatalogueCategory.User => Strings.Library_CategoryShort_User,
        _ => Strings.Library_CategoryShort_Charts,
    };

    /// <summary>How many sources the category lists (how many match while searching).</summary>
    public int Count => _count;

    /// <summary>"17", or "1+" for SECOM services before the registry is listed.</summary>
    public string CountText => _hasMore
        ? string.Format(CultureInfo.CurrentCulture, Strings.Library_CategoryCountMoreFormat, _count)
        : _count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The sidebar row's accessible name: "Charts, 17 sources".</summary>
    public string AccessibleName => string.Format(CultureInfo.CurrentCulture, Strings.Library_CategoryAccessibleFormat, Label, CountText);

    /// <summary>True for the open category (not while searching, when every category is shown).</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>True while searching when nothing in the category matches.</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        private set => SetProperty(ref _isDimmed, value);
    }

    /// <summary>Opens the category (and clears the search).</summary>
    public ICommand SelectCommand { get; }

    internal void Update(int count, bool hasMore, bool isOpen, bool isDimmed)
    {
        if (count != _count || hasMore != _hasMore)
        {
            _count = count;
            _hasMore = hasMore;
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(AccessibleName));
        }

        IsOpen = isOpen;
        IsDimmed = isDimmed;
    }
}
