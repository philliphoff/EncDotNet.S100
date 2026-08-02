using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class DatasetEntry : ViewModelBase
{
    private readonly MapDatasetId _id;
    private MapDataset? _mapDataset;

    /// <summary>
    /// Stable renderer-neutral identity projected from <see cref="MapDataset"/>
    /// after the dataset has loaded.
    /// </summary>
    public MapDatasetId Id => _mapDataset?.Id ?? _id;

    /// <summary>
    /// Renderer-neutral loaded state projected by this view-model, or
    /// <c>null</c> until the entry first loads. Lazy eviction retains the last
    /// snapshot so user-controlled display state survives a later reload.
    /// </summary>
    public MapDataset? MapDataset => _mapDataset;

    public string FilePath { get; }
    public string DisplayName { get; }
    public string ProductSpec { get; }

    /// <summary>
    /// Optional asset source backing this dataset. When non-null, the
    /// loader reads the dataset bytes from <see cref="Source"/> at
    /// <see cref="RelativePath"/> instead of opening
    /// <see cref="FilePath"/> directly. Set when the entry was added
    /// from an exchange set (folder or ZIP); null for plain file
    /// loads. Lifetime is owned by the producer (typically
    /// <see cref="EncDotNet.S100.Viewer.Services.IExchangeSetService"/>).
    /// </summary>
    public IAssetSource? Source { get; }

    /// <summary>
    /// Path of this dataset relative to <see cref="Source"/>, or
    /// <c>null</c> when the entry is a plain file load.
    /// </summary>
    public string? RelativePath { get; }

    /// <summary>True when this entry's bytes live inside an exchange-set asset source.</summary>
    public bool IsFromExchangeSet => Source is not null;

    /// <summary>
    /// Source-relative paths of the S-101 sequential update files
    /// (<c>….001</c>, <c>….002</c>, …) to apply over the base cell at
    /// <see cref="RelativePath"/>, in ascending update-number order.
    /// Empty for plain file loads, non-S-101 datasets, and S-101 cells
    /// with no in-set updates. When non-empty, the loader builds the
    /// up-to-date dataset via
    /// <see cref="EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory.CreateS101ProcessorWithUpdates"/>.
    /// S-101 / S-100 Part 10a.
    /// </summary>
    public IReadOnlyList<string> UpdateRelativePaths { get; }

    /// <summary>True when this entry has in-set S-101 updates to apply.</summary>
    public bool HasUpdates => UpdateRelativePaths.Count > 0;

    /// <summary>
    /// The coarsest scale denominator (largest value) at which this cell is
    /// intended to display, from the exchange-set catalogue's
    /// <c>dataCoverage/minimumDisplayScale</c> (S-100 Part 17;
    /// S-101 FC §3.1.1 <c>DataCoverage</c>). <see langword="null"/> for plain
    /// file loads and exchange sets that omit the metadata (e.g. S-57).
    /// </summary>
    /// <remarks>
    /// Drives the hole-safe per-cell zoom-out visibility window (issue #438
    /// Phase 1): a cell stops drawing once the viewport is zoomed out beyond
    /// this denominator, so finer nested cells (smaller
    /// <see cref="MinimumDisplayScale"/>) drop out first and the coarser cell
    /// underneath remains — removing the redundant overlapping stack without
    /// leaving gaps.
    /// </remarks>
    public int? MinimumDisplayScale { get; }

    /// <summary>
    /// The finest scale denominator (smallest value) at which this cell is
    /// intended to display, from the exchange-set catalogue's
    /// <c>dataCoverage/maximumDisplayScale</c> (S-100 Part 17;
    /// S-101 FC §3.1.1 <c>DataCoverage</c>). <see langword="null"/> when the
    /// metadata is absent.
    /// </summary>
    /// <remarks>
    /// Carried for completeness / future use. The zoom-in (under-scale) cutoff
    /// it would drive is <em>not</em> applied in Phase 1 because hiding a
    /// coarser cell where a finer cell covers only part of its footprint would
    /// leave holes; that suppression is deferred to the coverage-clipping work
    /// (issue #438 Phase 2).
    /// </remarks>
    public int? MaximumDisplayScale { get; }

    /// <summary>
    /// The cell's geographic (EPSG:4326) footprint as declared in the
    /// exchange-set catalogue, known <em>before</em> the dataset is parsed.
    /// <see langword="null"/> for plain file loads and catalogues that omit
    /// coverage (e.g. container-style features). Drives viewport-driven lazy
    /// loading: the coordinator culls out-of-view cells by this box without
    /// touching their bytes. See issue #458.
    /// </summary>
    public ExchangeSets.BoundingBox? GeographicBounds { get; }

    /// <summary>
    /// The ENC navigational-purpose band (1&#160;Overview .. 6&#160;Berthing)
    /// parsed from the cell name (S-57 Ed 3.1 App&#160;B.1 / S-101 §5.5), or
    /// <see langword="null"/> when the name is not a recognised ENC cell.
    /// Used as a load-free scale proxy for lazy loading (issue #458).
    /// </summary>
    public int? UsageBand { get; }

    private bool _isDeferred;
    /// <summary>
    /// True when this entry has been <em>registered</em> from a large exchange
    /// set but its bytes have not yet been loaded — it appears in the Datasets
    /// panel (dimmed) and as a map extent outline, and is loaded on demand when
    /// it enters the viewport at a relevant scale. Cleared once the cell loads.
    /// See issue #458.
    /// </summary>
    public bool IsDeferred
    {
        get => _isDeferred;
        set
        {
            if (SetProperty(ref _isDeferred, value))
                OnPropertyChanged(nameof(RowOpacity));
        }
    }

    private Mapsui.MRect? _mercatorExtent;
    /// <summary>
    /// The dataset's EPSG:3857 (web-mercator) extent, captured from the
    /// renderer's <c>MapsuiDatasetResult.Extent</c> the first time the dataset is
    /// rendered. <see langword="null"/> until the dataset has been loaded (and
    /// for out-of-range time-gated entries that produced no layers). Used to
    /// zoom/pan the map to this dataset (double-click reveal) and to draw the
    /// out-of-scale extent indicator (issue #446).
    /// </summary>
    public Mapsui.MRect? MercatorExtent
    {
        get => _mercatorExtent;
        set => SetProperty(ref _mercatorExtent, value);
    }

    private double? _contentMaxVisibleResolution;
    /// <summary>
    /// The coarsest EPSG:3857 resolution (metres per pixel) at which this
    /// dataset's content still draws, i.e. the whole-cell zoom-out cutoff that
    /// <see cref="EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer.ApplyCellScaleWindow"/>
    /// imposed from <see cref="MinimumDisplayScale"/> (issue #438 Phase 1).
    /// Once the viewport resolution exceeds this value the dataset renders
    /// nothing; the out-of-scale extent indicator (issue #446) uses it as its
    /// <c>MinVisible</c> so the accent border appears exactly when the content
    /// drops out. <see langword="null"/> when no scale window was applied (no
    /// <see cref="MinimumDisplayScale"/>, or the mariner opted to ignore scale
    /// minima), meaning the dataset never disappears on zoom-out.
    /// </summary>
    public double? ContentMaxVisibleResolution
    {
        get => _contentMaxVisibleResolution;
        set => SetProperty(ref _contentMaxVisibleResolution, value);
    }

    private bool _isLoaded;
    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    /// <summary>
    /// True when the dataset's declared product-spec edition diverges from
    /// the edition this build implements in a way that may degrade rendering
    /// (issue #248). Drives the persistent warning badge in the dataset list.
    /// </summary>
    public bool HasVersionWarning => VersionAssessment?.IsWarning == true;

    /// <summary>
    /// The human-readable warning shown as the badge tooltip, or <c>null</c>
    /// when <see cref="HasVersionWarning"/> is false.
    /// </summary>
    public string? VersionWarningTooltip =>
        HasVersionWarning ? VersionAssessment?.BuildMessage() : null;

    private SpecVersionAssessment? _versionAssessment;
    private SpecVersionAssessment? VersionAssessment =>
        _mapDataset?.VersionAssessment ?? _versionAssessment;

    /// <summary>
    /// Records the dataset's spec-version assessment, raising the warning
    /// badge when the divergence is significant. A <c>null</c> assessment or
    /// a non-warning assessment clears the badge.
    /// </summary>
    public void SetVersionAssessment(SpecVersionAssessment? assessment)
    {
        if (ReferenceEquals(VersionAssessment, assessment)) return;

        _versionAssessment = assessment;
        UpdateMapDataset();
        OnPropertyChanged(nameof(HasVersionWarning));
        OnPropertyChanged(nameof(VersionWarningTooltip));
    }

    // ── Per-dataset display state ─────────────────────────────────────
    //
    // These properties drive the underlying Mapsui ILayer.Enabled and
    // ILayer.Opacity values via MapsuiMapSession. They survive
    // re-renders (palette switches, time-step scrubs) because the session
    // reapplies them when replacing generated layers.

    private bool _isVisible = true;
    /// <summary>
    /// Whether this dataset's layers are drawn on the map. Toggling
    /// this updates <see cref="Mapsui.Layers.ILayer.Enabled"/> on every
    /// layer the loader has produced for this entry.
    /// </summary>
    public bool IsVisible
    {
        get => _mapDataset?.IsVisible ?? _isVisible;
        set
        {
            if (IsVisible != value)
            {
                _isVisible = value;
                UpdateMapDataset();
                OnPropertyChanged();
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    private bool _isActive = true;
    /// <summary>
    /// Whether this dataset participates in S-98 composition and queries,
    /// independently of <see cref="IsVisible"/>.
    /// </summary>
    public bool IsActive
    {
        get => _mapDataset?.IsActive ?? _isActive;
        set
        {
            if (IsActive == value) return;
            _isActive = value;
            UpdateMapDataset();
            OnPropertyChanged();
        }
    }

    private double _opacity = 1.0;
    /// <summary>
    /// Opacity factor applied to this dataset's layers, in the range
    /// 0..1. Updates <see cref="Mapsui.Layers.ILayer.Opacity"/>.
    /// </summary>
    public double Opacity
    {
        get => _mapDataset?.Opacity ?? _opacity;
        set
        {
            var clamped = value < 0 ? 0 : value > 1 ? 1 : value;
            if (Opacity == clamped) return;
            _opacity = clamped;
            UpdateMapDataset();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// UI helper: dims the row text when the dataset is hidden or still
    /// deferred (registered from a large exchange set but not yet loaded).
    /// </summary>
    public double RowOpacity => (!IsVisible || _isDeferred) ? 0.5 : 1.0;

    /// <summary>
    /// Flips <see cref="IsVisible"/>. Bound to the eye-icon button in
    /// the Datasets list so the action behaves as a transient command
    /// (icon swaps in response to state change) rather than a
    /// persistent toggle button (which would render as accent-checked).
    /// </summary>
    public ICommand ToggleVisibilityCommand { get; }

    // ── Sub-layers ────────────────────────────────────────────────────
    //
    // Products that emit more than one Mapsui ILayer (S-111 colour
    // band + arrows; future S-101 fan-out) expose them here so the
    // user can toggle each one independently. Single-layer products
    // leave this collection empty and the UI hides the disclosure
    // triangle. The collection is populated and reconciled by
    // MapsuiMapSession and projected into these view models, so user toggles
    // survive palette switches and time-step scrubs.

    private readonly ObservableCollection<DatasetSubLayer> _subLayers = new();
    private readonly HashSet<DatasetSubLayer> _projectedSubLayers = [];
    public ObservableCollection<DatasetSubLayer> SubLayers => _subLayers;

    /// <summary>
    /// True when this dataset has more than one sub-layer and the
    /// disclosure UI should be shown.
    /// </summary>
    public bool HasSubLayers => _subLayers.Count > 1;

    private string? _info;
    public string? Info
    {
        get => _info;
        set => SetProperty(ref _info, value);
    }

    // ── Time-aware participation ──────────────────────────────────────
    //
    // Time-step navigation is global: per-entry prev/next/ComboBox
    // controls were replaced by a single timeline panel beneath the
    // map (TimelineView). Each entry exposes only:
    //   • the time samples the loader discovered for this dataset, and
    //   • the timestamp it is currently rendered at (a read-only label).

    private IReadOnlyList<DateTime>? _availableTimes;
    public IReadOnlyList<DateTime>? AvailableTimes
    {
        get => _mapDataset?.AvailableTimes ?? _availableTimes;
        set
        {
            if (!ReferenceEquals(_availableTimes, value))
            {
                _availableTimes = value;
                UpdateMapDataset();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTimeSteps));
            }
        }
    }

    /// <summary>True when this dataset has at least one time sample.</summary>
    public bool HasTimeSteps => AvailableTimes is { Count: > 0 };

    private DateTime? _currentTime;
    /// <summary>
    /// The timestamp this dataset is currently rendered at, or
    /// <c>null</c> when the dataset is not time-aware or has not yet
    /// been rendered. Set by <see cref="IDatasetLoaderService"/>.
    /// </summary>
    public DateTime? CurrentTime
    {
        get => _mapDataset?.CurrentTime ?? _currentTime;
        set
        {
            if (CurrentTime != value)
            {
                _currentTime = value;
                UpdateMapDataset();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentTimeLabel));
            }
        }
    }

    /// <summary>
    /// Display label for <see cref="CurrentTime"/>, or empty when no
    /// time has been assigned. Formatted via
    /// <see cref="Strings.DatasetEntry_CurrentTimeFormat"/>.
    /// </summary>
    public string CurrentTimeLabel =>
        CurrentTime is { } t
            ? string.Format(CultureInfo.CurrentCulture, Strings.DatasetEntry_CurrentTimeFormat, t)
            : string.Empty;

    // ── Validation report ────────────────────────────────────────────
    //
    // Surfaced in the Validation tab of the dataset properties panel.
    // Populated once per load by DatasetLoaderService after Render
    // succeeds. A null Validation means the spec has no rule pack yet
    // (S-101 / S-102 / S-104 / S-111 / S-201 / S-57); an empty Findings
    // collection on a non-null Validation means the rules ran and
    // found nothing.

    private ValidationReport? _validation;
    /// <summary>
    /// Aggregated validation findings for this dataset, or <c>null</c>
    /// when the spec has no rule pack defined. Set once at load time
    /// by <see cref="Services.DatasetLoaderService"/> via
    /// <see cref="SetValidationReport"/>.
    /// </summary>
    public ValidationReport? Validation => _mapDataset?.Validation ?? _validation;

    /// <summary><c>true</c> when a rule pack ran (regardless of finding count).</summary>
    public bool HasValidationRulePack => Validation is not null;

    /// <summary>
    /// Read-only display models for the report's findings, in the
    /// order the rules emitted them. Empty when no rule pack ran or
    /// the report contains no findings.
    /// </summary>
    public IReadOnlyList<ValidationFindingViewModel> Findings { get; private set; } =
        Array.Empty<ValidationFindingViewModel>();

    /// <summary>Total findings across all severities.</summary>
    public int ValidationFindingCount =>
        Validation?.Findings.Count > 0 ? Validation.Findings.Count : 0;

    /// <summary>Number of <see cref="ValidationSeverity.Error"/> findings.</summary>
    public int ValidationErrorCount =>
        Validation?.Findings.Count > 0
            ? Validation.Findings.Count(f => f.Severity == ValidationSeverity.Error)
            : 0;

    /// <summary>Number of <see cref="ValidationSeverity.Warning"/> findings.</summary>
    public int ValidationWarningCount =>
        Validation?.Findings.Count > 0
            ? Validation.Findings.Count(f => f.Severity == ValidationSeverity.Warning)
            : 0;

    /// <summary>Number of <see cref="ValidationSeverity.Info"/> findings.</summary>
    public int ValidationInfoCount =>
        Validation?.Findings.Count > 0
            ? Validation.Findings.Count(f => f.Severity == ValidationSeverity.Info)
            : 0;

    /// <summary><c>true</c> when the report contains at least one finding.</summary>
    public bool HasValidationFindings => ValidationFindingCount > 0;

    /// <summary>
    /// Drives the badge severity class: <c>true</c> when at least one
    /// Error finding exists. Wired to a <c>Classes.Error</c> binding on
    /// the badge Border so styling stays in XAML — no value converter.
    /// </summary>
    public bool BadgeIsError => ValidationErrorCount > 0;

    /// <summary><c>true</c> when there are warnings but no errors.</summary>
    public bool BadgeIsWarning => ValidationErrorCount == 0 && ValidationWarningCount > 0;

    /// <summary><c>true</c> when only info-severity findings are present.</summary>
    public bool BadgeIsInfo =>
        ValidationErrorCount == 0 && ValidationWarningCount == 0 && ValidationInfoCount > 0;

    /// <summary>
    /// Localised tooltip for the count badge, e.g.
    /// <c>"3 validation findings (1 errors, 2 warnings, 0 info)"</c>.
    /// </summary>
    public string ValidationBadgeTooltip => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Tooltip_ValidationBadge,
        ValidationFindingCount,
        ValidationErrorCount,
        ValidationWarningCount,
        ValidationInfoCount);

    /// <summary>
    /// Localised counts summary shown above the findings list when
    /// findings are present.
    /// </summary>
    public string ValidationCountsSummary => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Pane_Validation_CountsSummaryFormat,
        ValidationFindingCount,
        ValidationErrorCount,
        ValidationWarningCount,
        ValidationInfoCount);

    /// <summary>
    /// Localised message rendered when the Findings list is empty —
    /// either "No findings." (rule pack ran clean) or
    /// "Validation rules not yet defined for {spec}." (no rule pack).
    /// </summary>
    public string ValidationEmptyStateMessage =>
        HasValidationRulePack
            ? Strings.Pane_Validation_NoFindings
            : string.Format(CultureInfo.CurrentCulture, Strings.Pane_Validation_NoRulePack, ProductSpec);

    /// <summary>
    /// Replaces the cached validation report and raises change
    /// notifications for every derived property. Pass <c>null</c> to
    /// reset (e.g. on reload), pass <see cref="ValidationReport.Empty"/>
    /// or a report with findings to populate. Safe to call from any
    /// thread; consumers are responsible for marshalling to the UI
    /// thread when needed.
    /// </summary>
    /// <summary>
    /// Optional callback that zooms the live map to a finding's
    /// extent. Set by <see cref="DatasetsViewModel"/> once the entry
    /// is added to its <c>Entries</c> collection so finding
    /// view-models built by <see cref="SetValidationReport"/> can
    /// drive <see cref="Services.IMapViewportController.ZoomToExtent"/> through it.
    /// Stays <c>null</c> in tests that don't construct the
    /// view-model, in which case <see cref="ValidationFindingViewModel.ZoomToFindingCommand"/>
    /// is disabled.
    /// </summary>
    internal Action<Mapsui.MRect>? ZoomDispatcher { get; set; }

    public void SetValidationReport(ValidationReport? report)
    {
        _validation = report;
        UpdateMapDataset();
        Findings = report is null || report.Findings.Count == 0
            ? Array.Empty<ValidationFindingViewModel>()
            : report.Findings.Select(f => new ValidationFindingViewModel(f, ZoomDispatcher)).ToArray();

        OnPropertyChanged(nameof(Validation));
        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(HasValidationRulePack));
        OnPropertyChanged(nameof(ValidationFindingCount));
        OnPropertyChanged(nameof(ValidationErrorCount));
        OnPropertyChanged(nameof(ValidationWarningCount));
        OnPropertyChanged(nameof(ValidationInfoCount));
        OnPropertyChanged(nameof(HasValidationFindings));
        OnPropertyChanged(nameof(BadgeIsError));
        OnPropertyChanged(nameof(BadgeIsWarning));
        OnPropertyChanged(nameof(BadgeIsInfo));
        OnPropertyChanged(nameof(ValidationBadgeTooltip));
        OnPropertyChanged(nameof(ValidationCountsSummary));
        OnPropertyChanged(nameof(ValidationEmptyStateMessage));
    }

    public DatasetEntry(string filePath, string productSpec)
        : this(filePath, productSpec, source: null, relativePath: null, displayName: null)
    {
    }

    /// <summary>
    /// Creates a dataset entry whose bytes live inside
    /// <paramref name="source"/> at <paramref name="relativePath"/>.
    /// Used by <see cref="EncDotNet.S100.Viewer.Services.IExchangeSetService"/>
    /// for exchange-set ingestion.
    /// </summary>
    public DatasetEntry(
        string filePath,
        string productSpec,
        IAssetSource? source,
        string? relativePath,
        string? displayName,
        IReadOnlyList<string>? updateRelativePaths = null,
        int? minimumDisplayScale = null,
        int? maximumDisplayScale = null,
        ExchangeSets.BoundingBox? geographicBounds = null)
    {
        FilePath = filePath;
        ProductSpec = productSpec;
        Source = source;
        RelativePath = relativePath;
        UpdateRelativePaths = updateRelativePaths ?? Array.Empty<string>();
        MinimumDisplayScale = minimumDisplayScale;
        MaximumDisplayScale = maximumDisplayScale;
        GeographicBounds = geographicBounds;
        DisplayName = displayName ?? System.IO.Path.GetFileName(
            relativePath is { Length: > 0 } ? relativePath : filePath);
        _id = new MapDatasetId(
            filePath is { Length: > 0 }
                ? System.IO.Path.GetFileName(filePath)
                : DisplayName);
        UsageBand = Services.LazyLoading.CellUsageBand.TryParse(DisplayName)
            ?? Services.LazyLoading.CellUsageBand.TryParse(relativePath);
        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);

        _subLayers.CollectionChanged += (_, _) =>
        {
            ReconcileSubLayerProjectionSubscriptions();
            UpdateMapDataset();
            OnPropertyChanged(nameof(HasSubLayers));
        };
    }

    /// <summary>
    /// Establishes the renderer-neutral state that becomes authoritative for
    /// this loaded entry. Registration-only fields remain on the view-model.
    /// </summary>
    internal void SetLoadedState(DatasetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _mapDataset = CreateMapDataset(metadata);
        OnPropertyChanged(nameof(MapDataset));
    }

    private void UpdateMapDataset()
    {
        if (_mapDataset is null) return;
        _mapDataset = CreateMapDataset(_mapDataset.Metadata);
        OnPropertyChanged(nameof(MapDataset));
    }

    private MapDataset CreateMapDataset(DatasetMetadata metadata) => new(
        _id,
        DisplayName,
        metadata,
        _isVisible,
        _isActive,
        _opacity,
        _availableTimes,
        _currentTime,
        _subLayers.Select(subLayer => subLayer.State).ToArray(),
        _validation,
        _versionAssessment);

    private void ReconcileSubLayerProjectionSubscriptions()
    {
        foreach (var subLayer in _projectedSubLayers)
        {
            subLayer.PropertyChanged -= OnSubLayerProjectionChanged;
        }
        _projectedSubLayers.Clear();

        foreach (var subLayer in _subLayers)
        {
            subLayer.PropertyChanged += OnSubLayerProjectionChanged;
            _projectedSubLayers.Add(subLayer);
        }
    }

    private void OnSubLayerProjectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetSubLayer.State))
        {
            UpdateMapDataset();
        }
    }
}

