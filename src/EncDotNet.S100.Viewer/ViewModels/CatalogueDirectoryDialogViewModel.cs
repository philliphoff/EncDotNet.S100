using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
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

    public CatalogueDirectoryDialogViewModel(
        IReadOnlyList<KnownCatalogueSource> sources,
        Action<Uri>? openUrl = null,
        UserCatalogueStore? userCatalogues = null,
        Func<Uri, CancellationToken, Task<CatalogueProbe>>? probe = null)
    {
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
        // Regions in the order the curated list first names them; user catalogues last, under Custom.
        var regionOrder = _known.Select(s => s.Region.FirstOrDefault() ?? string.Empty).Distinct().ToList();
        Entries = _known
            .OrderBy(s => regionOrder.IndexOf(s.Region.FirstOrDefault() ?? string.Empty))
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new CatalogueEntryViewModel(s, _openUrl))
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
        foreach (var group in _entries.GroupBy(e => e.IsUser ? KnownCatalogueSources.CustomRegion : e.Source.Region.FirstOrDefault() ?? string.Empty))
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

    public CatalogueEntryViewModel(
        KnownCatalogueSource source, Action<Uri>? openUrl, Action<KnownCatalogueSource>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
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

    /// <summary>True when the provider marks the catalogue's data as not for navigation (an amber chip).</summary>
    public bool IsNotForNavigation => Source.NotForNavigation;

    public string CatalogUrl => Source.CatalogUri.AbsoluteUri;

    /// <summary>Opens the provider's page (terms and notices).</summary>
    public ICommand OpenHomepageCommand { get; }

    public bool HasHomepage => Source.Homepage is not null;
}
