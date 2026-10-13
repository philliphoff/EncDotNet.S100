using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One editable passage value: a 26 px field in the Passage card or a leg's
/// detail. A set field shows its value with a solid border; an unset field
/// is dashed and shows its placeholder ("+ Add", "View time", or the value a
/// leg inherits). Typing commits on Enter or when the field loses focus; Esc
/// reverts; text that doesn't parse shows the field in error and changes nothing.
/// </summary>
internal sealed class PassageFieldViewModel : ViewModelBase
{
    private readonly Func<string, bool> _commit;
    private readonly Action? _reset;
    private string _committed = string.Empty;

    /// <summary>Creates a field.</summary>
    /// <param name="id">The field's automation id, e.g. <c>Routes.Passage.Speed</c>.</param>
    /// <param name="label">The field's label, e.g. "Speed".</param>
    /// <param name="error">The message shown when typed text doesn't parse.</param>
    /// <param name="commit">Applies typed text; returns <c>false</c> when it doesn't parse.</param>
    /// <param name="reset">Resets a leg override to inherit; <c>null</c> for Passage card fields.</param>
    public PassageFieldViewModel(string id, string label, string error, Func<string, bool> commit, Action? reset = null)
    {
        Id = id;
        _label = label;
        ErrorText = error;
        _commit = commit;
        _reset = reset;
        CommitCommand = new RelayCommand(Commit);
        RevertCommand = new RelayCommand(() =>
        {
            Text = _committed;
            HasError = false;
        });
        ResetCommand = new RelayCommand(() => _reset?.Invoke(), () => IsOverridden);
    }

    /// <summary>The field's automation id.</summary>
    public string Id { get; }

    /// <summary>The automation id of the field's Reset link.</summary>
    public string ResetId => Id + ".Reset";

    /// <summary>A muted word shown inside a set field, e.g. "static" for Required UKC.</summary>
    public string? Suffix { get; init; }

    /// <summary>Whether <see cref="Suffix"/> shows: the field has one and is set.</summary>
    public bool ShowsSuffix => Suffix is not null && IsSet;

    /// <summary>Whether an unset field shows a clock before its placeholder (Departure's "View time").</summary>
    public bool ShowsClockWhenUnset { get; init; }

    /// <summary>Whether the clock shows now.</summary>
    public bool ShowsClock => ShowsClockWhenUnset && !IsSet;

    private string _label;
    /// <summary>The label above the field ("Speed · override" when a leg overrides it).</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    private string _text = string.Empty;
    /// <summary>The text being edited.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                HasError = false;
        }
    }

    private string _placeholder = string.Empty;
    /// <summary>What an empty field shows: "+ Add", "View time", or an inherited value.</summary>
    public string Placeholder
    {
        get => _placeholder;
        private set => SetProperty(ref _placeholder, value);
    }

    private bool _isSet;
    /// <summary>Whether the value is set here: a solid border and a mono value. Otherwise dashed.</summary>
    public bool IsSet
    {
        get => _isSet;
        private set
        {
            if (SetProperty(ref _isSet, value))
            {
                OnPropertyChanged(nameof(IsUnset));
                OnPropertyChanged(nameof(ShowsSuffix));
                OnPropertyChanged(nameof(ShowsClock));
            }
        }
    }

    /// <summary>Whether the field is unset (dashed, showing its placeholder).</summary>
    public bool IsUnset => !_isSet;

    private bool _isOverridden;
    /// <summary>Whether a leg overrides the Passage value: accent label and border, and a Reset link.</summary>
    public bool IsOverridden
    {
        get => _isOverridden;
        private set
        {
            if (SetProperty(ref _isOverridden, value))
                ((RelayCommand)ResetCommand).NotifyCanExecuteChanged();
        }
    }

    private bool _hasError;
    /// <summary>Whether the typed text didn't parse.</summary>
    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
                OnPropertyChanged(nameof(ErrorTip));
        }
    }

    /// <summary>The field's tooltip: <see cref="ErrorText"/> while in error, else none.</summary>
    public string? ErrorTip => _hasError ? ErrorText : null;

    /// <summary>The message for <see cref="HasError"/>.</summary>
    public string ErrorText { get; }

    /// <summary>Applies <see cref="Text"/> (Enter, or focus leaving the field).</summary>
    public ICommand CommitCommand { get; }

    /// <summary>Restores the committed text (Esc).</summary>
    public ICommand RevertCommand { get; }

    /// <summary>Resets a leg override so the leg inherits again.</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Shows a value read from the model.</summary>
    /// <param name="text">The value's text, or empty when unset.</param>
    /// <param name="placeholder">What to show when empty.</param>
    /// <param name="isOverridden">Whether a leg overrides the value.</param>
    /// <param name="overrideLabel">The label to show while overridden.</param>
    internal void Load(string text, string placeholder, bool isOverridden = false, string? overrideLabel = null)
    {
        _committed = text;
        _text = text;
        OnPropertyChanged(nameof(Text));
        HasError = false;
        Placeholder = placeholder;
        IsSet = text.Length > 0;
        IsOverridden = isOverridden;
        if (overrideLabel is not null)
            Label = overrideLabel;
    }

    /// <summary>Restores the label used when not overridden.</summary>
    internal void SetLabel(string label) => Label = label;

    private void Commit()
    {
        if (Text == _committed)
        {
            HasError = false;
            return;
        }
        if (!_commit(Text))
            HasError = true;
    }
}

