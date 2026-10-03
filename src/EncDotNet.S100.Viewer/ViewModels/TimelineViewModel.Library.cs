using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// What the Library knows on the Timeline (#711, handoff E2, E5, G1, G3):
/// windows online (dashed) and on disk (outlined) on the lane of the data
/// they belong to, or on a lane of their own; New run and Expired tags as in
/// the Library; and a band's Get / Load / Reveal in Library.
/// </summary>
internal sealed partial class TimelineViewModel
{
    private ILibraryTimeSource? _library;
    private bool _showOnline = true;

    private void InitializeLibrary(ILibraryTimeSource? library)
    {
        _library = library;
        ToggleShowOnlineCommand = new RelayCommand(() => ShowOnline = !ShowOnline, () => IsShowOnlineAvailable);
        if (_library is null)
            return;
        _library.Changed += () =>
        {
            RebuildAxis();
            RaiseSteps();
        };
        _library.ProgressChanged += UpdateLaneProgress;
    }

    /// <summary>True when the Library can say what it knows.</summary>
    public bool IsShowOnlineAvailable => _library is not null;

    /// <summary>
    /// "Show online": draw what the Library knows but has not loaded, online
    /// and on disk (handoff E2). On by default.
    /// </summary>
    public bool ShowOnline
    {
        get => IsShowOnlineAvailable && _showOnline;
        set
        {
            if (!IsShowOnlineAvailable || _showOnline == value)
                return;
            _showOnline = value;
            OnPropertyChanged();
            RebuildAxis();
            RaiseSteps();
        }
    }

    /// <summary>Turns Show online on or off.</summary>
    public ICommand ToggleShowOnlineCommand { get; private set; } = null!;

    /// <summary>Every Library window, for linking lanes (shown or not).</summary>
    private IReadOnlyList<LibraryTimedEntry> LibraryEntries => _library?.Entries ?? [];

    /// <summary>True unless the map says the entry's footprint lies outside the view.</summary>
    private bool IsInView(LibraryTimedEntry entry) =>
        _scope is null || entry.Bounds is not { } bounds || _scope.Intersects(bounds) != false;

    /// <summary>
    /// The windows of Library data drawn on the Timeline (online or on disk,
    /// not loaded), within the filter: they widen the axis like loaded data.
    /// </summary>
    private IEnumerable<CoverageSegment> KnownWindows() =>
        ShowOnline
            ? LibraryEntries
                .Where(e => e.State != LibraryTimedState.Loaded && (!IsFiltering || IsInView(e)))
                .Select(e => new CoverageSegment(e.Start, e.End))
            : [];

    /// <summary>
    /// Builds the lanes of Library data no loaded layer stands for, one per
    /// model (or dataset), and links the rest to the loaded lanes they match.
    /// </summary>
    private void LinkLibrary(IReadOnlyList<TimelineLaneViewModel> loaded, List<(TimelineLaneViewModel Lane, bool InScope)> lanes, HashSet<string> seen)
    {
        var byKey = new Dictionary<string, TimelineLaneViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var lane in loaded)
            byKey.TryAdd(LibraryTimedEntry.KeyOf(lane.Dataset!.Name), lane);

