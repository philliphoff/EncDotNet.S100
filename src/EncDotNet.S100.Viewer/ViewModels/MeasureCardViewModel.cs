using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The measure card at the map's top left while Measure is on (#1130,
/// D1–D10, D17): the geometry, typed From and To positions, the shown leg's
/// distance in both unit systems, its forward and reverse bearings, the
/// path's total, and copy.
/// </summary>
/// <remarks>
/// From and To are the ends of the shown leg (D9). Before there's a leg,
/// From is the first point and To adds the second. A field being typed in
/// isn't overwritten as the path changes; Enter or leaving it commits it and
/// Esc reverts it (D7).
/// </remarks>
internal sealed class MeasureCardViewModel : ViewModelBase
{
    private readonly MeasureSession _session;
    private string _fromText = string.Empty;
    private string _toText = string.Empty;
    private bool _fromDirty;
    private bool _toDirty;
    private bool _refreshing;
    private string? _fromError;
    private string? _toError;

    public MeasureCardViewModel(MeasureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        RhumbCommand = new RelayCommand(() => _session.SetGeometry(LegGeometry.Rhumb));
        GreatCircleCommand = new RelayCommand(() => _session.SetGeometry(LegGeometry.GreatCircle));
        ToggleGeometryCommand = new RelayCommand(_session.ToggleGeometry);
        CopyCommand = new RelayCommand(Copy, () => CanCopy);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        PreviousLegCommand = new RelayCommand(() => SelectLeg(-1), () => CanGoPrevious);
        NextLegCommand = new RelayCommand(() => SelectLeg(+1), () => CanGoNext);
        CommitFromCommand = new RelayCommand(() => CommitFrom());
        CommitToCommand = new RelayCommand(() => CommitTo());
        _session.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Raised with the clipboard text when the measurement is copied (D8).</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>Raised when the card's close button is pressed.</summary>
    public event EventHandler? CloseRequested;

    private MeasurePathState State => _session.State;

    /// <summary>True while the path is measured along great circles.</summary>
    public bool IsGreatCircle => _session.Geometry == LegGeometry.GreatCircle;

    /// <summary>True while the path is measured along rhumb lines.</summary>
    public bool IsRhumb => !IsGreatCircle;

    public RelayCommand RhumbCommand { get; }
    public RelayCommand GreatCircleCommand { get; }
    public RelayCommand ToggleGeometryCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand PreviousLegCommand { get; }
    public RelayCommand NextLegCommand { get; }
    public RelayCommand CommitFromCommand { get; }
    public RelayCommand CommitToCommand { get; }

    /// <summary>The From field: the shown leg's start, or the first point.</summary>
    public string FromText
    {
        get => _fromText;
        set
        {
            if (SetProperty(ref _fromText, value ?? string.Empty) && !_refreshing)
                _fromDirty = true;
        }
    }

    /// <summary>The To field: the shown leg's end; empty until there is one.</summary>
    public string ToText
    {
        get => _toText;
        set
        {
            if (SetProperty(ref _toText, value ?? string.Empty) && !_refreshing)
                _toDirty = true;
        }
    }

    /// <summary>Why the From text was refused, or <c>null</c>.</summary>
    public string? FromError
    {
        get => _fromError;
        private set
        {
            if (SetProperty(ref _fromError, value))
                OnPropertyChanged(nameof(HasFromError));
        }
    }

    /// <summary>Why the To text was refused, or <c>null</c>.</summary>
    public string? ToError
    {
        get => _toError;
        private set
        {
            if (SetProperty(ref _toError, value))
                OnPropertyChanged(nameof(HasToError));
        }
    }

    public bool HasFromError => _fromError is not null;
    public bool HasToError => _toError is not null;

    /// <summary>Whether To can be typed: once there's a first point.</summary>
    public bool IsToEnabled => State.Waypoints.Count > 0;

    /// <summary>Whether the hint shows: the path has no leg yet.</summary>
    public bool ShowsHint => State.CurrentLeg is null;

    /// <summary>The hint shown before the first leg.</summary>
    public static string HintText => Strings.Status_MeasureNoData;

    /// <summary>Whether the distance and bearing rows show.</summary>
    public bool HasLeg => State.CurrentLeg is not null;

    /// <summary>Whether the leg stepper and Total show: the path has more than one leg.</summary>
    public bool HasMultipleLegs => State.ComputeLegs().Count > 1;

    /// <summary>"Leg 2 of 3".</summary>
    public string LegCaption => State.CurrentLeg is { } leg
        ? string.Format(CultureInfo.CurrentCulture, Strings.Measure_LegOf, leg.Index, State.ComputeLegs().Count)
        : string.Empty;

    public bool CanGoPrevious => State.CurrentLeg is { Index: > 1 };

    public bool CanGoNext => State.CurrentLeg is { } leg && leg.Index < State.ComputeLegs().Count;

    /// <summary>The shown leg's distance in the primary unit.</summary>
    public string DistanceText => State.CurrentLeg is { } leg ? MeasureFormat.Distance(leg.DistanceMetres, _session.Unit) : string.Empty;

    /// <summary>The shown leg's distance in the other unit system (D6).</summary>
    public string DistanceOtherText => State.CurrentLeg is { } leg ? MeasureFormat.OtherDistance(leg.DistanceMetres, _session.Unit) : string.Empty;

    /// <summary>The shown leg's forward true bearing, e.g. <c>295.7°T</c>.</summary>
    public string BearingText => State.CurrentLeg is { } leg ? TrueBearing(leg.BearingDeg) : string.Empty;

    /// <summary>The shown leg's reverse true bearing (D4).</summary>
    public string ReverseText => State.CurrentLeg is { } leg ? TrueBearing(leg.ReverseBearingDeg) : string.Empty;

    /// <summary>The whole path's length in both unit systems: <c>37.16 NM · 68.816 km</c>.</summary>
    public string TotalText
    {
        get
        {
            var metres = State.TotalDistanceMetres();
            return string.Format(CultureInfo.CurrentCulture, Strings.Measure_TotalValue,
                MeasureFormat.Distance(metres, _session.Unit), MeasureFormat.OtherDistance(metres, _session.Unit));
        }
    }

    /// <summary>Accessible name of the Distance value: "Distance 17.90 nautical miles".</summary>
    public string DistanceName => ValueName(Strings.Measure_Distance, DistanceText);

    /// <summary>Accessible name of the other-unit distance.</summary>
    public string DistanceOtherName => ValueName(Strings.Measure_Distance, DistanceOtherText);

    /// <summary>Accessible name of the Bearing value.</summary>
    public string BearingName => ValueName(Strings.Measure_Bearing, BearingText);

    /// <summary>Accessible name of the Reverse value.</summary>
    public string ReverseName => ValueName(Strings.Measure_Reverse, ReverseText);

    /// <summary>Accessible name of the Total value.</summary>
    public string TotalName => ValueName(Strings.Measure_Total,
        MeasureFormat.Distance(State.TotalDistanceMetres(), _session.Unit));

    /// <summary>The earth model in the footer (D17).</summary>
    public static string ModelText => MeasureSession.EarthModelText;

    /// <summary>The copy button's tooltip, naming its shortcut.</summary>
    public static string CopyTooltip => string.Format(
        CultureInfo.CurrentCulture, Strings.Tooltip_MeasureCopy, ShortcutText.Command("C"));

    /// <summary>Whether there's a placed leg to copy.</summary>
    public bool CanCopy => State.Waypoints.Count >= 2;

    /// <summary>Commits the From field. Returns false when it isn't a position.</summary>
    public bool CommitFrom()
    {
        if (!_fromDirty)
            return true;
        if (string.IsNullOrWhiteSpace(_fromText))
        {
            RevertFrom();
            return true;
        }

        if (!LatLonFormatter.TryParse(_fromText, out var lat, out var lon))
        {
            FromError = Strings.Measure_PositionError;
            return false;
        }

        var position = new GeoPosition(lat, lon);
        if (State.Waypoints.Count == 0)
            State.AppendTypedWaypoint(position);
        else
            State.MoveWaypoint(FromIndex, position);
        _fromDirty = false;
        FromError = null;
        _session.NotifyChanged();
        _session.OnPositionCommitted(position);
        return true;
    }

    /// <summary>Commits the To field. Returns false when it isn't a position.</summary>
    public bool CommitTo()
    {
        if (!_toDirty)
            return true;
        if (string.IsNullOrWhiteSpace(_toText) || State.Waypoints.Count == 0)
        {
            RevertTo();
            return true;
        }

        if (!LatLonFormatter.TryParse(_toText, out var lat, out var lon))
        {
            ToError = Strings.Measure_PositionError;
            return false;
        }

        var position = new GeoPosition(lat, lon);
        if (State.CurrentLeg is { IsRubberBand: false } leg)
            State.MoveWaypoint(leg.Index, position);
        else
            State.AppendTypedWaypoint(position);
        _toDirty = false;
        ToError = null;
        _session.NotifyChanged();
        _session.OnPositionCommitted(position);
        return true;
    }

    /// <summary>
    /// Puts the From field back to the shown position. Returns true when there
    /// was anything to revert, so a second Esc can leave the tool (D20).
    /// </summary>
    public bool RevertFrom()
    {
        var reverted = _fromDirty || HasFromError;
        _fromDirty = false;
        FromError = null;
        Refresh();
        return reverted;
    }

    /// <summary>Puts the To field back; see <see cref="RevertFrom"/>.</summary>
    public bool RevertTo()
    {
        var reverted = _toDirty || HasToError;
        _toDirty = false;
        ToError = null;
        Refresh();
        return reverted;
    }

    /// <summary>
    /// The clipboard text (D8): every placed leg's positions (the app's form
    /// and decimal degrees), distance in both unit systems and bearings, the
    /// geometry, the total and the earth model.
    /// </summary>
    internal static string BuildClipboardText(MeasurePathState state, DistanceUnit unit)
    {
        var culture = CultureInfo.CurrentCulture;
        var legs = state.ComputeLegs().Where(l => !l.IsRubberBand).ToList();
        var text = new StringBuilder();
        text.Append(Strings.Measure_CardTitle).Append(" · ")
            .AppendLine(state.Geometry == LegGeometry.GreatCircle ? Strings.Measure_GreatCircle : Strings.Measure_Rhumb);
        foreach (var leg in legs)
        {
            text.AppendLine();
            text.AppendLine(string.Format(culture, Strings.Measure_CopyLeg, leg.Index, legs.Count));
            text.AppendLine(Position(Strings.Measure_From, leg.From));
            text.AppendLine(Position(Strings.Measure_To, leg.To));
            text.AppendLine(string.Format(culture, Strings.Measure_CopyDistance,
                MeasureFormat.Distance(leg.DistanceMetres, unit), MeasureFormat.OtherDistance(leg.DistanceMetres, unit)));
            text.AppendLine(string.Format(culture, Strings.Measure_CopyBearings,
                MeasureFormat.Bearing(leg.BearingDeg), MeasureFormat.Bearing(leg.ReverseBearingDeg)));
        }

        text.AppendLine();
        if (legs.Count > 1)
        {
            var total = legs.Sum(l => l.DistanceMetres);
            text.AppendLine(string.Format(culture, Strings.Measure_CopyTotal,
                MeasureFormat.Distance(total, unit), MeasureFormat.OtherDistance(total, unit)));
        }

        text.Append(string.Format(culture, Strings.Measure_CopyModel, MeasureSession.EarthModelText));
        return text.ToString();

        static string Position(string label, GeoPosition position) => string.Format(
            CultureInfo.CurrentCulture, Strings.Measure_CopyPosition, label,
            LatLonFormatter.Format(position.Latitude, position.Longitude),
            LatLonFormatter.FormatDecimal(position.Latitude, position.Longitude));
    }

    /// <summary>
    /// Speaks a formatted value with its unit in words, for accessible names:
    /// <c>17.90 NM</c> reads "17.90 nautical miles", <c>295.7°T</c> "295.7
    /// degrees true".
    /// </summary>
    internal static string Spoken(string value)
    {
        (string Suffix, string Words)[] units =
        [
            (" NM", Strings.Measure_UnitNauticalMiles),
            (" km", Strings.Measure_UnitKilometres),
            (" mi", Strings.Measure_UnitStatuteMiles),
            (" m", Strings.Measure_UnitMetres),
            ("°T", Strings.Measure_UnitDegreesTrue),
        ];
        foreach (var (suffix, words) in units)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
                return value[..^suffix.Length] + " " + words;
        }

        return value;
    }

