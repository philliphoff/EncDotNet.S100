using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model that backs the Pick Report (Object Information) side panel.
/// Holds the most recently picked feature's identity, originating dataset,
/// and attribute list.
/// </summary>
/// <remarks>
/// Milestone 1 introduced FC-resolved attribute decoding; milestone 2 adds
/// multi-feature picks: <see cref="Hits"/> carries every overlapping feature
/// at the click point, <see cref="SelectedHit"/> drives the detail view, and
/// <see cref="HasMultipleHits"/> gates the hit-list UI. Picks with a single
/// hit behave exactly like before (hit-list hidden).
/// </remarks>
internal sealed class PickReportViewModel : ViewModelBase, EncDotNet.S100.Viewer.ViewModels.Activities.IActivityTabContentSignal
{
    private readonly IPickService _picks;
    private readonly StationChartContext _charts;
    private readonly ExaminerLinks _examinerLinks;
    private readonly Dictionary<PickHit, StationTimeSeriesViewModel> _stationCharts = new(ReferenceEqualityComparer.Instance);
    private StationTimeSeriesViewModel? _stationChart;
    private bool _warningAttributesExpanded;
    private string? _featureType;
    private string? _featureTypeName;
    private string? _featureRef;
    private string? _datasetFileName;
    private string? _productSpec;
    private bool _hasPick;
    private PickHit? _selectedHit;
    private IReadOnlyList<object> _sections = [];
    private PickLocation? _location;
    private DepthOverTimeViewModel? _locationDepth;
    private string _chartBasisLine = string.Empty;
    private Services.Legends.LegendService? _legend;

    /// <summary>Creates Object Information.</summary>
    /// <param name="picks">The picks it shows, and follows references through.</param>
    /// <param name="charts">
    /// The time, formatting and theme of its station and depth charts; it
    /// formats attribute times and depths the same way.
    /// </param>
    /// <param name="examinerLinks">Opens the picked feature and its attributes in the eXaminer.</param>
    /// <param name="warnings">The Warning cards of a pick that hit navigational warnings.</param>
    /// <param name="selection">What it shows, published for those that follow it (#1397).</param>
    public PickReportViewModel(
        IPickService picks,
        StationChartContext charts,
        ExaminerLinks examinerLinks,
        PickWarningCardsViewModel warnings,
        ObjectInformationSelection selection)
    {
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(charts);
        ArgumentNullException.ThrowIfNull(examinerLinks);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(selection);
        _picks = picks;
        _charts = charts;
        _examinerLinks = examinerLinks;
        Warnings = warnings;
        Selection = selection;
        ClearCommand = new RelayCommand(Clear);
        CopyLocationCommand = new RelayCommand(
            () => { if (_location is { } loc) CopyLocationRequested?.Invoke(this, LatLonFormatter.FormatDecimal(loc.Latitude, loc.Longitude)); },
            () => _location is not null);
        CopyIdentityCommand = new RelayCommand(
            () => { var text = IdentityClipboardText; if (!string.IsNullOrEmpty(text)) CopyIdentityRequested?.Invoke(this, text); },
            () => _selectedHit is not null);
        // A followed reference that isn't found says so in a toast.
        NavigateCommand = new RelayCommand<FeatureReference>(
            r => { if (r is not null) _picks.NavigateToReference(r); },
            r => r is not null);
        TakeHelmCommand = new RelayCommand<DynamicPickHit>(
            hit => { if (hit is not null && TryGetAisMmsi(hit, out var mmsi)) TakeHelmRequested?.Invoke(this, mmsi); },
            hit => hit is not null && TryGetAisMmsi(hit, out _));
        OpenFeatureInExaminerCommand = new RelayCommand(
            () => _examinerLinks.OpenFeature(ProductSpec, FeatureType),
            () => IsExaminerAvailable && !string.IsNullOrWhiteSpace(FeatureType));
        OpenAttributeInExaminerCommand = new RelayCommand<PickAttribute>(
            a => { if (a is not null) _examinerLinks.OpenAttribute(ProductSpec, FeatureType, a.Code); },
            a => IsExaminerAvailable && a is not null && !string.IsNullOrWhiteSpace(a.Code));

        // Object Information's selection, for those that follow it without
        // holding the pick report (#1397): the warnings panel and the drawing.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SelectedHit) or nameof(HasPick))
                Selection.SetHit(_selectedHit);
        };
        Warnings.PropertyChanged += OnWarningsPropertyChanged;
        Warnings.SelectRequested += (_, hit) => SelectedHit = hit;
        Warnings.CopyRequested += OnCardCopyRequested;