        var linked = loaded.ToDictionary(l => l, _ => new List<LibraryTimedEntry>());
        var known = new Dictionary<string, List<LibraryTimedEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LibraryEntries)
        {
            if (byKey.TryGetValue(entry.MatchKey, out var lane))
                linked[lane].Add(entry);
            else if (ShowOnline && entry.State != LibraryTimedState.Loaded)
            {
                var key = $"library:{entry.Spec}/{entry.Model ?? entry.MatchKey}";
                if (!known.TryGetValue(key, out var list))
                    known[key] = list = [];
                list.Add(entry);
            }
        }

        foreach (var (lane, entries) in linked)
        {
            lane.Entries = entries;
            lane.IsNewRun = entries.Any(e => e.IsNewRun);
            SetKnownBands(lane, ShowOnline ? entries.Where(e => e.State != LibraryTimedState.Loaded) : []);
        }

        var filtering = IsFiltering;
        foreach (var (key, entries) in known)
        {
            seen.Add(key);
            var spec = entries[0].Spec;
            if (!_lanes.TryGetValue(key, out var lane))
            {
                lane = new TimelineLaneViewModel(key, null, spec, Product(spec).Color, _service.SetCurrentTime, _library);
                _lanes[key] = lane;
            }
            var inView = filtering ? entries.Where(IsInView).ToList() : entries;
            var shown = inView.Count > 0 ? inView : entries;
            lane.Entries = shown;
            lane.IsNewRun = false;
            SetKnownBands(lane, shown);
            lane.Bands = [];
            lane.Gaps = _gaps;
            lanes.Add((lane, !filtering || inView.Count > 0));
        }
    }

    private void SetKnownBands(TimelineLaneViewModel lane, IEnumerable<LibraryTimedEntry> entries)
    {
        var axis = _axis;
        var list = entries as IReadOnlyCollection<LibraryTimedEntry> ?? [.. entries];
        if (axis is null || list.Count == 0)
        {
            lane.OnlineBands = [];
            lane.OnDiskBands = [];
            return;
        }
        lane.OnlineBands = axis.BandsFor(Merge(list.Where(e => e.State == LibraryTimedState.Online).Select(e => new CoverageSegment(e.Start, e.End))));
        lane.OnDiskBands = axis.BandsFor(Merge(list.Where(e => e.State == LibraryTimedState.OnDisk).Select(e => new CoverageSegment(e.Start, e.End))));
    }

    /// <summary>The windows a lane draws: its loaded data's, or its Library windows'.</summary>
    private static IEnumerable<CoverageSegment> LaneSegments(TimelineLaneViewModel lane) =>
        lane.Dataset is { } dataset
            ? Segments(dataset)
            : lane.Entries.Select(e => new CoverageSegment(e.Start, e.End));

    /// <summary>A Library lane's labels: "cbofs", "online · 24 tiles · 13 MB", its run.</summary>
    private void UpdateKnownLane(TimelineLaneViewModel lane, TimeFormat format)
    {
        var entries = lane.Entries;
        var first = entries[0];
        var culture = CultureInfo.CurrentCulture;
        lane.Code = first.Model ?? ForecastRunNames.ModelAndTile(first.Name)?.Model ?? first.Name;
        var parts = new List<string>(3)
        {
            entries.Any(e => e.State == LibraryTimedState.Online) ? Strings.TimelinePanel_LaneOnline : Strings.TimelinePanel_LaneOnDisk,
        };
        var tiles = entries.Select(e => e.ItemId).Distinct(StringComparer.Ordinal).Count();
        if (tiles > 1)
            parts.Add(string.Format(culture, Strings.TimelinePanel_LaneTilesFormat, tiles));
        var bytes = entries.Where(e => e.State == LibraryTimedState.Online).Sum(e => e.SizeBytes ?? 0);
        if (bytes > 0)
            parts.Add(LibraryItemViewModel.FormatBytes(bytes));
        lane.Sub = string.Join(" · ", parts);
        lane.IsExpired = entries.All(e => e.IsExpired);
        lane.IsHidden = false;
        lane.Nearest = null;
        var run = entries.Max(e => e.Run);
        lane.LayerTime = run is { } r ? string.Format(culture, Strings.TimelinePanel_BandRunFormat, Short(r, format, near: true)) : string.Empty;
        lane.Tooltip = string.Join("\n", entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Take(8)
            .Append($"{first.Spec} · {Short(entries.Min(e => e.Start), format)} → {Short(entries.Max(e => e.End), format)}"));
    }

    /// <summary>
    /// "18:00Z" (or the local short time) as the layer times read, with the
    /// day ("01.10 18:00Z") unless <paramref name="near"/> and within a day of now.
    /// </summary>
    private string Short(DateTime utc, TimeFormat format, bool near = false)
    {
        var culture = CultureInfo.CurrentCulture;
        var clock = LayerTimes.Clock(utc, format, Zone, culture);
        return near && (utc - Now).Duration() <= LayerTimes.DateThreshold
            ? clock
            : $"{LayerTimes.Day(utc, Zone, culture)} {clock}";
    }

    /// <summary>The band popover's title and details (handoff E5).</summary>
    private void UpdateLaneActions(TimelineLaneViewModel lane, TimeFormat format)
    {
        var actionable = lane.Entries.Where(e => e.State != LibraryTimedState.Loaded).ToArray();
        var entries = actionable.Length > 0 ? actionable : [.. lane.Entries];
        if (entries.Length == 0)
        {
            lane.ActionTitle = string.Empty;
            lane.ActionDetail = string.Empty;
            return;
        }
        var culture = CultureInfo.CurrentCulture;
        var run = entries.Max(e => e.Run);
        lane.ActionTitle = run is { } r
            ? $"{lane.Code} · {string.Format(culture, Strings.TimelinePanel_BandRunFormat, Short(r, format, near: true))}"
            : lane.Code;
        var details = new List<string>(3)
        {
            string.Format(culture, Strings.TimelinePanel_BandWindowFormat, Short(entries.Min(e => e.Start), format), Short(entries.Max(e => e.End), format)),
        };
        var tiles = entries.Select(e => e.ItemId).Distinct(StringComparer.Ordinal).Count();
        if (tiles > 1)
            details.Add(string.Format(culture, Strings.TimelinePanel_LaneTilesFormat, tiles));
        var bytes = entries.Where(e => e.State == LibraryTimedState.Online).Sum(e => e.SizeBytes ?? 0);
        if (bytes > 0)
            details.Add(LibraryItemViewModel.FormatBytes(bytes));
        lane.ActionDetail = string.Join(" · ", details);
    }

    /// <summary>Fills the online band of each lane whose data downloads (handoff E5).</summary>
    private void UpdateLaneProgress()
    {
        if (_library is null)
            return;
        foreach (var lane in _lanes.Values)
        {
            var fractions = lane.Entries
                .Where(e => e.State == LibraryTimedState.Online)
                .Select(_library.ProgressOf)
                .OfType<double>()
                .ToArray();
            lane.Progress = fractions.Length > 0 ? fractions.Average() : double.NaN;
        }
    }

    /// <summary>The footprints to outline for a lane: its dataset's, or its Library data's.</summary>
    private void HighlightLane(TimelineLaneViewModel? lane)
    {
        if (_scope is null)
            return;
        var color = lane is null ? default : (lane.Color.R, lane.Color.G, lane.Color.B);
        if (lane is { IsKnown: true })
            _scope.HighlightAreas([.. lane.Entries.Select(e => e.Bounds).OfType<GeoBounds>()], color);
        else
            _scope.Highlight(lane?.DatasetId, color);
    }
}
