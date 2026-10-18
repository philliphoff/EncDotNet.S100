using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tools;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A group in the Sources disclosure (D6).</summary>
/// <param name="Title">The group heading.</param>
/// <param name="Items">The lines in the group.</param>
internal sealed record ClearanceSourceGroup(string Title, IReadOnlyList<string> Items);

/// <summary>
/// The Clearance tab in the bottom dock (#902, handoff D1–D4 and D6): the
/// clearance profile along the checked route, its readout at the cursor and
/// the Sources disclosure, projected from <see cref="PassageCheckService"/>.
/// The cursor is the service's, so the profile, the leg rows and the map
/// ghost stay linked. Under the profile, the departure band (#903, D5) shows
/// the departure window; dragging it sets the route's departure.
/// </summary>
internal sealed class ClearanceViewModel : ViewModelBase
{
    private readonly PassageCheckService _check;
    private readonly IMarinerSettingsProvider? _mariner;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly IThemeService? _theme;
    private ClearanceProfile? _profile;
    private PassageCheckSnapshot? _shown;
    private ClearanceAxis _axis = ClearanceAxis.Distance;
    private readonly RectangularSection _cursorSection;
    private DepartureBand? _band;
    private PassageDepartureWindow? _shownWindow;
    private readonly DepartureDrag _drag;

    public ClearanceViewModel(
        PassageCheckService check,
        IMarinerSettingsProvider? mariner = null,
        ITimeFormatProvider? timeFormat = null,
        IThemeService? theme = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        _check = check;
        _mariner = mariner;
        _timeFormat = timeFormat;
        _theme = theme;

        _drag = new DepartureDrag(check);
        _cursorSection = new RectangularSection { Xi = double.NaN, Xj = double.NaN, ZIndex = 20 };
        ShowDistanceCommand = new RelayCommand(() => Axis = ClearanceAxis.Distance);
        ShowTimeCommand = new RelayCommand(() => Axis = ClearanceAxis.Time, () => CanShowTime);
        ToggleSourcesCommand = new RelayCommand(() => IsSourcesOpen = !IsSourcesOpen);

        _check.Changed += (_, _) => Refresh();
        if (_theme is not null)
            _theme.ThemeChanged += (_, _) => Rebuild(force: true);
        if (_mariner is not null)
            _mariner.Changed += _ => Rebuild(force: true);
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += _ => Rebuild(force: true);
        Refresh();
    }

    public ICommand ShowDistanceCommand { get; }

    public ICommand ShowTimeCommand { get; }

    public ICommand ToggleSourcesCommand { get; }

    /// <summary>The profile's x axis: distance or time (D3).</summary>
    public ClearanceAxis Axis
    {
        get => _axis;
        set
        {
            if (value == ClearanceAxis.Time && !CanShowTime)
                value = ClearanceAxis.Distance;
            if (!SetProperty(ref _axis, value))
                return;
            OnPropertyChanged(nameof(IsDistanceAxis));
            OnPropertyChanged(nameof(IsTimeAxis));
            Rebuild(force: true);
        }
    }

    public bool IsDistanceAxis => Axis == ClearanceAxis.Distance;

    public bool IsTimeAxis => Axis == ClearanceAxis.Time;