/// <summary>
/// The Passage card under the route header (handoff B1–B3): draft and
/// squat, speed, departure, required UKC, vessel and cross-track, all
/// optional. Collapsed on a new route, with a one-line prompt or summary.
/// Every edit runs through the route's <see cref="Route.History"/>.
/// </summary>
internal sealed class PassageCardViewModel : ViewModelBase
{
    private readonly RoutesService _routes;
    private readonly IRouteDisplayUnits _units;
    private readonly Func<DateTimeOffset> _now;

    public PassageCardViewModel(RoutesService routes, IRouteDisplayUnits units, Func<DateTimeOffset> now)
    {
        _routes = routes;
        _units = units;
        _now = now;

        DraftSquat = new PassageFieldViewModel("Routes.Passage.DraftSquat", Strings.Passage_DraftSquat, InvalidDepth, CommitDraftSquat);
        Speed = new PassageFieldViewModel("Routes.Passage.Speed", Strings.Passage_Speed, Strings.Passage_InvalidSpeed, CommitSpeed);
        Departure = new PassageFieldViewModel("Routes.Passage.Departure", Strings.Passage_Departure, Strings.Passage_InvalidDeparture, CommitDeparture)
        {
            ShowsClockWhenUnset = true,
        };
        RequiredUkc = new PassageFieldViewModel("Routes.Passage.RequiredUkc", Strings.Passage_RequiredUkc, InvalidDepth, CommitRequiredUkc)
        {
            Suffix = Strings.Passage_Static,
        };
        CrossTrack = new PassageFieldViewModel("Routes.Passage.CrossTrack", Strings.Passage_CrossTrack, Strings.Passage_InvalidCrossTrack, CommitCrossTrack);
        Vessel = new VesselFieldsViewModel(routes);

        ToggleCommand = new RelayCommand(() => _routes.IsPassageCardExpanded = !_routes.IsPassageCardExpanded);
        _routes.PassageCardChanged += (_, _) => OnPropertyChanged(nameof(IsExpanded));
    }

    /// <summary>The message for a depth that doesn't parse, with an example in the app's locale.</summary>
    internal static string InvalidDepth => string.Format(CultureInfo.CurrentCulture, Strings.Passage_InvalidDepth, PassageText.Number(11.5));

    /// <summary>Draft and the optional squat allowance.</summary>
    public PassageFieldViewModel DraftSquat { get; }

    /// <summary>Planned speed, in knots.</summary>
    public PassageFieldViewModel Speed { get; }

    /// <summary>Planned departure; unset uses the view time.</summary>
    public PassageFieldViewModel Departure { get; }

    /// <summary>Required static UKC.</summary>
    public PassageFieldViewModel RequiredUkc { get; }

    /// <summary>Cross-track limit, port and starboard.</summary>
    public PassageFieldViewModel CrossTrack { get; }

    /// <summary>The vessel field and its popover.</summary>
    public VesselFieldsViewModel Vessel { get; }

    /// <summary>Whether the card shows its fields, or the one-line collapsed text.</summary>
    public bool IsExpanded
    {
        get => _routes.IsPassageCardExpanded;
        set => _routes.IsPassageCardExpanded = value;
    }

    /// <summary>Expands or collapses the card.</summary>
    public ICommand ToggleCommand { get; }

    /// <summary>The help line under the fields.</summary>
    public string HelpText => Strings.Passage_Help;

