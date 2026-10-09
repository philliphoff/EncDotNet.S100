using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the catalogue step of "Add online catalogue" (issue #670):
/// lists the curated known chart catalogues grouped by region, with what each
/// provides, plus the catalogues the user added by URL (recognised by their
/// root element and kept in a <see cref="UserCatalogueStore"/>) under Custom.
/// The list can be searched; the wizard reads <see cref="SelectedEntry"/>.
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

        AddUrlCommand = new AsyncRelayCommand(AddUrlAsync, () => CanAddUrl);
        OpenUrlPanelCommand = new RelayCommand(() => IsUrlPanelOpen = true);
        CloseUrlPanelCommand = new RelayCommand(() => IsUrlPanelOpen = false);
        Rebuild(selectId: null);
    }

    /// <summary>
    /// Every catalogue (not filtered by the search), grouped by region in the
    /// order the curated list first names each region, by name within a
    /// region, followed by the user's own.
    /// </summary>
    public IReadOnlyList<CatalogueEntryViewModel> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    /// <summary>The list as shown: the catalogues matching the search, under non-selectable region headers.</summary>
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
            if (value.IsRegistry && value.Reachability is null && !value.IsProbing)
                _ = ProbeAsync(value);
        }
    }

    /// <summary>
    /// True when the chosen catalogue can be added: any curated or own
    /// catalogue, or a registry service that answers without a certificate or
    /// with this client's MCP identity (#832).
    /// </summary>
    public bool CanContinueWithSelection =>
        _selected is { } entry && (!entry.IsRegistry || entry.Reachability is SecomReachability.Open or SecomReachability.OpenWithCertificate);

    /// <summary>Lists SECOM services from the MCP service registry (#822); the registry is only asked when this runs.</summary>
    public ICommand ShowRegistryCommand { get; }

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

    /// <summary>Filters the list by name, provider, region or format.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ShowMatches();
        }
    }

    /// <summary>"18", or "3 of 18" while searching.</summary>
    public string CountText
    {
        get
        {
            var shown = _rows.Count(r => !r.IsGroupHeader);
            return shown == _entries.Count
                ? _entries.Count.ToString("N0", CultureInfo.CurrentCulture)
                : string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryCountFormat, shown, _entries.Count);
        }
    }

    /// <summary>True when the search matches no catalogue.</summary>
    public bool HasNoMatches => _rows.Count == 0;

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
        _user.RemoveAll(s => s.Id == source.Id);
        _store?.Remove(source.Id);
        Rebuild(_selected?.Source.Id == source.Id ? null : _selected?.Source.Id);
    }

    private void Rebuild(string? selectId)
    {
        // Regions in the order the curated list first names them; registry services
        // next (by product), then the user's own, under Custom.
        var regionOrder = _known.Select(s => s.Region.FirstOrDefault() ?? string.Empty).Distinct().ToList();
        var previous = _entries.Where(e => e.IsRegistry).ToDictionary(e => e.Source.Id, StringComparer.Ordinal);
        Entries = _known
            .OrderBy(s => regionOrder.IndexOf(s.Region.FirstOrDefault() ?? string.Empty))
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new CatalogueEntryViewModel(s, _openUrl))
            .Concat(_registry
                .OrderBy(s => s.Pilot)
                .ThenBy(s => s.Product, StringComparer.Ordinal)
                .ThenBy(s => s.Name, StringComparer.CurrentCulture)
                // Keep what was already found out about a service.
                .Select(s => previous.TryGetValue(s.Id, out var kept) ? kept : new CatalogueEntryViewModel(s, _openUrl, isRegistry: true)))
            .Concat(_user.Select(s => new CatalogueEntryViewModel(s, _openUrl, Remove)))
            .ToArray();

        var keep = _entries.FirstOrDefault(e => e.Source.Id == selectId) ?? _entries.FirstOrDefault();
        _selected = null;
        if (keep is not null)
            SelectedEntry = keep;
        else
            OnPropertyChanged(nameof(SelectedEntry));
        ShowMatches();
    }

    private void ShowMatches()
    {
        var text = _searchText.Trim();
        var rows = new List<CatalogueEntryViewModel>();
        foreach (var group in _entries.GroupBy(e => e.IsUser ? KnownCatalogueSources.CustomRegion
            : e.IsRegistry ? Strings.Library_RegistryRegion
            : e.Source.Region.FirstOrDefault() ?? string.Empty))
        {
            var matches = group.Where(e => e.Matches(text)).ToArray();
            if (matches.Length == 0)
                continue;

            rows.Add(CatalogueEntryViewModel.Header(GroupTitle(group.Key, group.ToArray())));
            rows.AddRange(matches);
        }

        Rows = rows;
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasNoMatches));

        // Replacing the rows clears the list's highlight; restore it on the kept choice.
        OnPropertyChanged(nameof(SelectedEntry));
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
/// One row in the directory: a catalogue, with display text for its quality
/// chips, or a region header (<see cref="IsGroupHeader"/>) above a group.
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
        KnownCatalogueSource source, Action<Uri>? openUrl, Action<KnownCatalogueSource>? remove = null, bool isRegistry = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        IsRegistry = isRegistry;
        IsUser = remove is not null;
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

    /// <summary>True when the row shows a reachability chip (a registry service).</summary>
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

    /// <summary>True when the service cannot be added as things stand (the chip is shown as limited).</summary>
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
                SecomReachability.NeedsSecom2Search => Strings.Library_Reachability_NeedsSecom2SearchExplanation,
                SecomReachability.OpenWithCertificate => string.Format(
                    CultureInfo.CurrentCulture, Strings.Library_Reachability_OpenWithCertificateExplanationFormat, _reachability.Identity),
                SecomReachability.CertificateRefused => string.Format(
                    CultureInfo.CurrentCulture, Strings.Library_Reachability_CertificateRefusedExplanationFormat, _reachability.Identity),
                SecomReachability.UntrustedServer => trust switch
                {
                    { Outcome: SecomServerTrustOutcome.WrongHost } => Strings.Library_Reachability_WrongHostExplanation,
                    { Outcome: SecomServerTrustOutcome.Expired, Anchor: { } expiredAnchor } =>
                        string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_ExpiredServerExplanationFormat, expiredAnchor),
                    _ => Strings.Library_Reachability_UntrustedServerExplanation,
                },
                SecomReachability.Unreachable => _reachability.Detail is { Length: > 0 } detail
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_UnreachableExplanationFormat, detail)
                    : null,
                _ => null,
            };
            var from = trust is { Outcome: SecomServerTrustOutcome.AnchorTrusted, Anchor: { } anchor }
                ? string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_ServerAnchorFormat, anchor)
                : null;
            return why is null ? from : from is null ? why : $"{why} {from}";
        }
    }

    /// <summary>True when <see cref="ReachabilityExplanation"/> is set.</summary>
    public bool HasReachabilityExplanation => ReachabilityExplanation is not null;

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

    public string? Note => Source.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Source.Note);

    /// <summary>What the map can show before anything is downloaded.</summary>
    public string CoverageChip => Source.Coverage switch
    {
        KnownCatalogueCoverage.Polygons => Strings.Library_Chip_CoveragePolygons,
        KnownCatalogueCoverage.BoundingBoxes => Strings.Library_Chip_CoverageBoxes,
        _ => Strings.Library_Chip_CoverageNone,
    };

    /// <summary>True when the coverage chip should read as a limitation.</summary>
    public bool IsCoverageLimited => Source.Coverage == KnownCatalogueCoverage.None;

    public string EditionsChip => Source.Editions ? Strings.Library_Chip_Editions : Strings.Library_Chip_NoEditions;

    public bool IsEditionsLimited => !Source.Editions;

    public string SizesChip => Source.Sizes ? Strings.Library_Chip_Sizes : Strings.Library_Chip_NoSizes;

    public bool IsSizesLimited => !Source.Sizes;

    /// <summary>
    /// True for a forecast feed (#685): its chips say "Forecast" and "Latest run
    /// only" in place of the edition and size chips.
    /// </summary>
    public bool IsForecast => Source.Format == KnownCatalogueFormat.S100ForecastModels;

    /// <summary>True when the edition and size chips apply (anything but a forecast feed).</summary>
    public bool HasEditionChips => !IsForecast;

    /// <summary>True for a pilot service (#685, NOAA's S-104): an amber "Pilot" chip.</summary>
    public bool IsPilot => Source.Pilot;

    /// <summary>True when the provider marks the catalogue's data as not for navigation (an amber chip).</summary>
    public bool IsNotForNavigation => Source.NotForNavigation;

    public string CatalogUrl => Source.CatalogUri.AbsoluteUri;

    /// <summary>Opens the provider's page (terms and notices).</summary>
    public ICommand OpenHomepageCommand { get; }

    public bool HasHomepage => Source.Homepage is not null;
}