/// <summary>
/// Represents one of the Mapsui layers a dataset is rendered as,
/// surfaced with a per-layer visibility toggle and opacity slider.
/// The combined effective state is computed by
/// <see cref="EncDotNet.S100.Renderers.Mapsui.MapsuiMapSession"/>:
///   <c>layer.Enabled = parent.IsVisible &amp;&amp; sub.IsVisible</c> and
///   <c>layer.Opacity = parent.Opacity * sub.Opacity</c>.
/// </summary>
internal sealed class DatasetSubLayer : ViewModelBase
{
    private MapDatasetSubLayer _state;
    private readonly string _displayName;

    /// <summary>Renderer-neutral state projected by this view-model.</summary>
    public MapDatasetSubLayer State => _state;

    /// <summary>
    /// Stable key supplied by the dataset processor (e.g.
    /// <c>"s111.arrows"</c>). Used to reconcile sub-layers across
    /// re-renders so a palette switch or time-scrub does not reset
    /// user-driven toggles.
    /// </summary>
    public string Key => _state.Key;

    public string DisplayName => _displayName;

    public bool IsVisible
    {
        get => _state.IsVisible;
        set
        {
            if (_state.IsVisible == value) return;
            _state = new MapDatasetSubLayer(Key, _state.Name, value, Opacity);
            OnPropertyChanged();
            OnPropertyChanged(nameof(State));
        }
    }

