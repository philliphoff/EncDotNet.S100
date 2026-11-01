using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Range &amp; bearing card (#1130 D13–D17): Origin, Range and Bearing
/// fields, End and Reverse, the range and bearing locks, the Rings row, the
/// geometry control and the earth model. Over the shared
/// <see cref="RangeBearingState"/>.
/// </summary>
/// <remarks>
/// A field the user is typing in isn't overwritten while the cursor moves the
/// end: the view marks it as being edited, and it catches up when it's
/// committed or reverted. Leaving a field without changing its text commits
/// nothing, so tabbing through the card never locks a value.
/// </remarks>
internal sealed class RangeBearingViewModel : ViewModelBase
{
    /// <summary>The card's text fields.</summary>
    public enum Field
    {
        /// <summary>The origin position.</summary>
        Origin,

        /// <summary>The range.</summary>
        Range,

        /// <summary>The bearing.</summary>
        Bearing,

        /// <summary>The ring interval.</summary>
        RingInterval,

        /// <summary>The ring count.</summary>
        RingCount,
    }

    private readonly RangeBearingState _state;
    private readonly Func<DistanceUnit> _unit;
    private readonly HashSet<Field> _editing = [];
    private readonly Dictionary<Field, string> _shown = [];

    private string _originText = string.Empty;
    private string _rangeText = string.Empty;
    private string _bearingText = string.Empty;
    private string _ringIntervalText = string.Empty;
    private string _ringCountText = string.Empty;
    private string? _originError;
    private string? _rangeError;
    private string? _bearingError;
    private string? _ringError;

    /// <param name="state">The shared model.</param>
    /// <param name="unit">The Distance unit setting.</param>
    /// <param name="close">Turns the tool off (the card's close button).</param>
    public RangeBearingViewModel(RangeBearingState state, Func<DistanceUnit> unit, Action close)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(close);
        _state = state;
        _unit = unit;
        _state.Changed += (_, _) => Refresh();
        _state.ToolActiveChanged += (_, _) => OnPropertyChanged(nameof(IsActive));

        CloseCommand = new RelayCommand(close);
        UnlockRangeCommand = new RelayCommand(() =>
        {
            _rangeError = null;
            _state.UnlockRange();
        });
        UnlockBearingCommand = new RelayCommand(() =>
        {
            _bearingError = null;
            _state.UnlockBearing();
        });
        ToggleGeometryCommand = new RelayCommand(ToggleGeometry);
        Refresh();
    }

    /// <summary>The shared model.</summary>
    public RangeBearingState State => _state;

    /// <summary>Turns the tool off.</summary>
    public ICommand CloseCommand { get; }

    /// <summary>Unlocks the range (its lock icon).</summary>
    public ICommand UnlockRangeCommand { get; }

    /// <summary>Unlocks the bearing (its lock icon).</summary>
    public ICommand UnlockBearingCommand { get; }

    /// <summary>Switches between rhumb line and great circle (<c>G</c>).</summary>
    public ICommand ToggleGeometryCommand { get; }

    /// <summary>True while the tool is on, so the card shows.</summary>
    public bool IsActive => _state.IsToolActive;

    /// <summary>True before an origin is placed, when the card shows its hint.</summary>
    public bool ShowHint => _state.Origin is null;

    /// <summary>The origin field's text.</summary>
    public string OriginText { get => _originText; set => SetProperty(ref _originText, value ?? string.Empty); }

    /// <summary>Why the typed origin was refused, or <c>null</c>.</summary>
    public string? OriginError { get => _originError; private set => SetProperty(ref _originError, value); }

    /// <summary>The range field's text, in the primary unit.</summary>
    public string RangeText { get => _rangeText; set => SetProperty(ref _rangeText, value ?? string.Empty); }

    /// <summary>Why the typed range was refused, or <c>null</c>.</summary>
    public string? RangeError { get => _rangeError; private set => SetProperty(ref _rangeError, value); }

    /// <summary>The range in the other unit system, under the field (D6).</summary>
    public string RangeOther { get; private set; } = string.Empty;

    /// <summary>The bearing field's text: <c>295.7°</c>.</summary>
    public string BearingText { get => _bearingText; set => SetProperty(ref _bearingText, value ?? string.Empty); }

    /// <summary>Why the typed bearing was refused, or <c>null</c>.</summary>
    public string? BearingError { get => _bearingError; private set => SetProperty(ref _bearingError, value); }

    /// <summary>The end position, or empty.</summary>
    public string EndText { get; private set; } = string.Empty;

    /// <summary>The reverse bearing, or empty.</summary>
    public string ReverseText { get; private set; } = string.Empty;

    /// <summary>Whether the range is locked.</summary>
    public bool IsRangeLocked => _state.IsRangeLocked;

    /// <summary>Whether the bearing is locked.</summary>
    public bool IsBearingLocked => _state.IsBearingLocked;

    /// <summary>Rhumb line selected.</summary>
    public bool IsRhumb
    {
        get => _state.Geometry == LegGeometry.Rhumb;
        set { if (value) _state.Geometry = LegGeometry.Rhumb; }
    }

    /// <summary>Great circle selected.</summary>
    public bool IsGreatCircle
    {
        get => _state.Geometry == LegGeometry.GreatCircle;
        set { if (value) _state.Geometry = LegGeometry.GreatCircle; }
    }

    /// <summary>Whether range rings are drawn.</summary>
    public bool RingsEnabled
    {
        get => _state.RingsEnabled;
        set => _state.SetRings(value);
    }

    /// <summary>The ring interval field's text, in the primary unit.</summary>
    public string RingIntervalText { get => _ringIntervalText; set => SetProperty(ref _ringIntervalText, value ?? string.Empty); }

    /// <summary>The ring count field's text.</summary>
    public string RingCountText { get => _ringCountText; set => SetProperty(ref _ringCountText, value ?? string.Empty); }

    /// <summary>Why a ring value was refused, or <c>null</c>.</summary>
    public string? RingError { get => _ringError; private set => SetProperty(ref _ringError, value); }

    /// <summary>The earth model footer: <c>WGS-84 ellipsoid</c>.</summary>
    public string ModelText { get; } = RangeBearingText.Model();

    /// <summary>Accessible name of the range value, with its unit.</summary>
    public string RangeAccessibleName => ValueName(Strings.RangeBearing_Range, RangeText);

    /// <summary>Accessible name of the bearing value.</summary>
    public string BearingAccessibleName => ValueName(Strings.RangeBearing_Bearing, BearingText);

    /// <summary>Accessible name of the end value.</summary>
    public string EndAccessibleName => ValueName(Strings.RangeBearing_End, EndText);

    /// <summary>Accessible name of the reverse bearing value.</summary>
    public string ReverseAccessibleName => ValueName(Strings.RangeBearing_Reverse, ReverseText);

    /// <summary>The result as clipboard text, or <c>null</c> before there's an origin.</summary>
    public string? CopyText => RangeBearingText.Copy(_state, _unit());

    /// <summary>The view says a field has focus: its text isn't overwritten until it's committed or reverted.</summary>
    public void BeginEdit(Field field) => _editing.Add(field);

    /// <summary>
    /// Enter or leaving the field: applies its text when it changed. A
    /// refused value keeps the field's text, with the reason shown.
    /// </summary>
    public void Commit(Field field)
    {
        _editing.Remove(field);
        var text = field switch
        {
            Field.Origin => OriginText,
            Field.Range => RangeText,
            Field.Bearing => BearingText,
            Field.RingInterval => RingIntervalText,
            _ => RingCountText,
        };
        if (_shown.TryGetValue(field, out var shown) && string.Equals(text.Trim(), shown, StringComparison.Ordinal))
        {
            Revert(field);
            return;
        }

        switch (field)
        {
            case Field.Origin:
                CommitOrigin(text);
                break;
            case Field.Range:
                CommitRange(text);
                break;
            case Field.Bearing:
                CommitBearing(text);
                break;
            case Field.RingInterval:
                CommitRingInterval(text);
                break;
            case Field.RingCount:
                CommitRingCount(text);
                break;
        }

        Refresh();
    }

    /// <summary>Whether <paramref name="field"/> holds typing that Esc would drop, or a refused value.</summary>
    public bool HasPendingEdit(Field field)
    {
        var (text, error) = field switch
        {
            Field.Origin => (OriginText, OriginError),
            Field.Range => (RangeText, RangeError),
            Field.Bearing => (BearingText, BearingError),
            Field.RingInterval => (RingIntervalText, RingError),
            _ => (RingCountText, RingError),
        };
        return error is not null
            || !_shown.TryGetValue(field, out var shown)
            || !string.Equals(text.Trim(), shown, StringComparison.Ordinal);
    }

    /// <summary>Esc in a field: drops the typing and shows the current value again.</summary>
    public void Revert(Field field)
    {
        _editing.Remove(field);
        switch (field)
        {
            case Field.Origin: OriginError = null; break;
            case Field.Range: RangeError = null; break;
            case Field.Bearing: BearingError = null; break;
            default: RingError = null; break;
        }

        Refresh();
    }

    /// <summary>
    /// ↑/↓ in the bearing field: turns the bearing by <paramref name="degrees"/>
    /// and locks it.
    /// </summary>
    public void NudgeBearing(double degrees)
    {
        var current = _state.BearingDegrees ?? 0.0;
        _editing.Remove(Field.Bearing);
        BearingError = null;
        _state.SetBearing(Math.Round(current + degrees, 1));
    }

    /// <summary>
    /// ↑/↓ in the range field: changes the range by one unit of the shown
    /// value's last digit, in the primary unit, and locks it.
    /// </summary>
    public void NudgeRange(int direction)
    {
        if (_state.RangeMetres is not { } metres)
            return;
        var step = RangeStepMetres(metres, _unit());
        var next = Math.Round(metres / step) * step + direction * step;
        if (next <= 0)
            return;
        _editing.Remove(Field.Range);
        RangeError = null;
        _state.SetRange(next);
    }

    /// <summary>One unit of the last digit <see cref="MeasureFormat.Distance"/> shows for <paramref name="metres"/>.</summary>
    internal static double RangeStepMetres(double metres, DistanceUnit unit) => unit switch
    {
        // Metres below 1 km, and 0.001 km above: a metre either way.
        DistanceUnit.Metric => 1.0,
        DistanceUnit.Miles => 0.01 * MeasureFormat.MetresPerStatuteMile,
        _ => 0.01 * PassageGeodesy.MetersPerNauticalMile,
    };

    /// <summary>The Distance unit changed: show values in the new one.</summary>
    public void OnUnitChanged() => Refresh();

    private void ToggleGeometry() =>
        _state.Geometry = _state.Geometry == LegGeometry.Rhumb ? LegGeometry.GreatCircle : LegGeometry.Rhumb;

    private void CommitOrigin(string text)
    {
        if (LatLonFormatter.TryParse(text, out var lat, out var lon))
        {
            OriginError = null;
            _state.SetOrigin(new GeoPosition(lat, lon));
        }
        else
        {
            OriginError = Strings.RangeBearing_NotAPosition;
            _editing.Add(Field.Origin);
        }
    }

    private void CommitRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            RangeError = null;
            _state.SetRange(null);
            return;
        }

        if (MeasureFormat.TryParseRange(text, _unit(), out var metres, out var error))
        {
            RangeError = null;
            _state.SetRange(metres);
            return;
        }

        RangeError = RangeErrorText(error);
        _editing.Add(Field.Range);
    }

    private void CommitBearing(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            BearingError = null;
            _state.SetBearing(null);
            return;
        }

        if (MeasureFormat.TryParseBearing(text, out var degrees))
        {
            BearingError = null;
            _state.SetBearing(degrees);
            return;
        }

        BearingError = Strings.RangeBearing_NotABearing;
        _editing.Add(Field.Bearing);
    }

    private void CommitRingInterval(string text)
    {
        if (MeasureFormat.TryParseRange(text, _unit(), out var metres, out var error))
        {
            RingError = null;
            _state.SetRings(true, metres);
            return;
        }

        RingError = RangeErrorText(error);
        _editing.Add(Field.RingInterval);
    }

    private void CommitRingCount(string text)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count is >= 1 and <= RangeBearingState.MaxRingCount)
        {
            RingError = null;
            _state.SetRings(true, count: count);
            return;
        }

        RingError = Strings.RangeBearing_NotARingCount;
        _editing.Add(Field.RingCount);
    }

    internal static string RangeErrorText(RangeParseError error) => error switch
    {
        RangeParseError.AmbiguousM => Strings.RangeBearing_AmbiguousM,
        RangeParseError.NotPositive => Strings.RangeBearing_NotPositive,
        _ => Strings.RangeBearing_NotARange,
    };

    private void Refresh()
    {
        var unit = _unit();
        var origin = _state.Origin;
        var range = _state.RangeMetres;
        var bearing = _state.BearingDegrees;

        Show(Field.Origin, origin is { } o ? LatLonFormatter.Format(o.Latitude, o.Longitude) : string.Empty, v => OriginText = v);
        Show(Field.Range, range is { } r ? MeasureFormat.Distance(r, unit) : string.Empty, v => RangeText = v);
        Show(Field.Bearing, bearing is { } b ? MeasureFormat.Bearing(b) : string.Empty, v => BearingText = v);
        Show(Field.RingInterval, MeasureFormat.Distance(_state.RingIntervalMetres, unit), v => RingIntervalText = v);
        Show(Field.RingCount, _state.RingCount.ToString(CultureInfo.CurrentCulture), v => RingCountText = v);

        RangeOther = range is { } rr ? MeasureFormat.OtherDistance(rr, unit) : string.Empty;
        EndText = _state.End is { } e ? LatLonFormatter.Format(e.Latitude, e.Longitude) : string.Empty;
        ReverseText = _state.ReverseBearingDegrees is { } rev ? MeasureFormat.Bearing(rev) + "T" : string.Empty;

        OnPropertyChanged(nameof(ShowHint));
        OnPropertyChanged(nameof(RangeOther));
        OnPropertyChanged(nameof(EndText));
        OnPropertyChanged(nameof(ReverseText));
        OnPropertyChanged(nameof(IsRangeLocked));
        OnPropertyChanged(nameof(IsBearingLocked));
        OnPropertyChanged(nameof(IsRhumb));
        OnPropertyChanged(nameof(IsGreatCircle));
        OnPropertyChanged(nameof(RingsEnabled));
        OnPropertyChanged(nameof(RangeAccessibleName));
        OnPropertyChanged(nameof(BearingAccessibleName));
        OnPropertyChanged(nameof(EndAccessibleName));
        OnPropertyChanged(nameof(ReverseAccessibleName));
    }

    private void Show(Field field, string value, Action<string> set)
    {
        _shown[field] = value;
        if (!_editing.Contains(field))
            set(value);
    }

    private static string ValueName(string label, string value) =>
        string.Format(CultureInfo.CurrentCulture, Strings.RangeBearing_ValueName, label, value);
}
