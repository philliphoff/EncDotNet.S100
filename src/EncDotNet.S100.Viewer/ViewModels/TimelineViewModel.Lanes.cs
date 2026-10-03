using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Timeline's lanes and its In map view filter (#710, handoff Part E):
/// one lane per loaded time-aware layer, grouped by product, on the shared
/// axis; with the filter on only the layers in the map view are listed and
/// set the axis, the rest fold into one row.
/// </summary>
internal sealed partial class TimelineViewModel
{
    /// <summary>Above this many lanes the In map view filter starts on (handoff E4).</summary>
    internal const int InMapViewDefaultThreshold = 6;

    private static readonly Dictionary<string, (string Title, Color Color, int Order)> Products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-111"] = (Strings.TimelinePanel_ProductS111, Color.Parse("#2563EB"), 0),
        ["S-104"] = (Strings.TimelinePanel_ProductS104, Color.Parse("#0E7490"), 1),
        ["S-411"] = (Strings.TimelinePanel_ProductS411, Color.Parse("#7C3AED"), 2),
        ["S-412"] = (Strings.TimelinePanel_ProductS412, Color.Parse("#C2410C"), 3),
        ["S-413"] = (Strings.TimelinePanel_ProductS413, Color.Parse("#4D7C0F"), 4),
    };

    private static readonly Color OtherProductColor = Color.Parse("#52525B");

    private ITimelineMapScope? _scope;
    private bool? _inMapViewChoice;
    private bool _collapsedToStrip;
    private bool _outsideExpanded;
    private string _laneStructure = string.Empty;
    private string _inViewKey = string.Empty;
    private readonly Dictionary<string, TimelineLaneViewModel> _lanes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TimelineLaneViewModel> _outsideLanes = [];
    private IReadOnlyList<NormalizedCoverageBand> _outsideBands = [];
    private TimelineLaneViewModel? _hoveredLane;

    private void InitializeLanes(ITimelineMapScope? scope, ILibraryTimeSource? library)
    {
        _scope = scope;
        InitializeLibrary(library);
        ToggleInMapViewCommand = new RelayCommand(() => IsInMapView = !IsInMapView, () => IsInMapViewAvailable);
        ToggleStripCommand = new RelayCommand(() => IsCollapsedToStrip = !IsCollapsedToStrip);
        ToggleOutsideCommand = new RelayCommand(() => IsOutsideExpanded = !IsOutsideExpanded);
        if (_scope is not null)
            _scope.Changed += OnMapScopeChanged;
    }

    /// <summary>The lanes in scope, by product.</summary>
    public ObservableCollection<TimelineLaneGroupViewModel> LaneGroups { get; } = [];

    /// <summary>The lanes outside the map view, folded into one row while the filter is on.</summary>
    public IReadOnlyList<TimelineLaneViewModel> OutsideLanes => _outsideLanes;

    /// <summary>True when some lanes lie outside the map view and are folded.</summary>
    public bool HasOutsideLanes => IsFiltering && _outsideLanes.Count > 0;

    /// <summary>"12 more outside the map view".</summary>
    public string OutsideLabel => string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_OutsideMapViewFormat, _outsideLanes.Count);

    /// <summary>The folded lanes' data, as one grey band.</summary>
    public IReadOnlyList<NormalizedCoverageBand> OutsideBands => _outsideBands;

    /// <summary>True when the folded lanes are listed under their row.</summary>
    public bool IsOutsideExpanded
    {
        get => _outsideExpanded;
        set => SetProperty(ref _outsideExpanded, value);
    }

    /// <summary>Lists or folds the lanes outside the map view.</summary>
    public ICommand ToggleOutsideCommand { get; private set; } = null!;

    /// <summary>True when the dock shows the strip only (handoff E6; remembered).</summary>
    public bool IsCollapsedToStrip
    {
        get => _collapsedToStrip;
        set
        {
            if (!SetProperty(ref _collapsedToStrip, value))
                return;
            OnPropertyChanged(nameof(ShowLanes));
            OnPropertyChanged(nameof(StripToggleLabel));
        }
    }

    /// <summary>True when the lanes are shown under the summary row.</summary>
    public bool ShowLanes => IsActive && !IsCollapsedToStrip;

    /// <summary>The dock header's toggle: "Collapse to strip" or "Show lanes".</summary>
    public string StripToggleLabel => IsCollapsedToStrip ? Strings.TimelinePanel_ShowLanes : Strings.TimelinePanel_CollapseToStrip;

    /// <summary>Switches between lanes and the strip.</summary>
    public ICommand ToggleStripCommand { get; private set; } = null!;

    /// <summary>True when the map can say which layers are in view.</summary>
    public bool IsInMapViewAvailable => _scope is not null;

    /// <summary>
    /// The In map view filter: the user's choice, else on when there are more
    /// than <see cref="InMapViewDefaultThreshold"/> lanes (handoff E4).
    /// </summary>
    public bool IsInMapView
    {
        get => IsInMapViewAvailable && (_inMapViewChoice ?? LaneCount > InMapViewDefaultThreshold);
        set
        {
            if (!IsInMapViewAvailable || IsInMapView == value && _inMapViewChoice is not null)
                return;
            _inMapViewChoice = value;
            OnPropertyChanged();
            RebuildAxis();
            RaiseSteps();
            RaiseNow();
        }
    }

    /// <summary>Turns the In map view filter on or off.</summary>
    public ICommand ToggleInMapViewCommand { get; private set; } = null!;

    /// <summary>True when the filter is on and the map view is known.</summary>
    private bool IsFiltering => IsInMapView;

    /// <summary>The summary row's label: "In map view" or "All loaded".</summary>
    public string SummaryLabel => IsFiltering ? Strings.TimelinePanel_InMapView : Strings.TimelinePanel_PresetAllLoaded;

    /// <summary>"2 of 14 layers in map view", or null while the filter is off.</summary>
    private string? InMapViewCount =>
        IsFiltering
            ? string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_InMapViewCountFormat, ScopedDatasets.Count, _service.TimedDatasets.Count)
            : null;

    /// <summary>The lane key of a dataset: its session id, else its name.</summary>
    private static string LaneKey(MapsuiMapTimedDataset dataset) => dataset.DatasetId ?? dataset.Name;

    /// <summary>True unless the map says the dataset lies outside the view.</summary>
    private bool IsInView(MapsuiMapTimedDataset dataset) =>
        _scope is null || dataset.DatasetId is not { } id || _scope.IsInMapView(id) != false;

    /// <summary>The layers the filter keeps: those in the map view, or every layer while it is off.</summary>
    internal IReadOnlyList<MapsuiMapTimedDataset> ScopedDatasets =>
        IsFiltering ? [.. _service.TimedDatasets.Where(IsInView)] : _service.TimedDatasets;

    /// <summary>
    /// The layers that set the axis: those the filter keeps, or every layer
    /// when none is in the map view (an empty axis helps nobody).
    /// </summary>
    private IReadOnlyList<MapsuiMapTimedDataset> AxisDatasets
    {
        get
        {
            var scoped = ScopedDatasets;
            return scoped.Count > 0 ? scoped : _service.TimedDatasets;
        }
    }

    /// <summary>True when the axis follows a subset of the layers.</summary>
    private bool IsAxisScoped => IsFiltering && AxisDatasets.Count < _service.TimedDatasets.Count;

    /// <summary>
    /// The range of the layers that set the axis, widened by the Library data
    /// shown with them (#711).
    /// </summary>
    private (DateTime Min, DateTime Max)? AxisRange
    {
        get
        {
            (DateTime Min, DateTime Max)? range;
            if (!IsAxisScoped)
            {
                range = _service.MinTime is { } min && _service.MaxTime is { } max ? (min, max) : null;
            }
            else
            {
                var datasets = AxisDatasets;
                range = (datasets.Min(d => d.First), datasets.Max(d => d.Last));
            }
            if (range is not { } loaded)
                return null;
            foreach (var window in KnownWindows())
                loaded = (window.Start < loaded.Min ? window.Start : loaded.Min, window.End > loaded.Max ? window.End : loaded.Max);
            return loaded;
        }
    }

    /// <summary>The coverage that lays out the axis: the layers in scope and the Library data shown, merged.</summary>
    private IReadOnlyList<CoverageSegment> AxisCoverage
    {
        get
        {
            var loaded = IsAxisScoped ? Merge(AxisDatasets.SelectMany(Segments)) : _service.CoverageSegments;
            var known = KnownWindows().ToArray();
            return known.Length == 0 ? loaded : Merge(loaded.Concat(known));
        }
    }

    /// <summary>The windows in which a dataset draws.</summary>
    private static IEnumerable<CoverageSegment> Segments(MapsuiMapTimedDataset dataset) =>
        dataset.Coverage.Count > 0
            ? dataset.Coverage.Select(c => new CoverageSegment(c.Start, c.End))
            : [new CoverageSegment(dataset.First, dataset.Last)];

    /// <summary>Sorts and merges overlapping segments.</summary>
    private static CoverageSegment[] Merge(IEnumerable<CoverageSegment> segments)
    {
        var merged = new List<CoverageSegment>();
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && segment.Start <= merged[^1].End)
            {
                if (segment.End > merged[^1].End)
                    merged[^1] = merged[^1] with { End = segment.End };
            }
            else
            {
                merged.Add(segment);
            }
        }
        return [.. merged];
    }

    /// <summary>
    /// The map view moved (or a footprint became known): re-filter, without
    /// moving the view time (scenario 5), when the set of layers in view changed.
    /// </summary>
    private void OnMapScopeChanged()
    {
        var key = string.Join('|', _service.TimedDatasets.Where(IsInView).Select(LaneKey))
            + "#" + (ShowOnline ? string.Join('|', LibraryEntries.Where(IsInView).Select(e => e.ItemId)) : string.Empty);
        if (key == _inViewKey)
            return;
        _inViewKey = key;
        if (IsFiltering)
        {
            RebuildAxis();
            RaiseSteps();
            RaiseNow();
        }
        else
        {
            RebuildLanes();
        }
    }

    /// <summary>Highlights the hovered lane's footprint on the map (handoff E4); null clears it.</summary>
    internal void HoverLane(TimelineLaneViewModel? lane)
    {
        if (ReferenceEquals(lane, _hoveredLane))
            return;
        _hoveredLane = lane;
        HighlightLane(lane);
    }

    /// <summary>
    /// Rebuilds the lanes from the loaded layers and the axis: their groups
    /// when the set of lanes changed, and every lane's bands.
    /// </summary>
    private void RebuildLanes()
    {
        var datasets = _service.TimedDatasets;
        var axis = _axis;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lanes = new List<(TimelineLaneViewModel Lane, bool InScope)>();
        var filtering = IsFiltering;
        foreach (var dataset in datasets
            .OrderBy(d => Product(d.ProductSpec).Order)
            .ThenBy(d => d.ProductSpec, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var key = LaneKey(dataset);
            if (!seen.Add(key))
                continue;
            if (!_lanes.TryGetValue(key, out var lane))
            {
                lane = new TimelineLaneViewModel(key, dataset, dataset.ProductSpec ?? string.Empty, Product(dataset.ProductSpec).Color, _service.SetCurrentTime, _library);
                _lanes[key] = lane;
            }
            lane.Dataset = dataset;
            lane.IsInMapView = _scope is not null && dataset.DatasetId is { } id ? _scope.IsInMapView(id) : null;
            lane.Bands = axis is null ? [] : axis.BandsFor(Segments(dataset));
            lane.Gaps = _gaps;
            lanes.Add((lane, !filtering || IsInView(dataset)));
        }

        // Library data: linked to the loaded lanes, or lanes of their own (#711).
        LinkLibrary([.. lanes.Select(l => l.Lane)], lanes, seen);
        var ordered = lanes
            .OrderBy(l => Product(l.Lane.Spec).Order)
            .ThenBy(l => l.Lane.Spec, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Lane.IsKnown)
            .ToArray();
        var inScope = ordered.Where(l => l.InScope).Select(l => l.Lane).ToList();
        var outside = ordered.Where(l => !l.InScope).Select(l => l.Lane).ToList();
        foreach (var stale in _lanes.Keys.Where(k => !seen.Contains(k)).ToArray())
            _lanes.Remove(stale);

        var structure = string.Join('|', inScope.Select(l => l.Key)) + "#" + string.Join('|', outside.Select(l => l.Key));
        if (structure != _laneStructure)
        {
            _laneStructure = structure;
            LaneGroups.Clear();
            foreach (var group in inScope.GroupBy(l => l.Spec, StringComparer.OrdinalIgnoreCase))
            {
                LaneGroups.Add(new TimelineLaneGroupViewModel(
                    group.Key,
                    GroupTitle(group.Key),
                    [.. group],
                    !_collapsedGroups.Contains(group.Key),
                    (key, expanded) =>
                    {
                        if (expanded)
                            _collapsedGroups.Remove(key);
                        else
                            _collapsedGroups.Add(key);
                    }));
            }
            _outsideLanes = outside;
        }
        _outsideBands = axis is null || outside.Count == 0
            ? []
            : axis.BandsFor(Merge(outside.SelectMany(LaneSegments)));

        UpdateLaneTimes();
        UpdateLaneProgress();
        OnPropertyChanged(nameof(OutsideLanes));
        OnPropertyChanged(nameof(HasOutsideLanes));
        OnPropertyChanged(nameof(OutsideLabel));
        OnPropertyChanged(nameof(OutsideBands));
        OnPropertyChanged(nameof(IsInMapView));
        OnPropertyChanged(nameof(SummaryLabel));
        OnPropertyChanged(nameof(ShowLanes));
    }

    /// <summary>Refreshes every lane's label, layer time and Expired tag.</summary>
    private void UpdateLaneTimes()
    {
        if (_lanes.Count == 0)
            return;
        var format = ActiveFormat;
        var zone = Zone;
        var now = Now;
        var view = _service.CurrentTime;
        foreach (var lane in _lanes.Values)
        {
            if (lane.Dataset is not { } dataset)
            {
                UpdateKnownLane(lane, format);
                UpdateLaneActions(lane, format);
                continue;
            }
            UpdateLaneActions(lane, format);
            var run = ForecastRunNames.RunOf(dataset, LibraryEntries);
            var tiled = ForecastRunNames.ModelAndTile(dataset.Name);
            lane.Code = DriverLabel(dataset);
            var parts = new List<string>(3);
            if (tiled is { } tile)
                parts.Add(tile.Tile);
            if (run is { FromIssueTime: false } known)
                parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_LaneRunFormat, ForecastRunNames.FormatRun(known.Time, format, zone, now)));
            if (TimelineStepper.Cadence(dataset.Samples) is { } cadence && cadence > TimeSpan.Zero)
                parts.Add(Duration(cadence));
            lane.Sub = string.Join(" · ", parts);
            lane.IsExpired = IsForecastDataset(dataset) && now > dataset.Last;

            var time = view is { } at
                ? LayerTimes.Describe(dataset, at, dataset.DatasetId is { } id && _progress?.IsDatasetDrawing(id) == true, format, zone, run)
                : null;
            lane.LayerTime = time?.Text ?? string.Empty;
            lane.IsHidden = time?.IsHidden == true;
            lane.Nearest = time?.IsHidden == true ? time.Nearest : null;
            lane.Tooltip = string.Format(
                CultureInfo.CurrentCulture,
                Strings.TimelinePanel_LaneTooltipFormat,
                dataset.Name,
                dataset.ProductSpec ?? string.Empty,
                TimeFormatting.Format(dataset.First, format),
                TimeFormatting.Format(dataset.Last, format))
                + (lane.LayerTime.Length > 0 ? "\n" + lane.LayerTime : string.Empty);
        }
    }

    private static (string Title, Color Color, int Order) Product(string? spec) =>
        spec is not null && Products.TryGetValue(spec, out var product)
            ? product
            : (string.Empty, OtherProductColor, int.MaxValue);

    /// <summary>"S-111 Surface currents", or the specification alone.</summary>
    internal static string GroupTitle(string spec) =>
        Product(spec).Title is { Length: > 0 } title ? $"{spec} {title}" : spec;

    /// <summary>The window spanned by the layers in the map view, for the In view preset.</summary>
    private (DateTime, DateTime)? InViewWindow()
    {
        if (_scope is null)
            return null;
        var inView = _service.TimedDatasets.Where(d => d.DatasetId is { } id && _scope.IsInMapView(id) == true).ToArray();
        if (inView.Length == 0)
            return null;
        var first = inView.Min(d => d.First);
        var last = inView.Max(d => d.Last);
        return last > first ? (first, last) : (first.AddHours(-1), last.AddHours(1));
    }
}
