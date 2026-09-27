using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the "Add Online Catalogue" directory (issue #670): lists the
/// curated known chart catalogues by region with what each provides, plus
/// the catalogues the user added by URL (recognised by their root element and
/// kept in a <see cref="UserCatalogueStore"/>), and hands the chosen one on
/// to the Add to Library dialog.
/// </summary>
internal sealed class CatalogueDirectoryDialogViewModel : ViewModelBase
{
    private readonly IReadOnlyList<KnownCatalogueSource> _known;
    private readonly Action<Uri>? _openUrl;
    private readonly UserCatalogueStore? _store;
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>>? _probe;
    private readonly List<KnownCatalogueSource> _user;
    private IReadOnlyList<CatalogueEntryViewModel> _entries = [];
    private CatalogueEntryViewModel? _selected;
    private string _catalogueUrl = string.Empty;
    private string? _urlError;
    private bool _isChecking;

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

        NextCommand = new RelayCommand(() => Chosen?.Invoke(this, _selected!.Source), () => _selected is not null);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        AddUrlCommand = new AsyncRelayCommand(AddUrlAsync, () => CanAddUrl);
        Rebuild(selectId: null);
    }

    /// <summary>Raised with the catalogue the user chose.</summary>
    public event EventHandler<KnownCatalogueSource>? Chosen;

    /// <summary>Raised when the user cancels.</summary>
    public event EventHandler? Cancelled;

    /// <summary>The curated catalogues, sorted by region then name, followed by the user's own.</summary>
    public IReadOnlyList<CatalogueEntryViewModel> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    /// <summary>The highlighted catalogue.</summary>
    public CatalogueEntryViewModel? SelectedEntry
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
        }
    }

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
                OnPropertyChanged(nameof(HasUrlError));
        }
    }

    public bool HasUrlError => _urlError is not null;

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

    /// <summary>Continues with the selected catalogue.</summary>
    public ICommand NextCommand { get; }

    public ICommand CancelCommand { get; }

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
                UrlError = probe.IsS100ExchangeCatalogue ? Strings.Library_DirectoryUrlS100
                    : probe.IsJson ? Strings.Library_DirectoryUrlJson
                    : probe.RootElement is { } root ? string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryUrlUnsupportedFormat, root)
                    : Strings.Library_DirectoryUrlNotXml;
                return;
            }

            var source = KnownCatalogueSources.FromUrl(uri, format, probe.Title);
            _user.RemoveAll(s => s.Id == source.Id);
            _user.Add(source);
            _store?.Add(source);
            Rebuild(source.Id);
            CatalogueUrl = string.Empty;
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

    private void Remove(KnownCatalogueSource source)
    {
        _user.RemoveAll(s => s.Id == source.Id);
        _store?.Remove(source.Id);
        Rebuild(_selected?.Source.Id == source.Id ? null : _selected?.Source.Id);
    }

    private void Rebuild(string? selectId)
    {
        Entries = _known
            .OrderBy(s => string.Join('/', s.Region), StringComparer.CurrentCulture)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new CatalogueEntryViewModel(s, _openUrl))
            .Concat(_user.Select(s => new CatalogueEntryViewModel(s, _openUrl, Remove)))
            .ToArray();
        SelectedEntry = _entries.FirstOrDefault(e => e.Source.Id == selectId) ?? _entries.FirstOrDefault();
    }
}

/// <summary>One catalogue in the directory, with display text for its quality chips.</summary>
internal sealed class CatalogueEntryViewModel : ViewModelBase
{
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

    /// <summary>The known source.</summary>
    public KnownCatalogueSource Source { get; }

    /// <summary>True for a catalogue the user added by URL (it can be removed).</summary>
    public bool IsUser { get; }

    /// <summary>Removes a user-added catalogue from the list.</summary>
    public ICommand RemoveCommand { get; }

    public string Name => Source.Name;

    /// <summary>"U.S. Army Corps of Engineers · North America › United States".</summary>
    public string ProviderAndRegion =>
        Source.Region.Count == 0 ? Source.Provider : $"{Source.Provider} · {string.Join(" › ", Source.Region)}";

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

    public string CatalogUrl => Source.CatalogUri.AbsoluteUri;

    /// <summary>Opens the provider's page (terms and notices).</summary>
    public ICommand OpenHomepageCommand { get; }

    public bool HasHomepage => Source.Homepage is not null;
}