    private bool _canShowTime;
    /// <summary>Whether the Time axis is available (the passage has ETAs).</summary>
    public bool CanShowTime
    {
        get => _canShowTime;
        private set
        {
            if (SetProperty(ref _canShowTime, value))
            {
                OnPropertyChanged(nameof(TimeTooltip));
                ((RelayCommand)ShowTimeCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string TimeTooltip => CanShowTime ? Strings.Tooltip_ClearanceTime : Strings.Tooltip_ClearanceTimeUnavailable;

    private bool _hasProfile;
    /// <summary>Whether there is a profile to show.</summary>
    public bool HasProfile
    {
        get => _hasProfile;
        private set => SetProperty(ref _hasProfile, value);
    }

    private string? _emptyText = Strings.Clearance_Empty;
    /// <summary>What shows when there is no profile.</summary>
    public string? EmptyText
    {
        get => _emptyText;
        private set => SetProperty(ref _emptyText, value);
    }

    private bool _isStale;
    /// <summary>Whether the profile is being recomputed (dimmed).</summary>
    public bool IsStale
    {
        get => _isStale;
        private set
        {
            if (SetProperty(ref _isStale, value))
                OnPropertyChanged(nameof(ProfileOpacity));
        }
    }

    public double ProfileOpacity => IsStale ? 0.45 : 1.0;

    private ISeries[] _series = [];
    public ISeries[] Series
    {
        get => _series;
        private set => SetProperty(ref _series, value);
    }

    private ICartesianAxis[] _xAxes = [new Axis()];
    public ICartesianAxis[] XAxes
    {
        get => _xAxes;
        private set => SetProperty(ref _xAxes, value);
    }

    private ICartesianAxis[] _yAxes = [new Axis()];
    public ICartesianAxis[] YAxes
    {
        get => _yAxes;
        private set => SetProperty(ref _yAxes, value);
    }

    private IReadOnlyList<RectangularSection> _sections = [];
    public IReadOnlyList<RectangularSection> Sections
    {
        get => _sections;
        private set => SetProperty(ref _sections, value);
    }

    // ── Readout (D4) ────────────────────────────────────────────────

    private bool _hasReadout;
    public bool HasReadout
    {
        get => _hasReadout;
        private set => SetProperty(ref _hasReadout, value);
    }

    private string? _readoutPosition;
    /// <summary><c>{NM} · {ETA}</c>.</summary>
    public string? ReadoutPosition
    {
        get => _readoutPosition;
        private set => SetProperty(ref _readoutPosition, value);
    }

    private string? _readoutLeg;
    public string? ReadoutLeg
    {
        get => _readoutLeg;
        private set => SetProperty(ref _readoutLeg, value);
    }

    private string? _depthText;
    public string? DepthText
    {
        get => _depthText;
        private set => SetProperty(ref _depthText, value);
    }

    private string? _depthSource;
    /// <summary><c>S-102 · {cell} · ±0,3 m</c>, or "S-101 DepthArea · lower confidence".</summary>
    public string? DepthSource
    {
        get => _depthSource;
        private set => SetProperty(ref _depthSource, value);
    }

    private bool _isLowConfidenceDepth;
    public bool IsLowConfidenceDepth
    {
        get => _isLowConfidenceDepth;
        private set => SetProperty(ref _isLowConfidenceDepth, value);
    }

    private bool _isLowConfidence;
    /// <summary>Whether the readout's sample is lower confidence (S-101 depth or unconfirmed datums) and has a clearance band.</summary>
    public bool IsLowConfidence
    {
        get => _isLowConfidence;
        private set => SetProperty(ref _isLowConfidence, value);
    }

    private string? _waterLevelText;
    public string? WaterLevelText
    {
        get => _waterLevelText;
        private set => SetProperty(ref _waterLevelText, value);
    }

    private string? _waterLevelSource;
    /// <summary><c>S-104 · {station} · {step}</c>, or "No S-104 at this ETA · tide not applied".</summary>
    public string? WaterLevelSource
    {
        get => _waterLevelSource;
        private set => SetProperty(ref _waterLevelSource, value);
    }

    private string? _draftText;
    public string? DraftText
    {
        get => _draftText;
        private set => SetProperty(ref _draftText, value);
    }

    private string? _ukcText;
    public string? UkcText
    {
        get => _ukcText;
        private set => SetProperty(ref _ukcText, value);
    }

    private string? _stateText;
    /// <summary>The verdict words for the sample's state.</summary>
    public string? StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    private CheckTone _tone = CheckTone.Neutral;
    public CheckTone Tone
    {
        get => _tone;
        private set
        {
            if (!SetProperty(ref _tone, value))
                return;
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(IsDestructive));
            OnPropertyChanged(nameof(IsNeutral));
        }
    }

    public bool IsSuccess => Tone == CheckTone.Success;

    public bool IsWarning => Tone == CheckTone.Warning;

    public bool IsDestructive => Tone == CheckTone.Destructive;

    public bool IsNeutral => Tone == CheckTone.Neutral;

    /// <summary>The waypoint numbers along the top of the profile, with their x.</summary>
    public IReadOnlyList<(double X, int Number)> WaypointNumbers { get; private set; } = [];

    /// <summary>The sample the readout describes.</summary>
    public UkcSample? ReadoutSample { get; private set; }

    // ── Departure band (D5) ─────────────────────────────────────────

    private bool _hasBand;
    /// <summary>Whether the departure band shows (a sweep over S-104 exists).</summary>
    public bool HasBand
    {
        get => _hasBand;
        private set => SetProperty(ref _hasBand, value);
    }

    private IReadOnlyList<DepartureBandSegment> _bandSegments = [];
    /// <summary>The band's runs of departures.</summary>
    public IReadOnlyList<DepartureBandSegment> BandSegments
    {
        get => _bandSegments;
        private set => SetProperty(ref _bandSegments, value);
    }

    private double? _bandKnobFraction;
    /// <summary>Where the knob (the current departure) sits across the band, 0–1.</summary>
    public double? BandKnobFraction
    {
        get => _bandKnobFraction;
        private set => SetProperty(ref _bandKnobFraction, value);
    }

    private string? _departureText;
    /// <summary>The current departure, HH:MM, beside the band.</summary>
    public string? DepartureText
    {
        get => _departureText;
        private set => SetProperty(ref _departureText, value);
    }

    private string? _bandStartLabel;
    public string? BandStartLabel
    {
        get => _bandStartLabel;
        private set => SetProperty(ref _bandStartLabel, value);
    }

    private string? _bandMiddleLabel;
    public string? BandMiddleLabel
    {
        get => _bandMiddleLabel;
        private set => SetProperty(ref _bandMiddleLabel, value);
    }

    private string? _bandEndLabel;
    /// <summary>"12:00 · S-104 ends".</summary>
    public string? BandEndLabel
    {
        get => _bandEndLabel;
        private set => SetProperty(ref _bandEndLabel, value);
    }

    private bool _isBandStale;
    /// <summary>Whether the band is being swept again for changed legs (dimmed).</summary>
    public bool IsBandStale
    {
        get => _isBandStale;
        private set
        {
            if (SetProperty(ref _isBandStale, value))
                OnPropertyChanged(nameof(BandOpacity));
        }
    }

    public double BandOpacity => IsBandStale ? 0.45 : 1.0;

    /// <summary>The band model, or <c>null</c> when there is no band.</summary>
    public DepartureBand? Band => _band;

    /// <summary>Starts a departure drag at a fraction of the band: one undo step until <see cref="EndDepartureDrag"/>.</summary>
    public void BeginDepartureDrag(double fraction)
    {
        if (_band is { } band)
            _drag.Begin(band, band.DepartureAt(fraction));
    }

    /// <summary>Sets the departure to the swept departure under a fraction of the band, recomputing live.</summary>
    public void DragDepartureTo(double fraction)
    {
        if (_band is { } band)
            _drag.MoveTo(band, band.DepartureAt(fraction));
    }

    /// <summary>Ends a departure drag, closing its undo step.</summary>
    public void EndDepartureDrag() => _drag.End();

    /// <summary>Moves the departure by a number of swept departures (arrow keys).</summary>
    public void NudgeDeparture(int steps)
    {
        if (_band is { } band)
            _drag.Nudge(band, steps);
    }

    private void UpdateBand(bool force)
    {
        var window = _check.IsActive && _check.Current is { } current && _check.Window is { } w && w.RouteId == current.RouteId ? w : null;
        if (force || !ReferenceEquals(window, _shownWindow))
        {
            _shownWindow = window;
            _band = window is null ? null : DepartureBand.Build(window);
            HasBand = _band is not null;
            BandSegments = _band?.Segments ?? [];
            if (_band is { } band)
            {
                BandStartLabel = Time(band.From, band.From);
                BandMiddleLabel = Time(band.Middle, band.From);
                BandEndLabel = string.Format(CultureInfo.CurrentCulture, Strings.Clearance_BandEndFormat, Time(band.To, band.From));
            }
            else
            {
                BandStartLabel = BandMiddleLabel = BandEndLabel = null;
            }
        }

        IsBandStale = _band is not null && _check.IsWindowComputing;
        var departure = _check.Departure;
        BandKnobFraction = _band is { } b && departure is { } d ? b.Fraction(d) : null;
        DepartureText = departure is { } dep ? Time(dep, dep) : null;
    }

    private string Time(DateTimeOffset t, DateTimeOffset reference) => PassageFormatting.FormatEta(t, reference, TimeFormat);

    // ── Sources (D6) ────────────────────────────────────────────────

    private bool _isSourcesOpen;
    public bool IsSourcesOpen
    {
        get => _isSourcesOpen;
        set => SetProperty(ref _isSourcesOpen, value);
    }

    public ObservableCollection<ClearanceSourceGroup> SourceGroups { get; } = new();

    /// <summary>
    /// Moves the shared cursor to an x on the profile (pointer hover).
    /// </summary>
    public void SetCursorAtX(double x)
    {
        if (_profile is { } profile)
            _check.SetCursor(profile.DistanceAtX(x));
    }

    /// <summary>The cursor's x on the profile, or <c>null</c> when hidden.</summary>
    public double? CursorX => _profile is { } p && _check.Cursor is { } c ? p.XAtDistance(c.DistanceMeters) : null;

    /// <summary>Re-reads the service: rebuilds the chart when the result changed, and always moves the cursor.</summary>
    public void Refresh()
    {
        var snapshot = _check.IsActive ? _check.Current : null;
        IsStale = snapshot is not null && _check.IsStale;
        if (!ReferenceEquals(snapshot, _shown))
            Rebuild(force: false);
        else if (snapshot is null)
            EmptyText = EmptyTextNow();
        UpdateCursor();
        UpdateBand(force: false);
    }

    private DepthUnit DepthUnit => _mariner?.Current.DepthUnit ?? DepthUnit.Metres;

    // Without a draft (#953) there is no clearance to profile.
    private string EmptyTextNow() =>
        !_check.IsActive ? Strings.Clearance_Empty
        : _check.Input is { HasDraft: false } ? Strings.Check_AddDraft
        : Strings.Check_Computing;

    private TimeFormat TimeFormat => _timeFormat?.Current ?? TimeFormat.Local;

    private void Rebuild(bool force)
    {
        var snapshot = _check.IsActive ? _check.Current : null;
        if (!force && ReferenceEquals(snapshot, _shown))
            return;
        _shown = snapshot;

        if (snapshot is null)
        {
            _profile = null;
            HasProfile = false;
            EmptyText = EmptyTextNow();
            Series = [];
            Sections = [];
            SourceGroups.Clear();
            UpdateCursor();
            UpdateBand(force);
            return;
        }

        CanShowTime = ClearanceProfile.HasTimes(snapshot);
        if (_axis == ClearanceAxis.Time && !CanShowTime)
        {
            _axis = ClearanceAxis.Distance;
            OnPropertyChanged(nameof(Axis));
            OnPropertyChanged(nameof(IsDistanceAxis));
            OnPropertyChanged(nameof(IsTimeAxis));
        }

        var profile = ClearanceProfile.Build(snapshot, _axis);
        _profile = profile;
        HasProfile = true;
        EmptyText = null;
        BuildChart(profile);
        BuildSources(snapshot);
        UpdateCursor();
        UpdateBand(force);
    }

    private void BuildChart(ClearanceProfile profile)
    {
        var dark = _theme?.IsDarkTheme ?? false;
        var theme = ChartTheme.Resolve(dark);
        var states = RouteCheckColors.For(StatePalette(_theme?.Current));
        var sand = dark ? new SKColor(0x4A, 0x42, 0x32) : new SKColor(0xEA, 0xDD, 0xBC);
        var sandLine = dark ? new SKColor(0x9C, 0x86, 0x5C) : new SKColor(0xA8, 0x8C, 0x55);
        var samples = profile.Samples;

        ObservablePoint[] Points(Func<UkcSample, int, double?> y)
        {
            var points = new ObservablePoint[samples.Count];
            for (var i = 0; i < samples.Count; i++)
                points[i] = new ObservablePoint(profile.X(samples[i]), y(samples[i], i));
            return points;
        }

        // A point belongs to a run when it or its neighbour does, so adjacent
        // runs (S-102 then S-101, say) join without a gap.
        bool InRun(int i, Func<UkcSample, bool> member) =>
            member(samples[i]) || (i > 0 && member(samples[i - 1]) && samples[i].Depth is not null);

        static LineSeries<ObservablePoint> Line(ObservablePoint[] values, LiveChartsCore.Painting.Paint? fill, LiveChartsCore.Painting.Paint? stroke, int z) => new()
        {
            Values = values,
            Fill = fill,
            Stroke = stroke,
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            ZIndex = z,
            IsHoverable = false,
            AnimationsSpeed = TimeSpan.Zero,
        };

        bool IsS102(UkcSample s) => s.Depth is { IsLowConfidence: false };
        bool IsS101(UkcSample s) => s.Depth is { IsLowConfidence: true };
        var hatch = new LinearGradientPaint(
            [sandLine.WithAlpha(0x90), sandLine.WithAlpha(0x90), sand, sand],
            new SKPoint(0, 0),
            new SKPoint(0.004f, 0.02f),
            [0f, 0.35f, 0.35f, 1f],
            SKShaderTileMode.Repeat);

        var series = new List<ISeries>
        {
            // Required-UKC band above the seabed (amber 30 %).
            Line(Points((s, _) => profile.BandTop(s)), new SolidColorPaint(states.Tight.WithAlpha(0x4D)), null, 1),
            // Squeeze: keel within the band (warning) or below it (destructive), 35 %.
            Line(Points((s, _) => s.Band == ClearanceBand.Tight ? profile.Keel(s) : null),
                new SolidColorPaint(states.Tight.WithAlpha(0x59)), null, 2),
            Line(Points((s, _) => s.Band == ClearanceBand.Below ? MaxOrNull(profile.Keel(s), profile.Seabed(s)) : null),
                new SolidColorPaint(states.Below.WithAlpha(0x59)), null, 3),
            // Seabed: S-102 sand with a darker line; S-101 fallback hatched.
            Line(Points((s, i) => InRun(i, IsS102) ? profile.Seabed(s) : null),
                new SolidColorPaint(sand), new SolidColorPaint(sandLine, 1.5f), 4),
            Line(Points((s, i) => InRun(i, IsS101) ? profile.Seabed(s) : null),
                hatch, new SolidColorPaint(sandLine, 1.2f) { PathEffect = new DashEffect([4, 3]) }, 5),
            // Water level (blue) and keel = water level − draft − squat (dark).
            Line(Points((s, _) => profile.WaterLevel(s)), null, new SolidColorPaint(theme.SeriesPrimary, 2f), 6),
            Line(Points((s, _) => profile.Keel(s)), null, new SolidColorPaint(theme.AxisName, 2f), 7),
        };

        // The min-UKC dot with its value.
        if (profile.Snapshot.Result.Minimum is { } min
            && samples[min.SampleIndex] is var minSample
            && profile.Keel(minSample) is { } keel)
        {
            var label = PassageFormatting.Format(min.ClearanceMeters, PassageQuantity.Depth, DepthUnit);
            series.Add(new ScatterSeries<ObservablePoint>
            {
                Values = [new ObservablePoint(profile.X(minSample), keel)],
                Fill = new SolidColorPaint(states.Of(min.State)),
                Stroke = new SolidColorPaint(states.Casing, 1.5f),
                GeometrySize = 9,
                DataLabelsPaint = new SolidColorPaint(states.Of(min.State)),
                DataLabelsSize = 11,
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                DataLabelsFormatter = _ => label,
                ZIndex = 8,
                IsHoverable = false,
                AnimationsSpeed = TimeSpan.Zero,
            });
        }

        // Waypoint numbers along the top, as labels on invisible points (a
        // zero-width section doesn't draw its label).
        var legsForNumbers = profile.Snapshot.Result.Legs;
        var numbers = new List<ObservablePoint>(legsForNumbers.Count + 1);
        for (var w = 0; w <= legsForNumbers.Count; w++)
        {
            var distance = w < legsForNumbers.Count
                ? legsForNumbers[w].StartDistanceMeters
                : legsForNumbers[^1].StartDistanceMeters + legsForNumbers[^1].DistanceMeters;
            numbers.Add(new ObservablePoint(profile.XAtDistance(distance), profile.Top - (profile.Top * 0.04)));
        }

        WaypointNumbers = numbers.Select((p, i) => (p.X!.Value, i + 1)).ToArray();
        series.Add(new ScatterSeries<ObservablePoint>
        {
            Values = numbers,
            GeometrySize = 0,
            Fill = null,
            Stroke = null,
            DataLabelsPaint = new SolidColorPaint(theme.AxisLabel),
            DataLabelsSize = 10,
            DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle,
            DataLabelsFormatter = point => (numbers.IndexOf(point.Model!) + 1).ToString(CultureInfo.CurrentCulture),
            ZIndex = 11,
            IsHoverable = false,
            AnimationsSpeed = TimeSpan.Zero,
        });

        Series = [.. series];

        var axisLabels = new SolidColorPaint(theme.AxisLabel);
        var separators = new SolidColorPaint(theme.Separator);
        var (from, to) = profile.XRange();
        var departure = profile.Snapshot.Result.Departure;
        var timeFormat = TimeFormat;
        XAxes =
        [
            new Axis
            {
                MinLimit = from,
                MaxLimit = to,
                LabelsPaint = axisLabels,
                SeparatorsPaint = null,
                TextSize = 10,
                MinStep = profile.Axis == ClearanceAxis.Time ? 30 : 0.5,
                ForceStepToMin = profile.Axis == ClearanceAxis.Time,
                Labeler = profile.Axis == ClearanceAxis.Time
                    ? v => PassageFormatting.FormatEta(departure.AddMinutes(v), departure, timeFormat)
                    : v => v.ToString("0.#", CultureInfo.CurrentCulture),
            },
        ];
        YAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = profile.Top,
                MinStep = 5,
                ForceStepToMin = true,
                LabelsPaint = axisLabels,
                SeparatorsPaint = separators,
                TextSize = 10,
                Labeler = profile.ElevationLabel,
            },
        ];

        // Waypoint numbers along the top, dotted leg boundaries, and the 4 px state strip.
        var sections = new List<RectangularSection>();
        var legs = profile.Snapshot.Result.Legs;
        var boundary = new SolidColorPaint(theme.Separator.WithAlpha(0xFF), 1f) { PathEffect = new DashEffect([2, 3]) };
        for (var w = 0; w <= legs.Count; w++)
        {
            var distance = w < legs.Count ? legs[w].StartDistanceMeters : legs[^1].StartDistanceMeters + legs[^1].DistanceMeters;
            var x = profile.XAtDistance(distance);
            sections.Add(new RectangularSection
            {
                Xi = x,
                Xj = x,
                Yi = 0,
                Yj = profile.Top * 0.92,
                Stroke = boundary,
                ZIndex = 9,
            });
        }

        var strip = profile.Top * 0.025;
        var start = 0;
        // The state strip: the band's colour, fainter where it's lower confidence (#939).
        static bool Faint(UkcSample s) => s.State != UkcState.NoData && s.IsLowConfidence;
        for (var i = 1; i <= samples.Count; i++)
        {
            if (i < samples.Count && samples[i].State == samples[start].State && Faint(samples[i]) == Faint(samples[start]))
                continue;
            var color = states.Of(samples[start].State);
            sections.Add(new RectangularSection
            {
                Xi = profile.XAtDistance(samples[start].StartDistanceMeters),
                Xj = profile.XAtDistance(samples[i - 1].EndDistanceMeters),
                Yi = 0,
                Yj = strip,
                Fill = new SolidColorPaint(Faint(samples[start]) ? color.WithAlpha(0x8C) : color),
                ZIndex = 10,
            });
            start = i;
        }

        _cursorSection.Stroke = new SolidColorPaint(theme.AxisName, 1f);
        _cursorSection.Yi = 0;
        _cursorSection.Yj = profile.Top;
        sections.Add(_cursorSection);
        Sections = sections;
    }

    /// <summary>
    /// The route palette whose state colours the profile uses on a chrome
    /// theme: Day on Light, Dusk on S-100 Dusk, Night on Dark and S-100 Night
    /// (#906), so the profile matches the route on the map.
    /// </summary>
    internal static PaletteType StatePalette(ChromeTheme? theme) => theme switch
    {
        ChromeTheme.S100Dusk => PaletteType.Dusk,
        ChromeTheme.Dark or ChromeTheme.S100Night => PaletteType.Night,
        _ => PaletteType.Day,
    };

    private static double? MaxOrNull(double? a, double? b) => a is { } x && b is { } y ? Math.Max(x, y) : a ?? b;

    private void UpdateCursor()
    {
        OnPropertyChanged(nameof(CursorX));
        if (_profile is not { } profile)
        {
            _cursorSection.Xi = _cursorSection.Xj = double.NaN;
            HasReadout = false;
            ReadoutSample = null;
            return;
        }

        // Without a cursor, the readout describes the minimum (or the first sample).
        var cursor = _check.Cursor;
        var sample = cursor is { } c
            ? profile.SampleAtDistance(c.DistanceMeters)
            : profile.Snapshot.Result.Minimum is { } min ? profile.Samples[min.SampleIndex]
            : profile.Samples.FirstOrDefault();
        if (cursor is { } at)
            _cursorSection.Xi = _cursorSection.Xj = profile.XAtDistance(at.DistanceMeters);
        else
            _cursorSection.Xi = _cursorSection.Xj = double.NaN;

        ShowReadout(profile, sample, cursor);
    }

    private void ShowReadout(ClearanceProfile profile, UkcSample? sample, PassageCursor? cursor)
    {
        ReadoutSample = sample;
        HasReadout = sample is not null;
        if (sample is null)
            return;

        var unit = DepthUnit;
        var result = profile.Snapshot.Result;
        var distance = (cursor?.DistanceMeters ?? sample.DistanceMeters) / PassageGeodesy.MetersPerNauticalMile;
        DateTimeOffset? eta = cursor is { } c ? c.Eta : sample.HasEta ? sample.Time : null;
        ReadoutPosition = eta is { } t
            ? string.Format(CultureInfo.CurrentCulture, Strings.Clearance_ReadoutPositionFormat, distance,
                PassageFormatting.FormatEta(t, result.Departure, TimeFormat))
            : string.Format(CultureInfo.CurrentCulture, Strings.Clearance_ReadoutDistanceFormat, distance);
        var legIndex = cursor?.LegIndex ?? sample.LegIndex;
        var place = legIndex < profile.Snapshot.LegPlaceNames.Count ? profile.Snapshot.LegPlaceNames[legIndex] : null;
        var legLabel = string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, legIndex + 1, legIndex + 2);
        ReadoutLeg = place is null ? legLabel : legLabel + " · " + place;

        if (sample.Depth is { } depth)
        {
            DepthText = PassageFormatting.Format(depth.DepthMeters, PassageQuantity.Depth, unit);
            IsLowConfidenceDepth = depth.IsLowConfidence;
            DepthSource = depth.Kind == ChartedDepthKind.Bathymetry
                ? depth.UncertaintyMeters is { } u
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Clearance_SourceS102Format, Cell(depth), PassageFormatting.Format(u, PassageQuantity.Depth, unit))
                    : string.Format(CultureInfo.CurrentCulture, Strings.Clearance_SourceS102NoUncertaintyFormat, Cell(depth))
                : string.Format(CultureInfo.CurrentCulture, Strings.Clearance_SourceS101Format, depth.Kind);
        }
        else
        {
            DepthText = "—";
            IsLowConfidenceDepth = false;
            DepthSource = Strings.Clearance_NoDepth;
        }