    /// <summary>Zero-based waypoint index of the From field.</summary>
    private int FromIndex => State.CurrentLeg is { } leg ? leg.Index - 1 : 0;

    private void SelectLeg(int step)
    {
        if (State.CurrentLeg is { } leg && State.SelectLeg(leg.Index + step))
            _session.NotifyChanged();
    }

    private void Copy()
    {
        if (CanCopy)
            CopyRequested?.Invoke(this, BuildClipboardText(State, _session.Unit));
    }

    private static string TrueBearing(double degrees) => string.Format(
        CultureInfo.CurrentCulture, Strings.Measure_TrueBearing, MeasureFormat.Bearing(degrees));

    private static string ValueName(string label, string value) => string.Format(
        CultureInfo.CurrentCulture, Strings.Measure_ValueName, label, Spoken(value));

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var leg = State.CurrentLeg;
            if (!_fromDirty)
            {
                GeoPosition? from = leg?.From ?? (State.Waypoints.Count > 0 ? State.Waypoints[0] : null);
                FromText = from is { } f ? LatLonFormatter.Format(f.Latitude, f.Longitude) : string.Empty;
            }

            if (!_toDirty)
                ToText = leg is { } l ? LatLonFormatter.Format(l.To.Latitude, l.To.Longitude) : string.Empty;
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(IsGreatCircle));
        OnPropertyChanged(nameof(IsRhumb));
        OnPropertyChanged(nameof(IsToEnabled));
        OnPropertyChanged(nameof(ShowsHint));
        OnPropertyChanged(nameof(HasLeg));
        OnPropertyChanged(nameof(HasMultipleLegs));
        OnPropertyChanged(nameof(LegCaption));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(DistanceText));
        OnPropertyChanged(nameof(DistanceOtherText));
        OnPropertyChanged(nameof(BearingText));
        OnPropertyChanged(nameof(ReverseText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(DistanceName));
        OnPropertyChanged(nameof(DistanceOtherName));
        OnPropertyChanged(nameof(BearingName));
        OnPropertyChanged(nameof(ReverseName));
        OnPropertyChanged(nameof(TotalName));
        OnPropertyChanged(nameof(CanCopy));
        CopyCommand.NotifyCanExecuteChanged();
        PreviousLegCommand.NotifyCanExecuteChanged();
        NextLegCommand.NotifyCanExecuteChanged();
    }
}
