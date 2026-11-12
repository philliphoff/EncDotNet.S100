using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

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
        Action<KnownCatalogueSource>? remove,
        Action<Keys.KeysTab>? openFix,
        Action<string>? copyText,
        bool isRegistry = false)
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
        : this(HeaderSource, null, remove: null, openFix: null, copyText: null)
    {
        IsGroupHeader = true;
        GroupTitle = title.Length > 0 ? title : null;
    }

    /// <summary>A region header row.</summary>
    public static CatalogueEntryViewModel Header(string title) => new(title);

    /// <summary>
    /// The registry row in SECOM services (D10): "List services from the
    /// registry" until it is listed, then its summary and Refresh. Not selectable.
    /// </summary>
    public static CatalogueEntryViewModel RegistryRow(CatalogueDirectoryDialogViewModel directory) =>
        new(string.Empty) { Directory = directory, IsRegistryRow = true };

    /// <summary>For the registry row, the directory whose registry it lists and refreshes.</summary>
    public CatalogueDirectoryDialogViewModel? Directory { get; private init; }

    /// <summary>True for the registry row (see <see cref="RegistryRow"/>).</summary>
    public bool IsRegistryRow { get; private init; }

    /// <summary>True for a source (not a header or the registry row): the rows that can be chosen.</summary>
    public bool IsSourceRow => !IsGroupHeader;

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

    /// <summary>True for a header or the registry row: not selectable, not a source.</summary>
    public bool IsGroupHeader { get; }

    /// <summary>True for a region or category header.</summary>
    public bool IsHeaderText => IsGroupHeader && !IsRegistryRow;

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

    /// <summary>The row's accessible name: the header's text, the registry row's action, else the source's name.</summary>
    public string? AccessibleName => IsRegistryRow ? Strings.Label_ShowSecomServices : IsGroupHeader ? GroupTitle : Name;

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