        var water = sample.WaterLevel;
        WaterLevelText = water.HeightMeters is { } h ? PassageFormatting.Format(h, PassageQuantity.Depth, unit) : "—";
        WaterLevelSource = water.Status switch
        {
            WaterLevelStatus.Applied when water.Source is { } src => string.Format(
                CultureInfo.CurrentCulture,
                src.Kind == WaterLevelSourceKind.Station ? Strings.Clearance_SourceS104StationFormat : Strings.Clearance_SourceS104GridFormat,
                src.StationId ?? src.GridCell ?? src.DatasetId,
                water.StepTime is { } step ? TimeFormatting.Format(new DateTimeOffset(DateTime.SpecifyKind(step, DateTimeKind.Utc)), TimeFormat) : "—"),
            WaterLevelStatus.DatumMismatch => Strings.Clearance_TideDatumMismatch,
            _ => Strings.Clearance_NoTide,
        };

        DraftText = PassageFormatting.Format(sample.DraftMeters, PassageQuantity.Depth, unit)
            + (sample.SquatMeters > 0 ? " + " + PassageFormatting.Format(sample.SquatMeters, PassageQuantity.Depth, unit) : string.Empty);
        UkcText = sample.ClearanceMeters is { } ukc ? PassageFormatting.Format(ukc, PassageQuantity.Depth, unit) : "—";
        var stateText = sample.State switch
        {
            UkcState.MeetsSettings => Strings.Check_LegendMeets,
            UkcState.Tight => Strings.Check_LegendTight,
            UkcState.BelowSetting => Strings.Check_LegendBelow,
            _ => Strings.Clearance_StateNoData,
        };
        // Confidence qualifies the band, never replaces it (#939).
        IsLowConfidence = sample.State != UkcState.NoData && sample.IsLowConfidence;
        StateText = IsLowConfidence
            ? string.Format(CultureInfo.CurrentCulture, Strings.Clearance_StateLowConfidenceFormat, stateText)
            : stateText;
        Tone = sample.State switch
        {
            UkcState.MeetsSettings => CheckTone.Success,
            UkcState.Tight => CheckTone.Warning,
            UkcState.BelowSetting => CheckTone.Destructive,
            _ => CheckTone.Neutral,
        };
    }

    private static string Cell(ChartedDepth depth) =>
        depth.Reference is { } r ? $"{depth.DatasetId} ({r})" : depth.DatasetId ?? string.Empty;

    private void BuildSources(PassageCheckSnapshot snapshot)
    {
        var result = snapshot.Result;
        var sources = result.Sources;
        var unit = DepthUnit;
        IReadOnlyList<string> OrNone(List<string> items) => items.Count == 0 ? [Strings.Clearance_SourcesNone] : items;

        string Stretch(PassageStretch s, string label) => string.Format(
            CultureInfo.CurrentCulture,
            Strings.Clearance_StretchFormat,
            s.StartDistanceMeters / PassageGeodesy.MetersPerNauticalMile,
            s.EndDistanceMeters / PassageGeodesy.MetersPerNauticalMile,
            label);

        string Datums(IReadOnlyList<int> codes) => codes.Count == 0
            ? Strings.Clearance_DatumUnknown
            : string.Join(", ", codes.Select(c => VerticalDatums.GetLabel(c)));

        SourceGroups.Clear();
        SourceGroups.Add(new ClearanceSourceGroup(Strings.Clearance_SourcesS102, OrNone([.. sources.BathymetryDatasets])));
        var fallback = Merge(result.LowConfidence.Where(s => s.Kind == PassageStretchKind.ChartedDepthFromS101))
            .Select(s => Range(s))
            .ToList();
        if (fallback.Count > 0)
            fallback.Insert(0, string.Join(", ", sources.FallbackDatasets));
        SourceGroups.Add(new ClearanceSourceGroup(Strings.Clearance_SourcesS101, OrNone(fallback)));
        SourceGroups.Add(new ClearanceSourceGroup(
            Strings.Clearance_SourcesS104,
            OrNone(sources.WaterLevelSources
                .Select(w => w.StationId is { } station
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Clearance_StationFormat, w.DatasetId, station)
                    : string.Format(CultureInfo.CurrentCulture, Strings.Clearance_GridFormat, w.DatasetId))
                .ToList())));
        SourceGroups.Add(new ClearanceSourceGroup(
            Strings.Clearance_SourcesUncertainty,
            sources.MaxUncertaintyMeters is { } u
                ? [string.Format(CultureInfo.CurrentCulture, Strings.Clearance_MaxUncertaintyFormat, PassageFormatting.Format(u, PassageQuantity.Depth, unit))]
                : [Strings.Clearance_SourcesNone]));
        SourceGroups.Add(new ClearanceSourceGroup(
            Strings.Clearance_SourcesDatums,
            [string.Format(CultureInfo.CurrentCulture, Strings.Clearance_DatumsFormat, Datums(sources.DepthDatums), Datums(sources.WaterLevelDatums))]));
        SourceGroups.Add(new ClearanceSourceGroup(
            Strings.Clearance_SourcesGaps,
            OrNone(result.Gaps.Concat(result.LowConfidence.Where(s => s.Kind == PassageStretchKind.DatumUnconfirmed))
                .GroupBy(g => g.Kind)
                .SelectMany(Merge)
                .OrderBy(g => g.StartDistanceMeters)
                .Select(g => Stretch(g, GapLabel(g.Kind)))
                .ToList())));
    }

    /// <summary>Runs closer than this are listed as one in Sources.</summary>
    private const double SourcesMergeMeters = 0.1 * PassageGeodesy.MetersPerNauticalMile;

    // Joins stretches of one kind that are less than 0,1 NM apart, so a
    // ragged S-102 edge reads as one line at the list's precision.
    private static IEnumerable<PassageStretch> Merge(IEnumerable<PassageStretch> stretches)
    {
        PassageStretch? open = null;
        foreach (var s in stretches.OrderBy(s => s.StartDistanceMeters))
        {
            if (open is not null && s.StartDistanceMeters - open.EndDistanceMeters < SourcesMergeMeters)
            {
                open = open with
                {
                    LastSample = s.LastSample,
                    LastLeg = s.LastLeg,
                    EndDistanceMeters = Math.Max(open.EndDistanceMeters, s.EndDistanceMeters),
                    EndTime = s.EndTime,
                };
                continue;
            }

            if (open is not null)
                yield return open;
            open = s;
        }

        if (open is not null)
            yield return open;
    }

    private static string Range(PassageStretch s) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Clearance_RangeFormat,
        s.StartDistanceMeters / PassageGeodesy.MetersPerNauticalMile,
        s.EndDistanceMeters / PassageGeodesy.MetersPerNauticalMile);

    internal static string GapLabel(PassageStretchKind kind) => kind switch
    {
        PassageStretchKind.NoChartedDepth => Strings.Clearance_Gap_NoChartedDepth,
        PassageStretchKind.NoWaterLevelCoverage => Strings.Clearance_Gap_NoWaterLevelCoverage,
        PassageStretchKind.WaterLevelBeforeStart => Strings.Clearance_Gap_WaterLevelBeforeStart,
        PassageStretchKind.WaterLevelAfterEnd => Strings.Clearance_Gap_WaterLevelAfterEnd,
        PassageStretchKind.WaterLevelNoData => Strings.Clearance_Gap_WaterLevelNoData,
        PassageStretchKind.DatumMismatch => Strings.Clearance_Gap_DatumMismatch,
        PassageStretchKind.ChartedDepthFromS101 => Strings.Clearance_Gap_ChartedDepthFromS101,
        _ => Strings.Clearance_Gap_DatumUnconfirmed,
    };
}