    /// <summary>"static", shown inside the Required UKC field (v1 has no dynamic UKC).</summary>
    public string StaticText => Strings.Passage_Static;

    private string _collapsedText = Strings.Passage_CollapsedPrompt;
    /// <summary>
    /// The collapsed card's line: "Passage · Add draft and speed to check
    /// clearance" until values are set, then a summary of them.
    /// </summary>
    public string CollapsedText
    {
        get => _collapsedText;
        private set => SetProperty(ref _collapsedText, value);
    }

    private bool _hasDraft;
    /// <summary>Whether a draft is set: Check passage needs one.</summary>
    public bool HasDraft
    {
        get => _hasDraft;
        private set => SetProperty(ref _hasDraft, value);
    }

    /// <summary>Shows <paramref name="route"/>'s passage values.</summary>
    internal void Refresh(Route? route)
    {
        var unit = _units.DepthUnit;
        var p = route?.Passage;
        DraftSquat.Load(PassageText.DraftSquat(p?.DraftMeters, p?.SquatMeters, unit), Strings.Passage_Add);
        Speed.Load(p?.SpeedKnots is { } s ? PassageText.Speed(s) : string.Empty, Strings.Passage_Add);
        Departure.Load(p?.Departure is { } d ? PassageText.Departure(d, _units.TimeFormat) : string.Empty, Strings.Passage_ViewTime);
        RequiredUkc.Load(p?.RequiredUkcMeters is { } u ? PassageText.Depth(u, unit) : string.Empty, Strings.Passage_Add);
        CrossTrack.Load(PassageText.CrossTrack(p?.PortCrossTrackMeters, p?.StarboardCrossTrackMeters), Strings.Passage_Add);
        Vessel.Refresh(route);
        HasDraft = p?.DraftMeters is not null;
        CollapsedText = Summary(route);
    }

    private string Summary(Route? route)
    {
        if (route is null || (route.Passage.IsEmpty && route.Info.Vessel?.Name is null))
            return Strings.Passage_CollapsedPrompt;

        var p = route.Passage;
        var unit = _units.DepthUnit;
        var parts = new List<string>();
        if (route.Info.Vessel?.Name is { Length: > 0 } vessel)
            parts.Add(vessel);
        if (p.DraftMeters is not null || p.SquatMeters is not null)
            parts.Add(PassageText.DraftSquat(p.DraftMeters, p.SquatMeters, unit));
        if (p.SpeedKnots is { } speed)
            parts.Add(PassageText.Speed(speed));
        if (p.Departure is { } departure)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Passage_DepartureSummaryFormat, PassageText.DepartureTime(departure, _units.TimeFormat)));
        if (p.RequiredUkcMeters is { } ukc)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Passage_UkcSummaryFormat, PassageText.Depth(ukc, unit)));
        if (p.PortCrossTrackMeters is not null || p.StarboardCrossTrackMeters is not null)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Passage_CrossTrackSummaryFormat, PassageText.CrossTrack(p.PortCrossTrackMeters, p.StarboardCrossTrackMeters)));
        return string.Format(CultureInfo.CurrentCulture, Strings.Passage_CollapsedSummaryFormat, string.Join(" · ", parts));
    }

    private bool Edit(Action<Route> edit)
    {
        if (_routes.Routes.ActiveRoute is not { } route)
            return true;
        route.History.Execute(RouteEditLabels.EditPassage, () => edit(route));
        return true;
    }

    private bool CommitDraftSquat(string text)
    {
        if (!PassageText.TryParseDraftSquat(text, _units.DepthUnit, out var draft, out var squat))
            return false;
        return Edit(r =>
        {
            r.SetPassageValue(RoutePassageField.Draft, draft);
            r.SetPassageValue(RoutePassageField.Squat, squat);
        });
    }

    private bool CommitSpeed(string text)
    {
        double? speed = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!PassageText.TryParseSpeed(text, out var parsed))
                return false;
            speed = parsed;
        }
        return Edit(r => r.SetPassageValue(RoutePassageField.Speed, speed));
    }

    private bool CommitDeparture(string text)
    {
        if (!PassageText.TryParseDeparture(text, _units.TimeFormat, _now(), out var departure))
            return false;
        return Edit(r => r.SetDeparture(departure));
    }

    private bool CommitRequiredUkc(string text)
    {
        double? ukc = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!PassageText.TryParseDepth(text, _units.DepthUnit, out var parsed))
                return false;
            ukc = parsed;
        }
        return Edit(r => r.SetPassageValue(RoutePassageField.RequiredUkc, ukc));
    }

    private bool CommitCrossTrack(string text)
    {
        if (!PassageText.TryParseCrossTrack(text, out var port, out var starboard))
            return false;
        return Edit(r =>
        {
            r.SetPassageValue(RoutePassageField.PortCrossTrack, port);
            r.SetPassageValue(RoutePassageField.StarboardCrossTrack, starboard);
        });
    }
}