    public double Opacity
    {
        get => _state.Opacity;
        set
        {
            var clamped = value < 0 ? 0 : value > 1 ? 1 : value;
            if (_state.Opacity == clamped) return;
            _state = new MapDatasetSubLayer(Key, _state.Name, IsVisible, clamped);
            OnPropertyChanged();
            OnPropertyChanged(nameof(State));
        }
    }

    /// <summary>
    /// Flips <see cref="IsVisible"/>. See the rationale on
    /// <see cref="DatasetEntry.ToggleVisibilityCommand"/>.
    /// </summary>
    public ICommand ToggleVisibilityCommand { get; }

    public DatasetSubLayer(string key, string displayName)
    {
        _state = new MapDatasetSubLayer(key, key);
        _displayName = displayName;
        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);
    }
}

internal sealed class DatasetsViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;

    public BulkObservableCollection<DatasetEntry> Entries { get; } = new();

    /// <summary>
    /// Header rows surfaced above <see cref="Entries"/> in the Datasets
    /// panel — one per currently-loaded exchange set. Populated by
    /// <see cref="EncDotNet.S100.Viewer.Services.IExchangeSetService"/>
    /// via <see cref="RegisterExchangeSetHeader"/> and removed when the
    /// last entry from a set is gone.
    /// </summary>
    public ObservableCollection<ExchangeSetHeader> ExchangeSetHeaders { get; } = new();

    private DatasetEntry? _selectedDataset;
    /// <summary>
    /// The dataset row currently highlighted in the <b>Datasets</b> tab's
    /// flat list. Bound TwoWay to that list's selection. When the Datasets
    /// tab is active this drives the pinned inspector via
    /// <see cref="SelectedEntry"/>.
    /// </summary>
    public DatasetEntry? SelectedDataset
    {
        get => _selectedDataset;
        set
        {
            if (!ReferenceEquals(_selectedDataset, value))
            {
                _selectedDataset = value;
                OnPropertyChanged();
                RecomputeInspection();
            }
        }
    }

    private object? _selectedSourceNode;
    /// <summary>
    /// The node selected in the <b>Exchange sets</b> tab's source tree.
    /// Either an <see cref="ExchangeSetHeader"/> (a source) or a
    /// <see cref="DatasetEntry"/> (a nested dataset). Bound TwoWay to the
    /// tree's selection. When the Exchange sets tab is active this drives
    /// the pinned inspector.
    /// </summary>
    public object? SelectedSourceNode
    {
        get => _selectedSourceNode;
        set
        {
            if (!ReferenceEquals(_selectedSourceNode, value))
            {
                _selectedSourceNode = value;
                OnPropertyChanged();
                RecomputeInspection();
            }
        }
    }

    /// <summary>Tab index constant for the Exchange sets tab.</summary>
    public const int ExchangeSetsTabIndex = 0;

    /// <summary>Tab index constant for the Datasets tab.</summary>
    public const int DatasetsTabIndex = 1;

    private bool _suppressTabUserFlag;
    private bool _userSelectedTab;
    private int _activeTabIndex = ExchangeSetsTabIndex;
    /// <summary>
    /// Active tab in the panel: <see cref="ExchangeSetsTabIndex"/> (0) or
    /// <see cref="DatasetsTabIndex"/> (1). Bound TwoWay to the
    /// <c>TabControl.SelectedIndex</c>. A genuine user switch pins the
    /// choice so the conditional default (see
    /// <see cref="ApplyDefaultTab"/>) stops overriding it.
    /// </summary>
    public int ActiveTabIndex
    {
        get => _activeTabIndex;
        set
        {
            if (_activeTabIndex == value) return;
            _activeTabIndex = value;
            if (!_suppressTabUserFlag) _userSelectedTab = true;
            OnPropertyChanged();
            RecomputeInspection();
        }
    }

    private DatasetEntry? _inspectedDataset;
    private ExchangeSetHeader? _inspectedExchangeSet;

    /// <summary>
    /// The dataset reflected in the pinned inspector (DATASET / LAYERS /
    /// VALIDATION). Normally derived from the active tab's selection via
    /// <see cref="RecomputeInspection"/>, but also directly settable so
    /// hosts/tests can drive the inspector (and the on-map validation
    /// overlay that <see cref="Services.ValidationOverlayService"/> keys
    /// off it) without going through a selection control.
    /// </summary>
    public DatasetEntry? SelectedEntry
    {
        get => _inspectedDataset;
        set
        {
            if (!ReferenceEquals(_inspectedDataset, value))
            {
                _inspectedDataset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoInspectorSelection));
            }
        }
    }

    /// <summary>The exchange set reflected in the pinned inspector when a
    /// source node (not a dataset) is selected on the Exchange sets tab.</summary>
    public ExchangeSetHeader? InspectedExchangeSet => _inspectedExchangeSet;

    /// <summary>True when the inspector targets a dataset; controls
    /// visibility of the dataset inspector variant.</summary>
    public bool HasSelection => _inspectedDataset is not null;

    /// <summary>True when the inspector targets an exchange set; controls
    /// visibility of the exchange-set inspector variant.</summary>
    public bool HasExchangeSetSelection => _inspectedExchangeSet is not null;

    /// <summary>True when nothing is selected; shows the inspector's
    /// "select an item" placeholder.</summary>
    public bool HasNoInspectorSelection =>
        _inspectedDataset is null && _inspectedExchangeSet is null;

    /// <summary>True when at least one exchange set is loaded; lets the
    /// Exchange sets tab swap its empty-state for the source tree.</summary>
    public bool HasExchangeSets => ExchangeSetHeaders.Count > 0;

    private void RecomputeInspection()
    {
        DatasetEntry? dataset;
        ExchangeSetHeader? exchangeSet = null;

        if (_activeTabIndex == DatasetsTabIndex)
        {
            dataset = _selectedDataset;
        }
        else
        {
            switch (_selectedSourceNode)
            {
                case ExchangeSetHeader header:
                    exchangeSet = header;
                    dataset = null;
                    break;
                case DatasetEntry entry:
                    dataset = entry;
                    break;
                default:
                    dataset = null;
                    break;
            }
        }

        var datasetChanged = !ReferenceEquals(_inspectedDataset, dataset);
        var exchangeSetChanged = !ReferenceEquals(_inspectedExchangeSet, exchangeSet);
        if (!datasetChanged && !exchangeSetChanged) return;

        _inspectedDataset = dataset;
        _inspectedExchangeSet = exchangeSet;

        if (datasetChanged) OnPropertyChanged(nameof(SelectedEntry));
        if (exchangeSetChanged) OnPropertyChanged(nameof(InspectedExchangeSet));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasExchangeSetSelection));
        OnPropertyChanged(nameof(HasNoInspectorSelection));
    }

    /// <summary>
    /// Applies the conditional default tab: the Exchange sets tab unless
    /// only loose (non-exchange-set) datasets are loaded, in which case
    /// the Datasets tab. A genuine user tab switch pins the choice and
    /// suppresses this. The pin resets once the panel empties.
    /// </summary>
    private void ApplyDefaultTab()
    {
        if (IsEmpty)
        {
            _userSelectedTab = false;
        }

        if (_userSelectedTab) return;

        var desired = HasExchangeSets ? ExchangeSetsTabIndex : DatasetsTabIndex;
        if (_activeTabIndex != desired)
        {
            _suppressTabUserFlag = true;
            ActiveTabIndex = desired;
            _suppressTabUserFlag = false;
        }
    }

    /// <summary>
    /// True when no datasets are loaded. Drives the Datasets panel's
    /// empty-state placeholder and the inline "Open Dataset" prompt,
    /// both of which are hidden once at least one dataset is present.
    /// </summary>
    public bool IsEmpty => Entries.Count == 0;

    private Action<Mapsui.MRect>? _zoomDispatcher;
    /// <summary>
    /// Routes <see cref="ValidationFindingViewModel.ZoomToFindingCommand"/>
    /// activations from individual finding view-models to the live
    /// map's <see cref="Services.IMapViewportController.ZoomToExtent"/>. Set once by
    /// the window after the map host is available; assigned to every
    /// entry currently in <see cref="Entries"/> and to entries added
    /// later.
    /// </summary>
    public Action<Mapsui.MRect>? ZoomDispatcher
    {
        get => _zoomDispatcher;
        set
        {
            _zoomDispatcher = value;
            foreach (var entry in Entries)
            {
                entry.ZoomDispatcher = value;
            }
        }
    }

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Moves the supplied entry one slot up in the panel (toward index 0).</summary>
    public ICommand MoveUpCommand { get; }
    /// <summary>Moves the supplied entry one slot down in the panel.</summary>
    public ICommand MoveDownCommand { get; }
    /// <summary>
    /// Moves the supplied entry to the top of the list, which makes its
    /// layers paint last (above every other dataset).
    /// </summary>
    public ICommand BringToFrontCommand { get; }
    /// <summary>
    /// Moves the supplied entry to the bottom of the list, which makes
    /// its layers paint first (below every other dataset).
    /// </summary>
    public ICommand SendToBackCommand { get; }

    // ── Bulk actions ─────────────────────────────────────────────────
    /// <summary>Sets <see cref="DatasetEntry.IsVisible"/> to true on every entry.</summary>
    public ICommand ShowAllCommand { get; }
    /// <summary>Sets <see cref="DatasetEntry.IsVisible"/> to false on every entry.</summary>
    public ICommand HideAllCommand { get; }
    /// <summary>
    /// Hides every dataset except the supplied one. Surfaced from the
    /// per-row context menu so no list-selection state is required.
    /// </summary>
    public ICommand IsolateCommand { get; }
    /// <summary>
    /// Resets <see cref="DatasetEntry.Opacity"/> to 1.0 on every entry
    /// and on every <see cref="DatasetEntry.SubLayers"/> child.
    /// </summary>
    public ICommand ResetOpacityCommand { get; }

    /// <summary>
    /// Raised when <see cref="LoadFromPathAsync"/> rejects a file because
    /// no S-100 product specification recognised its extension. The window
    /// surfaces this as a status-bar message.
    /// </summary>
    public event Action<string>? UnrecognizedFileEncountered;

    public DatasetsViewModel(IDatasetLoaderService loader, GlobalTimeService? globalTime = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;

        AddCommand = new RelayCommand<string?>(_ => { });
        RemoveCommand = new RelayCommand<DatasetEntry>(Remove);
        MoveUpCommand = new RelayCommand<DatasetEntry>(MoveUp);
        MoveDownCommand = new RelayCommand<DatasetEntry>(MoveDown);
        BringToFrontCommand = new RelayCommand<DatasetEntry>(BringToFront);
        SendToBackCommand = new RelayCommand<DatasetEntry>(SendToBack);
        ShowAllCommand = new RelayCommand(ShowAll);
        HideAllCommand = new RelayCommand(HideAll);
        IsolateCommand = new RelayCommand<DatasetEntry>(Isolate);
        ResetOpacityCommand = new RelayCommand(ResetOpacity);

        // Re-apply paint order whenever the entries collection is
        // mutated by reorder commands. Add/remove side-effects flow
        // through the loader's Load/RemoveEntry path; we only need to
        // push order changes here. Coalescing per-event keeps Mapsui
        // mutations cheap (a single removeAll + insertAll round-trip
        // per move). See <see cref="DatasetLoaderService.SetEntryOrder"/>
        // for the host-side reorder logic.
        Entries.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(IsEmpty));

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
                _loader.SetEntryOrder(Entries.ToArray());

            if (e.NewItems is not null && _zoomDispatcher is not null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is DatasetEntry entry)
                        entry.ZoomDispatcher = _zoomDispatcher;
                }
            }

            RebuildExchangeSetGrouping();
            ApplyDefaultTab();

            // A removed dataset may have been the inspected one; drop the
            // selection so the inspector doesn't dangle on a gone entry.
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (ReferenceEquals(item, _selectedDataset)) SelectedDataset = null;
                    if (ReferenceEquals(item, _selectedSourceNode)) SelectedSourceNode = null;
                }
            }
        };

        ExchangeSetHeaders.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasExchangeSets));
            RebuildExchangeSetGrouping();
            ApplyDefaultTab();

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (ReferenceEquals(item, _selectedSourceNode)) SelectedSourceNode = null;
                }
            }
        };

        // Auto-unregister entries from the global time service when they
        // are removed from the collection.
        globalTime?.AttachTo(this);
    }

    public DatasetEntry Add(string filePath, string productSpec)
    {
        var entry = new DatasetEntry(filePath, productSpec);
        // Photoshop/QGIS convention: list index 0 is the top of the
        // paint stack (drawn last, on top of every other dataset). New
        // datasets are inserted at the top so they overlay existing
        // ones by default.
        Entries.Insert(0, entry);
        return entry;
    }

    /// <summary>
    /// Adds a dataset entry whose bytes live inside an exchange set
    /// (folder or ZIP) rather than at a plain filesystem path.
    /// <paramref name="source"/> must remain alive for as long as
    /// the returned entry is loaded; the caller (typically
    /// <see cref="IExchangeSetService"/>) is responsible for that
    /// lifetime.
    /// </summary>
    public DatasetEntry AddFromExchangeSet(
        IAssetSource source,
        string relativePath,
        string productSpec,
        string? displayName = null,
        IReadOnlyList<string>? updateRelativePaths = null,
        int? minimumDisplayScale = null,
        int? maximumDisplayScale = null,
        ExchangeSets.BoundingBox? geographicBounds = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        // FilePath is set to the relative path so logging and the
        // Properties panel have something useful to show even when
        // there is no real on-disk file (the entry is sourced from
        // a ZIP archive).
        var entry = new DatasetEntry(
            filePath: relativePath,
            productSpec: productSpec,
            source: source,
            relativePath: relativePath,
            displayName: displayName,
            updateRelativePaths: updateRelativePaths,
            minimumDisplayScale: minimumDisplayScale,
            maximumDisplayScale: maximumDisplayScale,
            geographicBounds: geographicBounds);
        Entries.Insert(0, entry);
        return entry;
    }

    /// <summary>
    /// Registers many exchange-set cells in a single batch, raising one
    /// collection-changed notification for the whole set rather than one per
    /// cell. Used by the lazy-loading path (issue #458) so opening a very large
    /// exchange set does not incur O(N²) rebuild work in the grouping / extent
    /// overlay subscribers and freeze the UI. Each entry is created deferred
    /// (bytes unloaded) with its catalogue footprint; the returned list is in
    /// the same order as <paramref name="registrations"/>.
    /// </summary>
    /// <param name="registrations">The per-cell registration descriptors.</param>
    /// <returns>The created (registered, not-yet-loaded) entries.</returns>
    public IReadOnlyList<DatasetEntry> AddRangeFromExchangeSet(
        IReadOnlyList<ExchangeSetCellRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var created = new List<DatasetEntry>(registrations.Count);
        foreach (var reg in registrations)
        {
            // Validate each registration up front for parity with
            // AddFromExchangeSet, so a caller that accidentally supplies a null
            // source or empty RelativePath/ProductSpec fails fast here rather
            // than creating a DatasetEntry in an invalid state that surfaces as
            // a harder-to-diagnose failure later. See issue #458.
            ArgumentNullException.ThrowIfNull(reg);
            ArgumentNullException.ThrowIfNull(reg.Source);
            ArgumentException.ThrowIfNullOrEmpty(reg.RelativePath);
            ArgumentException.ThrowIfNullOrEmpty(reg.ProductSpec);

            var entry = new DatasetEntry(
                filePath: reg.RelativePath,
                productSpec: reg.ProductSpec,
                source: reg.Source,
                relativePath: reg.RelativePath,
                displayName: reg.DisplayName,
                updateRelativePaths: reg.UpdateRelativePaths,
                minimumDisplayScale: reg.MinimumDisplayScale,
                maximumDisplayScale: reg.MaximumDisplayScale,
                geographicBounds: reg.GeographicBounds);

            // Mark the entry deferred *before* it is inserted (and therefore
            // before the extent-overlay / grouping subscribers attach to it).
            // The coordinator's later Register call sets IsDeferred = true again,
            // but because the value is unchanged that set is a no-op and raises
            // no PropertyChanged — avoiding an O(N²) rebuild storm when a very
            // large set (thousands of cells) is registered. See issue #458.
            entry.IsDeferred = true;

            // The bulk insert raises a Reset (no NewItems), so wire the zoom
            // dispatcher here rather than relying on the collection handler.
            if (_zoomDispatcher is not null)
                entry.ZoomDispatcher = _zoomDispatcher;

            created.Add(entry);
        }

        // Newest datasets sit at the top of the paint stack (index 0), matching
        // AddFromExchangeSet's single-item semantics.
        Entries.InsertRange(0, created);
        return created;
    }

    /// <summary>
    /// Registers a header for an opened exchange set. The
    /// supplied <paramref name="closeAction"/> is invoked when the user
    /// clicks the header's Close button and is responsible for removing
    /// every <see cref="DatasetEntry"/> that came from this set
    /// (typically by enumerating <see cref="Entries"/> with
    /// <c>e.Source == source</c> and removing them); the service's
    /// <c>OnEntriesChanged</c> listener will then dispose the set and
    /// remove this header via <see cref="RemoveExchangeSetHeader"/>.
    /// </summary>
    internal ExchangeSetHeader RegisterExchangeSetHeader(
        IAssetSource source,
        string sourcePath,
        string? producer,
        string? issueDate,
        int datasetCount,
        Action<ExchangeSetHeader> closeAction)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(closeAction);

        var header = new ExchangeSetHeader(
            source, sourcePath, producer, issueDate, datasetCount, closeAction);
        ExchangeSetHeaders.Add(header);
        return header;
    }

    /// <summary>Removes a header registered via
    /// <see cref="RegisterExchangeSetHeader"/>. Idempotent.</summary>
    internal void RemoveExchangeSetHeader(ExchangeSetHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        ExchangeSetHeaders.Remove(header);
    }

    /// <summary>
    /// Reconciles each <see cref="ExchangeSetHeader.Datasets"/> child
    /// collection so it contains exactly the <see cref="Entries"/> whose
    /// <see cref="DatasetEntry.Source"/> matches that header, in the same
    /// relative order they occupy in <see cref="Entries"/>. Loose entries
    /// (no backing source) are not nested anywhere — they appear only in
    /// the Datasets tab. Called whenever <see cref="Entries"/> or
    /// <see cref="ExchangeSetHeaders"/> change.
    /// </summary>
    private void RebuildExchangeSetGrouping()
    {
        foreach (var header in ExchangeSetHeaders)
        {
            var members = Entries.Where(e => ReferenceEquals(e.Source, header.Source)).ToList();

            // Fast path: already in sync (same items, same order).
            if (header.Datasets.Count == members.Count)
            {
                var same = true;
                for (var i = 0; i < members.Count; i++)
                {
                    if (!ReferenceEquals(header.Datasets[i], members[i])) { same = false; break; }
                }
                if (same) continue;
            }

            header.Datasets.Clear();
            foreach (var member in members)
            {
                header.Datasets.Add(member);
            }
        }
    }

    /// <summary>
    /// Loads the supplied entry through the dataset loader. Fire-and-forget;
    /// errors are surfaced via the toast notification service.
    /// </summary>
    public void RequestLoad(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _ = _loader.LoadAsync(entry);
    }

    /// <summary>
    /// Loads the supplied entry through the dataset loader and returns the
    /// load task so callers (e.g. the exchange-set open flow) can await the
    /// completion of a batch of dispatched loads before declaring success.
    /// Like <see cref="RequestLoad"/>, the loader surfaces any per-entry
    /// failure via its own notification and does not throw, so the returned
    /// task completes successfully even when the underlying load fails.
    /// </summary>
    /// <param name="entry">The dataset entry to load.</param>
    /// <returns>A task that completes when the entry has finished loading.</returns>
    public Task RequestLoadAsync(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _loader.LoadAsync(entry);
    }

    /// <summary>
    /// "Reveals" a dataset in response to an explicit user gesture
    /// (double-clicking its row): ensures the dataset is loaded, then frames
    /// the map on its extent via <see cref="ZoomDispatcher"/>. Unlike a plain
    /// <see cref="RequestLoad"/>, this also re-centres an <em>already-loaded</em>
    /// dataset — including exchange-set members, which opt out of the loader's
    /// per-dataset auto-zoom — so the user can jump to a far-away cell that has
    /// zoomed out of view. Load failures are surfaced by the loader's own
    /// notifications rather than thrown to the caller; callers may await the
    /// returned task to observe completion. No-op framing when the dataset
    /// produced no geometry (e.g. an out-of-range time-gated entry). See issue #446.
    /// </summary>
    /// <param name="entry">The dataset entry to reveal.</param>
    /// <returns>A task that completes once the reveal (load + zoom) is done.</returns>
    public async Task RevealDatasetAsync(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.IsLoaded)
            await RequestLoadAsync(entry).ConfigureAwait(true);

        if (entry.MercatorExtent is { } extent)
            _zoomDispatcher?.Invoke(extent);
    }

    /// <summary>
    /// Detects the product spec for <paramref name="path"/>, adds an entry,
    /// and asks the loader to render it. If the file extension is not
    /// recognised, raises <see cref="UnrecognizedFileEncountered"/> with the
    /// extension and returns <c>null</c>.
    /// </summary>
    public async Task<DatasetEntry?> LoadFromPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var spec = Datasets.Pipelines.DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null)
        {
            UnrecognizedFileEncountered?.Invoke(System.IO.Path.GetExtension(path));
            return null;
        }

        var entry = Add(path, spec);
        await _loader.LoadAsync(entry);
        return entry;
    }

    private void Remove(DatasetEntry? entry)
    {
        if (entry is null) return;
        Entries.Remove(entry);
    }

    private void MoveUp(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i > 0) Entries.Move(i, i - 1);
    }

    private void MoveDown(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i >= 0 && i < Entries.Count - 1) Entries.Move(i, i + 1);
    }

    private void BringToFront(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i >= 0 && i != 0) Entries.Move(i, 0);
    }

    private void SendToBack(DatasetEntry? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        var last = Entries.Count - 1;
        if (i >= 0 && i != last) Entries.Move(i, last);
    }

    private void ShowAll()
    {
        foreach (var e in Entries) e.IsVisible = true;
    }

    private void HideAll()
    {
        foreach (var e in Entries) e.IsVisible = false;
    }

    private void Isolate(DatasetEntry? entry)
    {
        if (entry is null) return;
        foreach (var e in Entries) e.IsVisible = ReferenceEquals(e, entry);
    }

    private void ResetOpacity()
    {
        foreach (var e in Entries)
        {
            e.Opacity = 1.0;
            foreach (var sub in e.SubLayers) sub.Opacity = 1.0;
        }
    }
}