        _picks.Picked += (_, result) => SetPicks(result);
        _charts.TimeFormat.TimeFormatChanged += _ => ReformatAttributesFromSelectedHit();
        // DepthUnit is the only mariner setting the pick panel renders;
        // re-run the same projection used for time-format changes so
        // depth-typed rows pick up the new unit immediately.
        _charts.MarinerSettings.Changed += _ => ReformatAttributesFromSelectedHit();
    }

    private void OnWarningsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PickWarningCardsViewModel.Card):
                Selection.SetOpenWarning(Warnings.Card?.Hit);
                break;
            case nameof(PickWarningCardsViewModel.HasCard):
                OnPropertyChanged(nameof(ShowInlineAttributes));
                break;
            case nameof(PickWarningCardsViewModel.HasList):
                OnPropertyChanged(nameof(ShowHitList));
                OnPropertyChanged(nameof(SelectedOtherHit));
                break;
            case nameof(PickWarningCardsViewModel.PreviewHit):
                OnPropertyChanged(nameof(PreviewHit));
                break;
        }
    }

    private void ReformatAttributesFromSelectedHit()
    {
        if (_selectedHit is null) return;
        PopulateAttributeSections(FormatAttributes(_selectedHit.Attributes));
    }

    private IReadOnlyList<PickAttribute> FormatAttributes(IReadOnlyList<PickAttribute> attributes) =>
        PickAttributeFormatter.Format(attributes, _charts.TimeFormat.Current, _charts.MarinerSettings.Current.DepthUnit);

    /// <summary>
    /// Splits the reformatted attribute tree into the key/value
    /// <see cref="Attributes"/> table and the <see cref="TextCards"/>, then
    /// publishes both. Resolved <c>fileReference</c> rows (S-101 FC; alias
    /// TXTDSC / NTXTDS) and long text values (see
    /// <see cref="PickTextCardViewModel.IsLongValue"/>) are lifted out of the
    /// table so their text is read as a block rather than a truncated cell.
    /// </summary>
    private void PopulateAttributeSections(IReadOnlyList<PickAttribute> reformatted)
    {
        Attributes.Clear();
        // The panel leads with NavigationNotice for a product that isn't
        // S-100; the processor's own notice row (kept for MCP, the CLI and
        // the Mapsui pick query) would repeat it (#1096).
        if (NavigationNotice is not null)
            reformatted = PickAttributeFormatter.WithoutNavigationNotice(reformatted);
        // The stale line shows under the notice (StaleNotice), not as a row.
        if (reformatted.Any(r => r.IsStaleNotice))
            reformatted = [.. reformatted.Where(r => !r.IsStaleNotice)];
        var rows = FeatureInfoBuilder.WithoutResolvedFileReferences(reformatted);
        if (_selectedHit?.NavigationalWarning is not null)
            rows = PickAttributeFormatter.WithoutWarningText(rows);
        var longValues = new List<(PickAttribute Attribute, PickAttribute? Parent)>();
        rows = PickAttributeFormatter.WithoutLongValues(rows, longValues);
        foreach (var a in rows)
            Attributes.Add(a);
        OnPropertyChanged(nameof(HasAttributes));
        OnPropertyChanged(nameof(AttributeCount));

        ClearTextCards();
        var notForNavigation = _selectedHit?.NotForNavigation == true;
        foreach (var fileRef in FeatureInfoBuilder.CollectResolvedFileReferences(reformatted))
            AddTextCard(PickTextCardViewModel.FromReferencedText(fileRef, notForNavigation));
        foreach (var (attribute, parent) in longValues)
            AddTextCard(PickTextCardViewModel.FromLongValue(attribute, parent, notForNavigation));
        OnPropertyChanged(nameof(HasTextCards));
    }

    private void AddTextCard(PickTextCardViewModel card)
    {
        card.CopyRequested += OnCardCopyRequested;
        TextCards.Add(card);
    }

    private void ClearTextCards()
    {
        foreach (var card in TextCards)
            card.CopyRequested -= OnCardCopyRequested;
        TextCards.Clear();
    }

    /// <summary>The picked feature's class/type code (e.g. "DepthArea", "LateralBuoy").</summary>
    public string? FeatureType
    {
        get => _featureType;
        private set => SetProperty(ref _featureType, value);
    }

    /// <summary>FC-resolved human-readable name of the feature type, when available.</summary>
    public string? FeatureTypeName
    {
        get => _featureTypeName;
        private set => SetProperty(ref _featureTypeName, value);
    }

    /// <summary>The picked feature's dataset-specific reference identifier.</summary>
    public string? FeatureRef
    {
        get => _featureRef;
        private set => SetProperty(ref _featureRef, value);
    }

    /// <summary>File name (no path) of the dataset the picked feature came from.</summary>
    public string? DatasetFileName
    {
        get => _datasetFileName;
        private set => SetProperty(ref _datasetFileName, value);
    }

    /// <summary>Product specification of the source dataset (e.g. "S-101").</summary>
    public string? ProductSpec
    {
        get => _productSpec;
        private set => SetProperty(ref _productSpec, value);
    }

    /// <summary>
    /// Primary heading for the identity block: the picked feature's instance
    /// name when one is present, otherwise the feature class name. Mirrors
    /// <see cref="PickHit.PrimaryLabel"/> for the selected hit.
    /// </summary>
    public string? PrimaryLabel => _selectedHit?.PrimaryLabel;

    /// <summary>
    /// Class-name pill shown beside <see cref="PrimaryLabel"/> when an
    /// instance name is leading; <see langword="null"/> otherwise.
    /// </summary>
    public string? SecondaryLabel => _selectedHit?.SecondaryLabel;

    /// <summary>True when <see cref="SecondaryLabel"/> has a value to show.</summary>
    public bool HasSecondaryLabel => !string.IsNullOrEmpty(SecondaryLabel);

    /// <summary>Category glyph for the selected hit's feature class.</summary>
    public FluentIcons.Common.Icon Glyph =>
        _selectedHit?.Glyph ?? Services.FeatureGlyphs.Fallback;

    /// <summary>
    /// Single-line caption demoting the technical identity (ID, product
    /// spec, source file) beneath the heading, e.g.
    /// <c>"ID 555 · S-101 · 101GB00502793.000"</c>. Segments that are
    /// absent are omitted. Rendered in a monospace caption style.
    /// </summary>
    public string? IdentityCaption
    {
        get
        {
            if (_selectedHit is null)
                return null;

            // An AML feature's spec, cell and marking are on its provenance
            // strip; the caption names its class instead (#1372).
            if (_selectedHit.Aml is { } aml)
                return $"{FeatureType} · {aml.ClassKindText(_selectedHit.Attributes)}";

            var segments = new List<string>(3);
            if (!string.IsNullOrEmpty(FeatureRef))
                segments.Add(string.Format(CultureInfo.CurrentCulture, Strings.Pick_IdCaption, FeatureRef));
            if (!string.IsNullOrEmpty(ProductSpec))
                segments.Add(ProductSpec!);
            if (!string.IsNullOrEmpty(DatasetFileName))
                segments.Add(DatasetFileName!);

            return segments.Count > 0 ? string.Join(" · ", segments) : null;
        }
    }

    /// <summary>
    /// "SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology" when the
    /// selected hit's product specification defines no portrayal, so the symbol
    /// the user clicked isn't IHO symbology; <see langword="null"/> otherwise
    /// (issue #937).
    /// </summary>
    /// <remarks>An AML feature the map doesn't draw has no symbol to explain; it says <see cref="AmlNotDrawnNote"/> instead.</remarks>
    public string? PortrayalNote => _selectedHit is null || _selectedHit.Aml is { Drawn: false }
        ? null
        : PortrayalNoteText.For(ProductSpec);

    /// <summary>True when <see cref="PortrayalNote"/> has a value to show.</summary>
    public bool HasPortrayalNote => PortrayalNote is not null;

    /// <summary>
    /// The provenance strip of the selected hit when it's a NATO Additional
    /// Military Layers feature: AML tag, cell and protective marking (#1372,
    /// design decision 13); <see langword="null"/> otherwise.
    /// </summary>
    public AmlPickProvenance? Aml => _selectedHit?.Aml;

    /// <summary>True when <see cref="Aml"/> has a strip to show.</summary>
    public bool HasAml => Aml is not null;

    /// <summary>
    /// "Not drawn. Picked from its position." when the selected hit is an AML
    /// feature the map doesn't draw; <see langword="null"/> otherwise.
    /// </summary>
    public string? AmlNotDrawnNote => Aml is { Drawn: false } ? Strings.Pick_AmlNotDrawn : null;

    /// <summary>True when <see cref="AmlNotDrawnNote"/> has a value to show.</summary>
    public bool HasAmlNotDrawnNote => AmlNotDrawnNote is not null;

    /// <summary>
    /// The pick report's first line for a product that isn't S-100, such as
    /// "Weather model data, not for navigation."; <see langword="null"/> for an
    /// S-100 product (see <see cref="NavigationNoticeText"/>).
    /// </summary>
    public string? NavigationNotice => _selectedHit is null ? null : NavigationNoticeText.For(ProductSpec);

    /// <summary>True when <see cref="NavigationNotice"/> has a value to show.</summary>
    public bool HasNavigationNotice => NavigationNotice is not null;

    /// <summary>
    /// A warning line under the notice when the data picked is old: "This
    /// analysis is 9 h old. A newer one is likely." (vector-weather.md
    /// §7); the processor's <see cref="PickAttribute.IsStaleNotice"/> row, else null.
    /// </summary>
    public string? StaleNotice => _selectedHit?.Attributes.FirstOrDefault(a => a.IsStaleNotice) is { } row
        ? row.DisplayValue ?? row.RawValue
        : null;

    /// <summary>True when <see cref="StaleNotice"/> has a value to show.</summary>
    public bool HasStaleNotice => StaleNotice is not null;

    /// <summary>"Data: NOAA/NCEP GFS-Wave · Public domain": the picked dataset's credit from its Library source (#1075), or null.</summary>
    public string? DataCredit => _selectedHit?.Credit is { } credit
        ? string.Format(CultureInfo.CurrentCulture, Strings.Library_DataCreditFormat, credit)
        : null;

    /// <summary>True when <see cref="DataCredit"/> has a value to show.</summary>
    public bool HasDataCredit => DataCredit is not null;

    /// <summary>
    /// Clipboard text for the copy-identity action: heading plus caption, after
    /// the <see cref="NavigationNotice"/> when there is one.
    /// </summary>
    private string? IdentityClipboardText
    {
        get
        {
            if (_selectedHit is null)
                return null;
            var caption = IdentityCaption;
            var identity = string.IsNullOrEmpty(caption)
                ? PrimaryLabel
                : $"{PrimaryLabel}\n{caption}";
            if (Aml is { } aml)
                identity = $"{aml.TagText} · {aml.CellName}{(aml.HasMarking ? " · " + aml.Marking : string.Empty)}\n{identity}";
            return NavigationNotice is { } notice ? $"{notice}\n{identity}" : identity;
        }
    }

    /// <summary>True when a feature is currently displayed in the panel.</summary>
    public bool HasPick
    {
        get => _hasPick;
        private set
        {
            var wasHasPick = _hasPick;
            if (SetProperty(ref _hasPick, value) && !wasHasPick && value)
            {
                // false→true transition: signal that the Pick Report dock
                // should auto-open (PR-M4). SetPicks raises it again for each
                // later pick, so a pick switches the dock back to this tab.
                ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// True when the current pick includes at least one dataset-owned
    /// feature. Drives visibility of the identity / references /
    /// attributes sections in the panel; dynamic-only picks set this
    /// to false and only render the dynamic-hits section.
    /// </summary>
    public bool HasDatasetPick => Hits.Count > 0;

    /// <summary>
    /// Geographic location (WGS84) of the click that produced the current
    /// pick, or <c>null</c> when the pick carries no location (e.g. a
    /// programmatic open via feature search). Set by the pick service from
    /// the map's world position.
    /// </summary>
    public PickLocation? Location
    {
        get => _location;
        private set
        {
            if (Nullable.Equals(_location, value))
                return;
            _location = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLocation));
            OnPropertyChanged(nameof(LocationDisplay));
            (CopyLocationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    /// <summary>True when a pick location is available to display and copy.</summary>
    public bool HasLocation => _location is not null;

    /// <summary>
    /// The assimilated depth-over-time card for the current pick location, or
    /// <c>null</c> when the pick is on land, off-coverage, or no base depth
    /// could be resolved. Shown between the location block and the feature list.
    /// </summary>
    public DepthOverTimeViewModel? LocationDepthSeries
    {
        get => _locationDepth;
        private set
        {
            if (ReferenceEquals(_locationDepth, value))
                return;
            _locationDepth?.Dispose();
            _locationDepth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLocationDepthSeries));
        }
    }

    /// <summary>
    /// Tells the depth card what the chart's depths are relative to (#1191),
    /// so it says how the picked depth relates to what the chart draws.
    /// </summary>
    /// <param name="basis">The depth basis indicator's current basis.</param>
    public void SetDepthBasis(Services.Depth.DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        _chartBasisLine = Services.Depth.DepthBasisText.CardLine(basis, _charts.TimeFormat.Current);
        if (_locationDepth is not null)
            _locationDepth.ChartBasisLine = _chartBasisLine;
    }

    /// <summary>True when a depth-assimilation card is available for the pick.</summary>
    public bool HasLocationDepthSeries => _locationDepth is not null;

    /// <summary>
    /// Shows the picked features' Legend rows as key lines (#1410, legends
    /// design note D3, D16), each with an <b>In legend</b> link that opens the
    /// Legend tab at the row. The lines follow the legend as it rebuilds (a
    /// palette change, a pan). Without a legend the report has no key.
    /// </summary>
    /// <param name="legend">The legend service.</param>
    internal void AttachLegend(Services.Legends.LegendService legend)
    {
        ArgumentNullException.ThrowIfNull(legend);
        if (_legend is not null)
            _legend.Changed -= OnLegendChanged;
        _legend = legend;
        legend.Changed += OnLegendChanged;
        RefreshLegendKey();
    }

    /// <summary>
    /// One key line per picked feature that a Legend row matches, in hit
    /// order, each row once: "S-102 · 2–10 m" with its swatch (round 3's C).
    /// </summary>
    public ObservableCollection<PickLegendKeyLine> LegendKey { get; } = new();

    /// <summary>True when <see cref="LegendKey"/> has lines to show.</summary>
    public bool HasLegendKey => LegendKey.Count > 0;

    private void OnLegendChanged(object? sender, EventArgs e) => RefreshLegendKey();

    private void RefreshLegendKey()
    {
        LegendKey.Clear();
        if (_legend is { } legend)
        {
            var tile = Legends.LegendViewModel.TileOf(legend.Palette);
            var seen = new HashSet<(string Section, string Row)>();
            foreach (var hit in Hits)
            {
                if (legend.FindRow(hit, _location?.Latitude) is { } match
                    && seen.Add((match.Section.Id, match.Row.Label)))
                {
                    LegendKey.Add(new PickLegendKeyLine(match, tile, legend.Show));
                }
            }
        }
        OnPropertyChanged(nameof(HasLegendKey));
    }


    /// <summary>
    /// Mariner-friendly degrees-decimal-minutes rendering of
    /// <see cref="Location"/> for display in the panel, or <c>null</c> when
    /// no location is available.
    /// </summary>
    public string? LocationDisplay =>
        _location is { } loc ? LatLonFormatter.Format(loc.Latitude, loc.Longitude) : null;

    /// <inheritdoc />
    public event EventHandler? ContentBecameAvailable;

    /// <summary>
    /// Ordered list of every feature the most recent pick gesture hit. The
    /// first entry is selected by default (matching the legacy single-hit
    /// behaviour); when <see cref="HasMultipleHits"/> is true the panel
    /// renders a selectable list above the detail view.
    /// </summary>
    public ObservableCollection<PickHit> Hits { get; } = new();

    /// <summary>
    /// The hit currently shown in the detail view. Two-way bound to the
    /// hit-list selection in the panel. Setting this property updates
    /// <see cref="FeatureType"/>, <see cref="FeatureRef"/>,
    /// <see cref="Attributes"/>, etc., to match the new selection.
    /// </summary>
    public PickHit? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (ReferenceEquals(_selectedHit, value))
                return;
            _selectedHit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOtherHit));
            ApplyHitToDetailFields(value);
        }
    }

    /// <summary>True when more than one feature was hit at the pick location.</summary>
    public bool HasMultipleHits => Hits.Count > 1;

    /// <summary>
    /// Whether the hit list shows: several hits, unless they include several
    /// navigational warnings, whose cards are the list instead (#932).
    /// </summary>
    public bool ShowHitList => HasMultipleHits && !Warnings.HasList;

    /// <summary>
    /// The Warning cards of the pick (#932): a card per navigational warning
    /// part when it hit two or more, which replace the hit list, else the lone
    /// card of a selected warning.
    /// </summary>
    public PickWarningCardsViewModel Warnings { get; }

    /// <summary>
    /// The selected row in the hits listed below the Warning cards
    /// (<see cref="PickWarningCardsViewModel.OtherHits"/>):
    /// <see cref="SelectedHit"/> when it is one of them, else <c>null</c>.
    /// Setting <c>null</c> keeps the selection, so opening a card doesn't
    /// bounce off the rows.
    /// </summary>
    public PickHit? SelectedOtherHit
    {
        get => _selectedHit is not null && Warnings.OtherHits.Contains(_selectedHit) ? _selectedHit : null;
        set
        {
            if (value is not null)
                SelectedHit = value;
            else
                OnPropertyChanged();
        }
    }

    /// <summary>
    /// The hit whose Warning card the pointer is over, previewed on the map
    /// with the pick highlight; <c>null</c> when none is.
    /// </summary>
    public PickHit? PreviewHit => Warnings.PreviewHit;

    /// <summary>What Object Information shows, published for those that follow it (#1397).</summary>
    public ObjectInformationSelection Selection { get; }

    /// <summary>
    /// Attribute rows for the picked feature, decoded against the dataset's
    /// Feature Catalogue when one is available. Complex attributes nest their
    /// sub-rows via <see cref="PickAttribute.Children"/>; the panel renders
    /// the collection through a TreeView.
    /// </summary>
    public ObservableCollection<PickAttribute> Attributes { get; } = new();

    /// <summary>True when the current pick has at least one displayable attribute.</summary>
    public bool HasAttributes => Attributes.Count > 0;

    /// <summary>
    /// The number of attribute values, counting a complex attribute's leaves
    /// at any depth, shown on the folded Attributes header beneath a Warning card.
    /// </summary>
    public int AttributeCount => Attributes.Sum(LeafCount);

    private static int LeafCount(PickAttribute attribute) =>
        attribute.Children.Count == 0 ? 1 : attribute.Children.Sum(LeafCount);

    /// <summary>
    /// Whether the attribute table shows inline: not when a station chart
    /// or a Warning card leads, where it folds into an expander.
    /// </summary>
    public bool ShowInlineAttributes => !HasStationSeries && !Warnings.HasCard;

    /// <summary>
    /// Whether the attributes folded beneath a Warning card are open. Closed
    /// by default; the choice holds across picks.
    /// </summary>
    public bool WarningAttributesExpanded
    {
        get => _warningAttributesExpanded;
        set => SetProperty(ref _warningAttributesExpanded, value);
    }

    private void OnCardCopyRequested(object? sender, string text) =>
        CopyIdentityRequested?.Invoke(this, text);

    /// <summary>
    /// Long-text cards for the picked feature, lifted out of
    /// <see cref="Attributes"/>: resolved <c>fileReference</c> text (S-101
    /// Feature Catalogue; aliases <c>TXTDSC</c> / <c>NTXTDS</c>) first, then
    /// long text values in attribute order. Each shows its whole text in the
    /// shared long-text body (#932).
    /// </summary>
    public ObservableCollection<PickTextCardViewModel> TextCards { get; } = new();

    /// <summary>True when the current pick has at least one long-text card.</summary>
    public bool HasTextCards => TextCards.Count > 0;

    /// <summary>
    /// xlink-style references the currently selected hit points to.
    /// Surfaced in the panel above the attributes table; clicking a row
    /// invokes <see cref="NavigateCommand"/> to re-open the panel on the
    /// referenced feature.
    /// </summary>
    public ObservableCollection<FeatureReference> References { get; } = new();

    /// <summary>True when the selected hit has at least one outbound reference.</summary>
    public bool HasReferences => References.Count > 0;

    /// <summary>
    /// Invoked from the References list. Parameter is the
    /// <see cref="FeatureReference"/> to follow, which the pick service
    /// opens in the selected hit's dataset (saying so when it isn't found).
    /// </summary>
    public ICommand NavigateCommand { get; }

    /// <summary>Clears the panel.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>
    /// Copies <see cref="Location"/> to the clipboard as signed decimal
    /// degrees. Enabled only when <see cref="HasLocation"/> is true; raises
    /// <see cref="CopyLocationRequested"/> with the clipboard text so the
    /// view owns the actual clipboard access (keeping the view-model
    /// unit-testable without a clipboard backend).
    /// </summary>
    public ICommand CopyLocationCommand { get; }

    /// <summary>
    /// Copies the selected hit's identity (heading + technical caption) to
    /// the clipboard. Enabled only when a hit is selected; raises
    /// <see cref="CopyIdentityRequested"/> with the text so the view owns
    /// the actual clipboard access.
    /// </summary>
    public ICommand CopyIdentityCommand { get; }

    /// <summary>
    /// Raised when the user invokes <see cref="CopyLocationCommand"/>. The
    /// payload is the clipboard-ready coordinate text.
    /// </summary>
    public event EventHandler<string>? CopyLocationRequested;

    /// <summary>
    /// Raised when the user invokes <see cref="CopyIdentityCommand"/>. The
    /// payload is the clipboard-ready identity text.
    /// </summary>
    public event EventHandler<string>? CopyIdentityRequested;

    /// <summary>
    /// "Take the helm of this vessel" (pirate mode). Parameter is the
    /// <see cref="DynamicPickHit"/> to impersonate. Enabled only for AIS
    /// hits (feature id <c>"ais:{mmsi}"</c>); raises
    /// <see cref="TakeHelmRequested"/> with the parsed MMSI so the app
    /// can engage <c>PirateModeController</c>. The view-model owns no
    /// service references directly so unit tests can drive it without a
    /// controller double.
    /// </summary>
    public ICommand TakeHelmCommand { get; }

    /// <summary>
    /// Raised when the user invokes <see cref="TakeHelmCommand"/> on an
    /// AIS hit. The payload is the target's MMSI.
    /// </summary>
    public event EventHandler<uint>? TakeHelmRequested;

    /// <summary>
    /// Opens the selected hit's feature in the S-100 Feature Catalogue
    /// eXaminer (issue #442). Enabled only when the examiner hosts the
    /// hit's product spec and a feature type is known.
    /// </summary>
    public ICommand OpenFeatureInExaminerCommand { get; }

    /// <summary>
    /// Opens a specific attribute (parameter) of the selected hit's feature
    /// in the S-100 Feature Catalogue eXaminer (issue #442). Enabled only
    /// when the examiner hosts the hit's product spec.
    /// </summary>
    public ICommand OpenAttributeInExaminerCommand { get; }

    /// <summary>
    /// True when the S-100 Feature Catalogue eXaminer integration is enabled
    /// and hosts a catalogue for the selected hit's product spec. Gates the
    /// feature- and attribute-level "open in eXaminer" affordances.
    /// </summary>
    public bool IsExaminerAvailable => _examinerLinks.SupportsSpec(ProductSpec);

    /// <summary>
    /// Re-evaluates the examiner affordances against the current settings.
    /// Called when the user toggles the integration or changes the base URL
    /// so the feature- and attribute-level "open in eXaminer" buttons
    /// appear/disappear immediately, without waiting for the next pick
    /// (issue #442).
    /// </summary>
    public void RefreshExaminerAvailability()
    {
        OnPropertyChanged(nameof(IsExaminerAvailable));
        (OpenFeatureInExaminerCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenAttributeInExaminerCommand as RelayCommand<PickAttribute>)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reads the MMSI of <paramref name="hit"/> when it represents an AIS
    /// target. AIS feature ids follow the
    /// <c>"ais:{mmsi}"</c> convention defined by
    /// <c>AisDynamicFeatureSource.FeatureIdForMmsi</c>.
    /// </summary>
    internal static bool TryGetAisMmsi(DynamicPickHit hit, out uint mmsi)
    {
        mmsi = 0;
        if (hit is null) return false;
        const string prefix = "ais:";
        var id = hit.FeatureId;
        if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return uint.TryParse(id.AsSpan(prefix.Length), out mmsi) && mmsi != 0;
    }

    /// <summary>
    /// Dynamic-source hits collected by
    /// <see cref="Services.DynamicSources.IDynamicSourcePickService"/>.
    /// Rendered in a sibling section beneath the dataset hit list. The
    /// section is hidden when empty.
    /// </summary>
    public ObservableCollection<DynamicPickHit> DynamicHits { get; } = new();

    /// <summary>True when at least one dynamic-source hit was returned by the most recent pick.</summary>
    public bool HasDynamicHits => DynamicHits.Count > 0;

    /// <summary>
    /// Shows a pick result: its dataset hits AND dynamic-source hits. Either
    /// list may be empty; both being empty is equivalent to
    /// <see cref="Clear"/>. When dataset hits are non-empty the first hit (or
    /// the first Warning card's) drives the detail view; when only dynamic hits
    /// are present the detail view is left empty and only the dynamic section
    /// is shown. The pick's location is surfaced in the panel and copyable to
    /// the clipboard, with the depth card when the result has a depth there.
    /// </summary>
    /// <param name="result">What the pick found.</param>
    public void SetPicks(PickResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var hits = result.Hits;
        var dynamicHits = result.DynamicHits;

        DisposeHitResources();
        Hits.Clear();
        foreach (var hit in hits)
            Hits.Add(hit);
        Warnings.Show(hits);

        DynamicHits.Clear();
        foreach (var dh in dynamicHits)
            DynamicHits.Add(dh);

        if (result.IsEmpty)
        {
            Clear();
            return;
        }

        Location = result.Location;
        LocationDepthSeries = BuildDepthCard(result);
        var hadPick = _hasPick;
        HasPick = true;
        // Every pick shows Object information, even while the right dock is on
        // another tab such as the Legend (#1404, legends design note D2).
        if (hadPick)
            ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasMultipleHits));
        OnPropertyChanged(nameof(ShowHitList));
        OnPropertyChanged(nameof(SelectedOtherHit));
        OnPropertyChanged(nameof(HasDynamicHits));
        OnPropertyChanged(nameof(HasDatasetPick));
        SelectedHit = Warnings.Cards.Count > 0 ? Warnings.Cards[0].Hit : hits.Count > 0 ? hits[0] : null;
        RefreshLegendKey();
        if (hits.Count == 0)
        {
            // ApplyHitToDetailFields(null) cleared the detail fields;
            // re-raise the attribute panels so the view collapses them.
            OnPropertyChanged(nameof(HasAttributes));
            OnPropertyChanged(nameof(HasTextCards));
            OnPropertyChanged(nameof(HasReferences));
        }
    }

    // The depth card for the pick's location, in the mariner's depth unit and
    // with their safety depth, saying how the chart's depths relate (#1191).
    private DepthOverTimeViewModel? BuildDepthCard(PickResult result)
    {
        if (result is not { LocationDepth: { } depth, Location: { } location })
            return null;
        var settings = _charts.MarinerSettings.Current;
        var card = new LocationDepthCard(
            depth,
            LatLonFormatter.FormatDecimal(location.Latitude, location.Longitude),
            location.Latitude,
            location.Longitude,
            settings.DepthUnit,
            settings.SafetyDepth.TotalMetres);
        return new DepthOverTimeViewModel(card, _charts) { ChartBasisLine = _chartBasisLine };
    }

    /// <summary>Clears all pick state and sets <see cref="HasPick"/> to false.</summary>
    public void Clear()
    {
        DisposeHitResources();
        Warnings.Clear();
        OnPropertyChanged(nameof(ShowHitList));
        OnPropertyChanged(nameof(SelectedOtherHit));
        Hits.Clear();
        DynamicHits.Clear();
        // SelectedHit setter rejects identical references; clear the backing
        // field directly so we always raise PropertyChanged when something
        // was selected.
        if (_selectedHit is not null)
        {
            _selectedHit = null;
            OnPropertyChanged(nameof(SelectedHit));
        }

        FeatureType = null;
        FeatureTypeName = null;
        FeatureRef = null;
        DatasetFileName = null;
        ProductSpec = null;
        Attributes.Clear();
        ClearTextCards();
        References.Clear();
        _sections = [];
        _stationChart = null;
        Location = null;
        LocationDepthSeries = null;
        HasPick = false;
        OnPropertyChanged(nameof(HasAttributes));
        OnPropertyChanged(nameof(HasTextCards));
        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(SelectedStationSeries));
        OnPropertyChanged(nameof(HasStationSeries));
        OnPropertyChanged(nameof(ShowInlineAttributes));
        OnPropertyChanged(nameof(SelectedSections));
        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(SelectedEggCode));
        OnPropertyChanged(nameof(HasEggCode));
        OnPropertyChanged(nameof(HasMultipleHits));
        OnPropertyChanged(nameof(HasDynamicHits));
        OnPropertyChanged(nameof(HasDatasetPick));
        RefreshLegendKey();
    }

    private void ApplyHitToDetailFields(PickHit? hit)
    {
        Warnings.Select(hit);
        _stationChart = hit is null ? null : StationChartFor(hit);
        if (hit is null)
        {
            FeatureType = null;
            FeatureTypeName = null;
            FeatureRef = null;
            DatasetFileName = null;
            ProductSpec = null;
            Attributes.Clear();
            ClearTextCards();
            References.Clear();
            _sections = [];
            OnPropertyChanged(nameof(HasAttributes));
            OnPropertyChanged(nameof(HasTextCards));
            OnPropertyChanged(nameof(HasReferences));
            OnPropertyChanged(nameof(SelectedStationSeries));
            OnPropertyChanged(nameof(HasStationSeries));
            OnPropertyChanged(nameof(ShowInlineAttributes));
            OnPropertyChanged(nameof(SelectedSections));
            OnPropertyChanged(nameof(HasSections));
            OnPropertyChanged(nameof(SelectedEggCode));
            OnPropertyChanged(nameof(HasEggCode));
            RaiseIdentityChanged();
            return;
        }

        FeatureType = hit.FeatureType;
        FeatureTypeName = hit.FeatureTypeName;
        FeatureRef = hit.FeatureRef;
        DatasetFileName = hit.DatasetFileName;
        ProductSpec = hit.ProductSpec;

        PopulateAttributeSections(FormatAttributes(hit.Attributes));

        References.Clear();
        foreach (var reference in hit.References)
            References.Add(reference);
        OnPropertyChanged(nameof(HasReferences));

        _sections = [.. hit.Sections.Select(PickSectionPresenters.Default.Present)];

        OnPropertyChanged(nameof(SelectedStationSeries));
        OnPropertyChanged(nameof(HasStationSeries));
        OnPropertyChanged(nameof(ShowInlineAttributes));
        OnPropertyChanged(nameof(SelectedSections));
        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(SelectedEggCode));
        OnPropertyChanged(nameof(HasEggCode));
        RaiseIdentityChanged();
    }

    // A station hit's chart, made when it is first selected and kept until the
    // next pick: S-104 charts the water level, S-111 speed and direction.
    private StationTimeSeriesViewModel? StationChartFor(PickHit hit)
    {
        if (hit.StationSeries is not { } snapshot)
            return null;
        if (_stationCharts.TryGetValue(hit, out var chart))
            return chart;
        chart = hit.ProductSpec switch
        {
            "S-104" => new S104StationTimeSeriesViewModel(snapshot, _charts),
            "S-111" => new S111StationTimeSeriesViewModel(snapshot, _charts),
            _ => null,
        };
        if (chart is not null)
            _stationCharts[hit] = chart;
        return chart;
    }

    /// <summary>
    /// Raises change notifications for the identity-block properties derived
    /// from <see cref="SelectedHit"/> and refreshes the copy-identity
    /// command's executability.
    /// </summary>
    private void RaiseIdentityChanged()
    {
        OnPropertyChanged(nameof(PrimaryLabel));
        OnPropertyChanged(nameof(SecondaryLabel));
        OnPropertyChanged(nameof(HasSecondaryLabel));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(IdentityCaption));
        OnPropertyChanged(nameof(PortrayalNote));
        OnPropertyChanged(nameof(HasPortrayalNote));
        OnPropertyChanged(nameof(Aml));
        OnPropertyChanged(nameof(HasAml));
        OnPropertyChanged(nameof(AmlNotDrawnNote));
        OnPropertyChanged(nameof(HasAmlNotDrawnNote));
        OnPropertyChanged(nameof(NavigationNotice));
        OnPropertyChanged(nameof(HasNavigationNotice));
        OnPropertyChanged(nameof(DataCredit));
        OnPropertyChanged(nameof(HasDataCredit));
        OnPropertyChanged(nameof(StaleNotice));
        OnPropertyChanged(nameof(HasStaleNotice));
        OnPropertyChanged(nameof(IsExaminerAvailable));
        (CopyIdentityCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenFeatureInExaminerCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenAttributeInExaminerCommand as RelayCommand<PickAttribute>)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The station chart of <see cref="SelectedHit"/>, or <c>null</c> when
    /// the selected hit is not an S-104 or S-111 station observation. Bound
    /// by the pick panel's chart section.
    /// </summary>
    public StationTimeSeriesViewModel? SelectedStationSeries => _stationChart;

    /// <summary>True when <see cref="SelectedStationSeries"/> is non-null.</summary>
    public bool HasStationSeries => _stationChart is not null;

    /// <summary>
    /// The view models of <see cref="SelectedHit"/>'s pick sections, one per
    /// <see cref="PickSection"/>, built by <see cref="PickSectionPresenters"/>:
    /// an <see cref="EggCodeViewModel"/> for an ice egg code, a
    /// <see cref="PickSectionRowsViewModel"/> for a kind with no presenter.
    /// Bound by the pick panel's sections list.
    /// </summary>
    public IReadOnlyList<object> SelectedSections => _sections;

    /// <summary>True when <see cref="SelectedSections"/> isn't empty.</summary>
    public bool HasSections => _sections.Count > 0;

    /// <summary>
    /// The ice egg code among <see cref="SelectedSections"/>, or <c>null</c>
    /// when the selected hit has none.
    /// </summary>
    public EggCodeViewModel? SelectedEggCode => _sections.OfType<EggCodeViewModel>().FirstOrDefault();

    /// <summary>True when <see cref="SelectedEggCode"/> is non-null.</summary>
    public bool HasEggCode => SelectedEggCode is not null;

    private void DisposeHitResources()
    {
        foreach (var chart in _stationCharts.Values)
            chart.Dispose();
        _stationCharts.Clear();
        // A dynamic hit's rich card (a predicted tide station's) follows the
        // view time until the pick is replaced.
        foreach (var hit in DynamicHits)
            hit.Card?.Release();
    }
}
