using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
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
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly IMarinerSettingsProvider? _marinerSettings;
    private readonly IUrlOpener? _urlOpener;
    private readonly IS100ExaminerLinkBuilder? _examinerLinks;
    private string? _featureType;
    private string? _featureTypeName;
    private string? _featureRef;
    private string? _datasetFileName;
    private string? _productSpec;
    private bool _hasPick;
    private PickHit? _selectedHit;
    private EggCodeViewModel? _eggCode;
    private PickLocation? _location;
    private DepthOverTimeViewModel? _locationDepth;

    public PickReportViewModel()
        : this(timeFormat: null, marinerSettings: null)
    {
    }

    public PickReportViewModel(ITimeFormatProvider? timeFormat)
        : this(timeFormat, marinerSettings: null)
    {
    }

    public PickReportViewModel(
        ITimeFormatProvider? timeFormat,
        IMarinerSettingsProvider? marinerSettings)
        : this(timeFormat, marinerSettings, urlOpener: null, examinerLinks: null)
    {
    }

    public PickReportViewModel(
        ITimeFormatProvider? timeFormat,
        IMarinerSettingsProvider? marinerSettings,
        IUrlOpener? urlOpener,
        IS100ExaminerLinkBuilder? examinerLinks)
    {
        _timeFormat = timeFormat;
        _marinerSettings = marinerSettings;
        _urlOpener = urlOpener;
        _examinerLinks = examinerLinks;
        ClearCommand = new RelayCommand(Clear);
        CopyLocationCommand = new RelayCommand(
            () => { if (_location is { } loc) CopyLocationRequested?.Invoke(this, LatLonFormatter.FormatDecimal(loc.Latitude, loc.Longitude)); },
            () => _location is not null);
        CopyIdentityCommand = new RelayCommand(
            () => { var text = IdentityClipboardText; if (!string.IsNullOrEmpty(text)) CopyIdentityRequested?.Invoke(this, text); },
            () => _selectedHit is not null);
        CopyReferencedTextCommand = new RelayCommand<PickReferencedText>(
            t => { if (t is not null && !string.IsNullOrEmpty(t.ClipboardText)) CopyIdentityRequested?.Invoke(this, t.ClipboardText); },
            t => t is not null && !string.IsNullOrEmpty(t.ClipboardText));
        NavigateCommand = new RelayCommand<FeatureReference>(
            r => { if (r is not null) NavigateRequested?.Invoke(this, r); },
            r => r is not null);
        TakeHelmCommand = new RelayCommand<DynamicPickHit>(
            hit => { if (hit is not null && TryGetAisMmsi(hit, out var mmsi)) TakeHelmRequested?.Invoke(this, mmsi); },
            hit => hit is not null && TryGetAisMmsi(hit, out _));
        OpenFeatureInExaminerCommand = new RelayCommand(
            OpenFeatureInExaminer,
            () => IsExaminerAvailable && !string.IsNullOrWhiteSpace(FeatureType));
        OpenAttributeInExaminerCommand = new RelayCommand<PickAttribute>(
            OpenAttributeInExaminer,
            a => IsExaminerAvailable && a is not null && !string.IsNullOrWhiteSpace(a.Code));

        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnTimeFormatChanged;

        if (_marinerSettings is not null)
            _marinerSettings.Changed += OnMarinerSettingsChanged;
    }

    private void OnTimeFormatChanged(TimeFormat _)
    {
        ReformatAttributesFromSelectedHit();
    }

    private void OnMarinerSettingsChanged(MarinerSettings _)
    {
        // DepthUnit is the only mariner setting the pick panel renders;
        // re-run the same projection used for time-format changes so
        // depth-typed rows pick up the new unit immediately.
        ReformatAttributesFromSelectedHit();
    }

    private void ReformatAttributesFromSelectedHit()
    {
        if (_selectedHit is null) return;
        PopulateAttributeSections(ReformatTypedAttributes(_selectedHit.Attributes));
    }

    /// <summary>
    /// Splits the reformatted attribute tree into the key/value
    /// <see cref="Attributes"/> table and the standalone
    /// <see cref="ReferencedTexts"/> cards, then publishes both. Resolved
    /// <c>fileReference</c> rows (S-101 FC; alias TXTDSC / NTXTDS) are lifted
    /// out of the table so their text is read as its own block rather than a
    /// monospace dump inside the table.
    /// </summary>
    private void PopulateAttributeSections(IReadOnlyList<PickAttribute> reformatted)
    {
        Attributes.Clear();
        foreach (var a in FeatureInfoBuilder.WithoutResolvedFileReferences(reformatted))
            Attributes.Add(a);
        OnPropertyChanged(nameof(HasAttributes));

        ReferencedTexts.Clear();
        foreach (var fileRef in FeatureInfoBuilder.CollectResolvedFileReferences(reformatted))
            ReferencedTexts.Add(PickReferencedText.FromAttribute(fileRef));
        OnPropertyChanged(nameof(HasReferencedText));
    }

    private IReadOnlyList<PickAttribute> ReformatTypedAttributes(IReadOnlyList<PickAttribute> source)
    {
        var timeFmt = _timeFormat?.Current ?? TimeFormat.Local;
        var depthUnit = _marinerSettings?.Current.DepthUnit ?? DepthUnit.Metres;
        var list = new List<PickAttribute>(source.Count);
        foreach (var attr in source)
        {
            list.Add(ReformatOne(attr, timeFmt, depthUnit));
        }
        return list;
    }

    private PickAttribute ReformatOne(PickAttribute attr, TimeFormat timeFmt, DepthUnit depthUnit)
    {
        var children = attr.Children.Count == 0
            ? attr.Children
            : (IReadOnlyList<PickAttribute>)ReformatTypedAttributes(attr.Children);

        if (attr.DateTimeValue is { } dt)
        {
            return new PickAttribute
            {
                Code = attr.Code,
                Name = attr.Name,
                RawValue = attr.RawValue,
                DisplayValue = TimeFormatting.Format(dt, timeFmt),
                DateTimeValue = attr.DateTimeValue,
                DateTimeRangeValue = attr.DateTimeRangeValue,
                DepthMetresValue = attr.DepthMetresValue,
                ExternalText = attr.ExternalText,
                Children = children,
            };
        }

        if (attr.DateTimeRangeValue is { } range)
        {
            return new PickAttribute
            {
                Code = attr.Code,
                Name = attr.Name,
                RawValue = attr.RawValue,
                DisplayValue = TimeFormatting.FormatTimeRange(range.Start, range.End, timeFmt),
                DateTimeValue = attr.DateTimeValue,
                DateTimeRangeValue = attr.DateTimeRangeValue,
                DepthMetresValue = attr.DepthMetresValue,
                ExternalText = attr.ExternalText,
                Children = children,
            };
        }

        if (attr.DepthMetresValue is { } metres)
        {
            return new PickAttribute
            {
                Code = attr.Code,
                Name = attr.Name,
                RawValue = attr.RawValue,
                DisplayValue = DepthFormatting.Format(metres, depthUnit),
                DateTimeValue = attr.DateTimeValue,
                DateTimeRangeValue = attr.DateTimeRangeValue,
                DepthMetresValue = attr.DepthMetresValue,
                ExternalText = attr.ExternalText,
                Children = children,
            };
        }

        if (attr.Children.Count != 0 && !ReferenceEquals(children, attr.Children))
        {
            return new PickAttribute
            {
                Code = attr.Code,
                Name = attr.Name,
                RawValue = attr.RawValue,
                DisplayValue = attr.DisplayValue,
                DateTimeValue = attr.DateTimeValue,
                DateTimeRangeValue = attr.DateTimeRangeValue,
                DepthMetresValue = attr.DepthMetresValue,
                ExternalText = attr.ExternalText,
                Children = children,
            };
        }

        return attr;
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

    /// <summary>Clipboard text for the copy-identity action: heading plus caption.</summary>
    private string? IdentityClipboardText
    {
        get
        {
            if (_selectedHit is null)
                return null;
            var caption = IdentityCaption;
            return string.IsNullOrEmpty(caption)
                ? PrimaryLabel
                : $"{PrimaryLabel}\n{caption}";
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
                // should auto-open (PR-M4). Subsequent updates while still
                // HasPick=true do NOT re-raise.
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

    /// <summary>True when a depth-assimilation card is available for the pick.</summary>
    public bool HasLocationDepthSeries => _locationDepth is not null;

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
            ApplyHitToDetailFields(value);
        }
    }

    /// <summary>True when more than one feature was hit at the pick location.</summary>
    public bool HasMultipleHits => Hits.Count > 1;

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
    /// Externally referenced text blocks for the picked feature, lifted out
    /// of <see cref="Attributes"/> from resolved <c>fileReference</c>
    /// attributes (S-101 Feature Catalogue; aliases <c>TXTDSC</c> /
    /// <c>NTXTDS</c>). Rendered in the panel as labelled cards with the full
    /// text always visible, mirroring an ECDIS "show textual description"
    /// affordance.
    /// </summary>
    public ObservableCollection<PickReferencedText> ReferencedTexts { get; } = new();

    /// <summary>True when the current pick carries at least one referenced text block.</summary>
    public bool HasReferencedText => ReferencedTexts.Count > 0;

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
    /// <see cref="FeatureReference"/> to follow; the view-model raises
    /// <see cref="NavigateRequested"/> and the pick service (or any
    /// other subscriber) performs the actual lookup. The view-model
    /// owns no service references directly so unit tests can drive it
    /// without a pick-service double.
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
    /// Copies a <see cref="PickReferencedText"/> card's full text to the
    /// clipboard. The command parameter is the card to copy; raises
    /// <see cref="CopyIdentityRequested"/> with the text so the view owns the
    /// actual clipboard access.
    /// </summary>
    public ICommand CopyReferencedTextCommand { get; }

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
    public bool IsExaminerAvailable => _examinerLinks?.SupportsSpec(ProductSpec) ?? false;

    private void OpenFeatureInExaminer()
    {
        if (_urlOpener is null || _examinerLinks is null)
            return;
        var url = _examinerLinks.BuildFeatureUrl(ProductSpec, FeatureType);
        if (!string.IsNullOrEmpty(url))
            _urlOpener.Open(url);
    }

    private void OpenAttributeInExaminer(PickAttribute? attribute)
    {
        if (_urlOpener is null || _examinerLinks is null || attribute is null)
            return;
        var url = _examinerLinks.BuildAttributeUrl(ProductSpec, FeatureType, attribute.Code);
        if (!string.IsNullOrEmpty(url))
            _urlOpener.Open(url);
    }

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

    /// represents an AIS target. AIS feature ids follow the
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
    /// Raised when the user clicks a row in the References list.
    /// Subscribers (typically <see cref="Services.PickService"/>) are
    /// expected to resolve the reference and re-open the panel on the
    /// target feature.
    /// </summary>
    public event EventHandler<FeatureReference>? NavigateRequested;

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
    /// Replaces the current pick with the supplied list of hits. The first
    /// hit is selected by default. An empty list is equivalent to
    /// <see cref="Clear"/>.
    /// </summary>
    public void SetPicks(IReadOnlyList<PickHit> hits)
        => SetPicks(hits, Array.Empty<DynamicPickHit>());

    /// <summary>
    /// Replaces the current pick with the supplied dataset hits AND
    /// dynamic-source hits. Either list may be empty; both being empty
    /// is equivalent to <see cref="Clear"/>. When dataset hits are
    /// non-empty the first hit drives the detail view (existing
    /// behaviour); when only dynamic hits are present the detail view
    /// is left empty and only the dynamic section is shown.
    /// </summary>
    /// <param name="hits">Dataset-owned feature hits.</param>
    /// <param name="dynamicHits">Dynamic-source hits (AIS, own-ship, …).</param>
    /// <param name="location">
    /// Geographic location of the click that produced the pick, or
    /// <c>null</c> when no location is available (e.g. a programmatic
    /// open). Surfaced in the panel and copyable to the clipboard.
    /// </param>
    public void SetPicks(
        IReadOnlyList<PickHit> hits,
        IReadOnlyList<DynamicPickHit> dynamicHits,
        PickLocation? location = null,
        DepthOverTimeViewModel? locationDepth = null)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(dynamicHits);

        DisposeHitResources();
        Hits.Clear();
        foreach (var hit in hits)
            Hits.Add(hit);

        DynamicHits.Clear();
        foreach (var dh in dynamicHits)
            DynamicHits.Add(dh);

        if (hits.Count == 0 && dynamicHits.Count == 0)
        {
            locationDepth?.Dispose();
            Clear();
            return;
        }

        Location = location;
        LocationDepthSeries = locationDepth;
        HasPick = true;
        OnPropertyChanged(nameof(HasMultipleHits));
        OnPropertyChanged(nameof(HasDynamicHits));
        OnPropertyChanged(nameof(HasDatasetPick));
        SelectedHit = hits.Count > 0 ? hits[0] : null;
        if (hits.Count == 0)
        {
            // ApplyHitToDetailFields(null) cleared the detail fields;
            // re-raise the attribute panels so the view collapses them.
            OnPropertyChanged(nameof(HasAttributes));
            OnPropertyChanged(nameof(HasReferencedText));
            OnPropertyChanged(nameof(HasReferences));
        }
    }

    /// <summary>
    /// Convenience overload that wraps a single-feature pick into the
    /// multi-hit shape. Preserved for callers that haven't migrated to
    /// <see cref="SetPicks"/>.
    /// </summary>
    public void SetPick(
        string featureType,
        string? featureTypeName,
        string featureRef,
        string? datasetFileName,
        string? productSpec,
        IReadOnlyList<PickAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        ArgumentNullException.ThrowIfNull(featureRef);
        ArgumentNullException.ThrowIfNull(attributes);

        SetPicks(new[]
        {
            new PickHit
            {
                FeatureType = featureType,
                FeatureTypeName = featureTypeName,
                FeatureRef = featureRef,
                DatasetFileName = datasetFileName,
                ProductSpec = productSpec,
                Attributes = attributes,
            },
        });
    }

    /// <summary>Clears all pick state and sets <see cref="HasPick"/> to false.</summary>
    public void Clear()
    {
        DisposeHitResources();
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
        ReferencedTexts.Clear();
        References.Clear();
        _eggCode = null;
        Location = null;
        LocationDepthSeries = null;
        HasPick = false;
        OnPropertyChanged(nameof(HasAttributes));
        OnPropertyChanged(nameof(HasReferencedText));
        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(SelectedEggCode));
        OnPropertyChanged(nameof(HasEggCode));
        OnPropertyChanged(nameof(HasMultipleHits));
        OnPropertyChanged(nameof(HasDynamicHits));
        OnPropertyChanged(nameof(HasDatasetPick));
    }

    private void ApplyHitToDetailFields(PickHit? hit)
    {
        if (hit is null)
        {
            FeatureType = null;
            FeatureTypeName = null;
            FeatureRef = null;
            DatasetFileName = null;
            ProductSpec = null;
            Attributes.Clear();
            ReferencedTexts.Clear();
            References.Clear();
            _eggCode = null;
            OnPropertyChanged(nameof(HasAttributes));
            OnPropertyChanged(nameof(HasReferencedText));
            OnPropertyChanged(nameof(HasReferences));
            OnPropertyChanged(nameof(SelectedStationSeries));
            OnPropertyChanged(nameof(HasStationSeries));
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

        PopulateAttributeSections(ReformatTypedAttributes(hit.Attributes));

        References.Clear();
        foreach (var reference in hit.References)
            References.Add(reference);
        OnPropertyChanged(nameof(HasReferences));

        _eggCode = hit.EggCode is { } egg ? new EggCodeViewModel(egg) : null;

        OnPropertyChanged(nameof(SelectedStationSeries));
        OnPropertyChanged(nameof(HasStationSeries));
        OnPropertyChanged(nameof(SelectedEggCode));
        OnPropertyChanged(nameof(HasEggCode));
        RaiseIdentityChanged();
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
        OnPropertyChanged(nameof(IsExaminerAvailable));
        (CopyIdentityCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenFeatureInExaminerCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenAttributeInExaminerCommand as RelayCommand<PickAttribute>)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Station time-series view model attached to <see cref="SelectedHit"/>,
    /// or <c>null</c> when the selected hit is not a station observation.
    /// Bound by the pick panel's chart section.
    /// </summary>
    public StationTimeSeriesViewModel? SelectedStationSeries => _selectedHit?.StationSeries;

    /// <summary>True when <see cref="SelectedStationSeries"/> is non-null.</summary>
    public bool HasStationSeries => _selectedHit?.StationSeries is not null;

    /// <summary>
    /// WMO / SIGRID-3 ice egg-code view model attached to
    /// <see cref="SelectedHit"/>, or <c>null</c> when the selected hit is not
    /// an S-411 sea-ice / lake-ice feature. Bound by the pick panel's
    /// egg-code section.
    /// </summary>
    public EggCodeViewModel? SelectedEggCode => _eggCode;

    /// <summary>True when <see cref="SelectedEggCode"/> is non-null.</summary>
    public bool HasEggCode => _eggCode is not null;

    private void DisposeHitResources()
    {
        foreach (var hit in Hits)
            hit.StationSeries?.Dispose();
    }
}