/// <summary>
/// The Passage card's Vessel field: the vessel name (or "+ Add") and a
/// popover with name, MMSI, IMO, call sign, length and beam. Each value
/// commits when its box loses focus, as one undoable step.
/// </summary>
internal sealed class VesselFieldsViewModel : ViewModelBase
{
    private readonly RoutesService _routes;
    private bool _loading;

    public VesselFieldsViewModel(RoutesService routes) => _routes = routes;

    private string _summary = string.Empty;
    /// <summary>The field's text: the vessel name, or empty when there is no vessel.</summary>
    public string Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
            {
                OnPropertyChanged(nameof(IsSet));
                OnPropertyChanged(nameof(IsUnset));
            }
        }
    }

    /// <summary>Whether a vessel is set.</summary>
    public bool IsSet => _summary.Length > 0;

    /// <summary>Whether no vessel is set (dashed "+ Add").</summary>
    public bool IsUnset => !IsSet;

    /// <summary>"+ Add".</summary>
    public string Placeholder => Strings.Passage_Add;

    private string? _name;
    /// <summary>Vessel name.</summary>
    public string? Name { get => _name; set => Set(ref _name, value, (v, x) => v.Name = x); }

    private string? _mmsi;
    /// <summary>MMSI.</summary>
    public string? Mmsi { get => _mmsi; set => Set(ref _mmsi, value, (v, x) => v.Mmsi = x); }

    private string? _imo;
    /// <summary>IMO number.</summary>
    public string? Imo { get => _imo; set => Set(ref _imo, value, (v, x) => v.Imo = x); }

    private string? _callsign;
    /// <summary>Call sign.</summary>
    public string? Callsign { get => _callsign; set => Set(ref _callsign, value, (v, x) => v.Callsign = x); }

    private string? _length;
    /// <summary>Overall length in metres, as typed.</summary>
    public string? Length { get => _length; set => SetNumber(ref _length, value, (v, x) => v.LengthMeters = x); }

    private string? _beam;
    /// <summary>Beam in metres, as typed.</summary>
    public string? Beam { get => _beam; set => SetNumber(ref _beam, value, (v, x) => v.BeamMeters = x); }

    internal void Refresh(Route? route)
    {
        _loading = true;
        try
        {
            var vessel = route?.Info.Vessel;
            Name = vessel?.Name;
            Mmsi = vessel?.Mmsi;
            Imo = vessel?.Imo;
            Callsign = vessel?.Callsign;
            Length = vessel?.LengthMeters?.ToString("0.#", CultureInfo.CurrentCulture);
            Beam = vessel?.BeamMeters?.ToString("0.#", CultureInfo.CurrentCulture);
            Summary = vessel is null
                ? string.Empty
                : vessel.Name is { Length: > 0 } n ? n : vessel.Mmsi ?? vessel.Imo ?? vessel.Callsign ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Set(ref string? field, string? value, Action<RouteVesselInfo, string?> apply)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (field == text)
            return;
        field = text;
        OnPropertyChanged(string.Empty);
        if (!_loading)
            Edit(v => apply(v, text));
    }

    private void SetNumber(ref string? field, string? value, Action<RouteVesselInfo, double?> apply)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (field == text)
            return;
        double? number = null;
        if (text is not null)
        {
            if (!PassageText.TryParseNumber(text, out var parsed) || parsed < 0)
                return;
            number = parsed;
        }
        field = text;
        OnPropertyChanged(string.Empty);
        if (!_loading)
            Edit(v => apply(v, number));
    }

    private void Edit(Action<RouteVesselInfo> apply)
    {
        if (_routes.Routes.ActiveRoute is not { } route)
            return;
        route.History.Execute(RouteEditLabels.EditRouteInfo, () =>
        {
            apply(route.Info.Vessel ??= new RouteVesselInfo());
            route.NotifyChanged();
        });
    }
}
