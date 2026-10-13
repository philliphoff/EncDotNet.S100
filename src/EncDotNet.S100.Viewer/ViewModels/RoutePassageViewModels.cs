using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A text field in the Routes panel that commits on Enter or when it loses
/// focus (see <c>RoutesView</c>) and reverts on Esc.
/// </summary>
internal interface ICommittableField
{
    /// <summary>Commits the typed text.</summary>
    ICommand CommitCommand { get; }
}

/// <summary>
/// One editable value in the Passage card or the leg detail. The field shows
/// <see cref="Text"/> (the value with its unit, or empty when unset, when the
/// view shows <see cref="Placeholder"/>). Typing and committing hands the text
/// to the owner's commit function, which edits the route through its undo
/// history; text that doesn't parse reverts.
/// </summary>
internal class RouteFieldViewModel : ViewModelBase, ICommittableField
{
    private readonly Func<string?, bool> _commit;
    private string? _value;

    /// <summary>Creates a field.</summary>
    /// <param name="label">The field's label, e.g. "Speed".</param>
    /// <param name="commit">Applies typed text to the route; returns
    /// <c>false</c> when the text is not a valid value.</param>
    public RouteFieldViewModel(string label, Func<string?, bool> commit)
    {
        _label = label;
        _commit = commit;
        CommitCommand = new RelayCommand(Commit);
        RevertCommand = new RelayCommand(() => Text = _value ?? string.Empty);
    }

    private string _label;
    /// <summary>The field's label.</summary>
    public string Label
    {
        get => _label;
        protected set => SetProperty(ref _label, value);
    }

    private string _text = string.Empty;
    /// <summary>The text being shown or edited.</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    private string _placeholder = Strings.Routes_FieldAdd;
    /// <summary>What an empty field shows: "+ Add", or an inherited value.</summary>
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value);
    }

    /// <summary>The field's automation id, e.g. <c>Routes.Passage.Speed</c> (#776).</summary>
    public string AutomationId { get; init; } = string.Empty;

    /// <summary>True when the field holds a value (solid border); false shows it dashed.</summary>
    public bool IsSet => _value is not null;

    /// <inheritdoc />
    public ICommand CommitCommand { get; }

    /// <summary>Restores the field to the route's value (Esc).</summary>
    public ICommand RevertCommand { get; }

    /// <summary>Shows <paramref name="value"/> (or nothing when <c>null</c>).</summary>
    public void Show(string? value)
    {
        _value = value;
        Text = value ?? string.Empty;
        OnPropertyChanged(nameof(IsSet));
    }

    private void Commit()
    {
        var typed = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
        if (typed == _value)
            return;
        if (!_commit(typed))
            Text = _value ?? string.Empty;
    }
}

/// <summary>
/// A leg-detail field: an override of a passage value. An overridden field
/// shows its value, a "· override" label and a Reset link; an inherited one is
/// empty and shows the passage value as its placeholder.
/// </summary>
internal sealed class RouteLegFieldViewModel : RouteFieldViewModel
{
    private readonly string _baseLabel;

    /// <summary>Creates a leg field.</summary>
    public RouteLegFieldViewModel(string label, Func<string?, bool> commit, Action reset)
        : base(label, commit)
    {
        _baseLabel = label;
        ResetCommand = new RelayCommand(reset);
    }

    private bool _isOverridden;
    /// <summary>True when the leg overrides the passage value.</summary>
    public bool IsOverridden
    {
        get => _isOverridden;
        private set => SetProperty(ref _isOverridden, value);
    }

    /// <summary>Removes the override so the leg inherits the passage value.</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Shows the leg's override, or the inherited value when there is none.</summary>
    public void Show(string? overrideText, string? inheritedText)
    {
        IsOverridden = overrideText is not null;
        Label = IsOverridden
            ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_FieldOverrideFormat, _baseLabel)
            : _baseLabel;
        Placeholder = inheritedText ?? Strings.Routes_FieldAdd;
        Show(overrideText);
    }
}

/// <summary>
/// The Passage card under the route header: the route-level values legs
/// inherit (draft and squat, speed, departure, required UKC, vessel and
/// cross-track). Every value is optional. The card keeps its field
/// view-models across route edits, so focus survives a commit.
/// </summary>
internal sealed class RoutePassageCardViewModel : ViewModelBase
{
    private readonly Func<Route?> _route;
    private readonly Func<DepthUnit> _depthUnit;
    private readonly Func<TimeFormat> _timeFormat;

    public RoutePassageCardViewModel(Func<Route?> route, Func<DepthUnit> depthUnit, Func<TimeFormat> timeFormat)
    {
        _route = route;
        _depthUnit = depthUnit;
        _timeFormat = timeFormat;

        Draft = new RouteFieldViewModel(Strings.Routes_PassageDraftSquat, t => CommitValue(RoutePassageField.Draft, PassageQuantity.Depth, t)) { AutomationId = "Routes.Passage.Draft" };
        Squat = new RouteFieldViewModel(Strings.Routes_PassageSquat, t => CommitValue(RoutePassageField.Squat, PassageQuantity.Depth, t))
        {
            AutomationId = "Routes.Passage.Squat",
            Placeholder = Strings.Routes_PassageAddSquat,
        };
        Speed = new RouteFieldViewModel(Strings.Routes_PassageSpeed, t => CommitValue(RoutePassageField.Speed, PassageQuantity.Speed, t)) { AutomationId = "Routes.Passage.Speed" };
        Departure = new RouteFieldViewModel(Strings.Routes_PassageDeparture, CommitDeparture)
        {
            AutomationId = "Routes.Passage.Departure",
            Placeholder = Strings.Routes_PassageViewTime,
        };
        RequiredUkc = new RouteFieldViewModel(Strings.Routes_PassageRequiredUkc, t => CommitValue(RoutePassageField.RequiredUkc, PassageQuantity.Depth, t)) { AutomationId = "Routes.Passage.RequiredUkc" };
        CrossTrack = new RouteFieldViewModel(Strings.Routes_PassageCrossTrack, CommitCrossTrack) { AutomationId = "Routes.Passage.CrossTrack" };

        VesselName = new RouteFieldViewModel(Strings.Routes_VesselName, t => CommitVessel(t, (v, s) => v.Name = s)) { AutomationId = "Routes.Vessel.Name" };
        VesselMmsi = new RouteFieldViewModel(Strings.Routes_VesselMmsi, t => CommitVessel(t, (v, s) => v.Mmsi = s)) { AutomationId = "Routes.Vessel.Mmsi" };
        VesselImo = new RouteFieldViewModel(Strings.Routes_VesselImo, t => CommitVessel(t, (v, s) => v.Imo = s)) { AutomationId = "Routes.Vessel.Imo" };
        VesselCallsign = new RouteFieldViewModel(Strings.Routes_VesselCallsign, t => CommitVessel(t, (v, s) => v.Callsign = s)) { AutomationId = "Routes.Vessel.Callsign" };
        VesselLength = new RouteFieldViewModel(Strings.Routes_VesselLength, t => CommitVesselLength(t, (v, d) => v.LengthMeters = d)) { AutomationId = "Routes.Vessel.Length" };
        VesselBeam = new RouteFieldViewModel(Strings.Routes_VesselBeam, t => CommitVesselLength(t, (v, d) => v.BeamMeters = d)) { AutomationId = "Routes.Vessel.Beam" };

        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    /// <summary>Draft, in the depth unit.</summary>
    public RouteFieldViewModel Draft { get; }

    /// <summary>Squat allowance, in the depth unit; shown beside the draft once one is set.</summary>
    public RouteFieldViewModel Squat { get; }

    /// <summary>Speed over ground, in knots.</summary>
    public RouteFieldViewModel Speed { get; }

    /// <summary>Planned departure; "View time" when unset.</summary>
    public RouteFieldViewModel Departure { get; }

    /// <summary>Required static UKC, in the depth unit.</summary>
    public RouteFieldViewModel RequiredUkc { get; }

    /// <summary>Cross-track limit in metres: one value for both sides, or "port / stbd".</summary>
    public RouteFieldViewModel CrossTrack { get; }

    /// <summary>Vessel name (in the vessel popover).</summary>
    public RouteFieldViewModel VesselName { get; }

    /// <summary>Vessel MMSI (in the vessel popover).</summary>
    public RouteFieldViewModel VesselMmsi { get; }

    /// <summary>Vessel IMO number (in the vessel popover).</summary>
    public RouteFieldViewModel VesselImo { get; }

    /// <summary>Vessel call sign (in the vessel popover).</summary>
    public RouteFieldViewModel VesselCallsign { get; }

    /// <summary>Vessel length, in metres (in the vessel popover).</summary>
    public RouteFieldViewModel VesselLength { get; }

    /// <summary>Vessel beam, in metres (in the vessel popover).</summary>
    public RouteFieldViewModel VesselBeam { get; }

    /// <summary>Toggles <see cref="IsExpanded"/>.</summary>
    public ICommand ToggleExpandedCommand { get; }

    private bool _isExpanded;
    /// <summary>True when the card shows its fields; false shows one summary line.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>True when the squat field shows: a squat is set, or a draft is.</summary>
    public bool ShowSquat => Draft.IsSet || Squat.IsSet;

    private string _vesselText = string.Empty;
    /// <summary>The vessel chip's text: the vessel's name (or MMSI), empty when there is none.</summary>
    public string VesselText
    {
        get => _vesselText;
        private set => SetProperty(ref _vesselText, value);
    }

    /// <summary>True when the route has a vessel with a name or identifier.</summary>
    public bool HasVessel => VesselText.Length > 0;

    private string _summary = Strings.Routes_PassagePrompt;
    /// <summary>
    /// The collapsed line: a prompt on a route with no passage values, else a
    /// summary of the values that are set.
    /// </summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>True when no passage value or vessel is set.</summary>
    public bool IsEmpty { get; private set; } = true;

    /// <summary>
    /// Shows the active route's values. Call after any route change; the
    /// fields are updated in place.
    /// </summary>
    public void Refresh()
    {
        var route = _route();
        var passage = route?.Passage;
        var unit = _depthUnit();

        Draft.Show(PassageFormatting.FormatOrNull(passage?.DraftMeters, PassageQuantity.Depth, unit));
        Squat.Show(PassageFormatting.FormatOrNull(passage?.SquatMeters, PassageQuantity.Depth, unit));
        Speed.Show(PassageFormatting.FormatOrNull(passage?.SpeedKnots, PassageQuantity.Speed, unit));
        Departure.Show(passage?.Departure is { } d ? PassageFormatting.FormatDeparture(d, _timeFormat()) : null);
        RequiredUkc.Show(PassageFormatting.FormatOrNull(passage?.RequiredUkcMeters, PassageQuantity.Depth, unit));
        CrossTrack.Show(FormatCrossTrack(passage?.PortCrossTrackMeters, passage?.StarboardCrossTrackMeters));

        var vessel = route?.Info.Vessel;
        VesselName.Show(vessel?.Name);
        VesselMmsi.Show(vessel?.Mmsi);
        VesselImo.Show(vessel?.Imo);
        VesselCallsign.Show(vessel?.Callsign);
        VesselLength.Show(PassageFormatting.FormatOrNull(vessel?.LengthMeters, PassageQuantity.Distance, unit));
        VesselBeam.Show(PassageFormatting.FormatOrNull(vessel?.BeamMeters, PassageQuantity.Distance, unit));
        VesselText = vessel?.Name ?? vessel?.Mmsi ?? vessel?.Imo ?? vessel?.Callsign ?? string.Empty;

        IsEmpty = (passage?.IsEmpty ?? true) && VesselText.Length == 0;
        Summary = IsEmpty ? Strings.Routes_PassagePrompt : BuildSummary();
        OnPropertyChanged(nameof(ShowSquat));
        OnPropertyChanged(nameof(HasVessel));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Formats a cross-track pair: one value when both sides match, else
    /// "port / stbd m", or <c>null</c> when neither is set.
    /// </summary>
    internal static string? FormatCrossTrack(double? port, double? starboard)
    {
        if (port is null && starboard is null)
            return null;
        if (port == starboard)
            return PassageFormatting.Format(port!.Value, PassageQuantity.Distance, DepthUnit.Metres);
        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.Routes_CrossTrackPairFormat,
            port is { } p ? PassageFormatting.Number(p, PassageQuantity.Distance, DepthUnit.Metres) : "–",
            starboard is { } s ? PassageFormatting.Number(s, PassageQuantity.Distance, DepthUnit.Metres) : "–");
    }

    /// <summary>
    /// Parses a cross-track entry: one value for both sides, or "port / stbd".
    /// Empty unsets both.
    /// </summary>
    internal static bool TryParseCrossTrack(string? text, out double? port, out double? starboard)
    {
        port = starboard = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        var parts = text.Split('/');
        if (parts.Length > 2)
            return false;
        if (!PassageFormatting.TryParse(parts[0], PassageQuantity.Distance, DepthUnit.Metres, out port))
            return false;
        if (parts.Length == 1)
        {
            starboard = port;
            return true;
        }
        return PassageFormatting.TryParse(parts[1], PassageQuantity.Distance, DepthUnit.Metres, out starboard);
    }

    private string BuildSummary()
    {
        var route = _route()!;
        var p = route.Passage;
        var unit = _depthUnit();
        var parts = new List<string>();
        if (p.DraftMeters is { } draft)
        {
            parts.Add(p.SquatMeters is { } squat
                ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_DraftSquatFormat,
                    PassageFormatting.Format(draft, PassageQuantity.Depth, unit),
                    PassageFormatting.Format(squat, PassageQuantity.Depth, unit))
                : PassageFormatting.Format(draft, PassageQuantity.Depth, unit));
        }
        if (p.SpeedKnots is { } speed)
            parts.Add(PassageFormatting.Format(speed, PassageQuantity.Speed, unit));
        if (p.Departure is { } departure)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Routes_SummaryDepartureFormat,
                PassageFormatting.FormatDeparture(departure, _timeFormat())));
        if (p.RequiredUkcMeters is { } ukc)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Routes_SummaryUkcFormat,
                PassageFormatting.Format(ukc, PassageQuantity.Depth, unit)));
        if (FormatCrossTrack(p.PortCrossTrackMeters, p.StarboardCrossTrackMeters) is { } xtd)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Routes_SummaryCrossTrackFormat, xtd));
        if (VesselText.Length > 0)
            parts.Add(VesselText);
        return string.Join(" · ", parts);
    }

    private bool CommitValue(RoutePassageField field, PassageQuantity quantity, string? text)
    {
        if (!PassageFormatting.TryParse(text, quantity, _depthUnit(), out var value))
            return false;
        if (field == RoutePassageField.Speed && value <= 0)
            return false;
        if (_route() is { } route)
            route.History.Execute(RouteEditLabels.EditPassage, () => route.SetPassageValue(field, value));
        return true;
    }

    private bool CommitDeparture(string? text)
    {
        if (!PassageFormatting.TryParseDeparture(text, _timeFormat(), out var value))
            return false;
        if (_route() is { } route)
            route.History.Execute(RouteEditLabels.EditPassage, () => route.SetDeparture(value));
        return true;
    }

    private bool CommitCrossTrack(string? text)
    {
        if (!TryParseCrossTrack(text, out var port, out var starboard))
            return false;
        if (_route() is { } route)
        {
            route.History.Execute(RouteEditLabels.EditPassage, () =>
            {
                route.SetPassageValue(RoutePassageField.PortCrossTrack, port);
                route.SetPassageValue(RoutePassageField.StarboardCrossTrack, starboard);
            });
        }
        return true;
    }

    private bool CommitVessel(string? text, Action<RouteVesselInfo, string?> set)
    {
        EditVessel(v => set(v, text));
        return true;
    }

    private bool CommitVesselLength(string? text, Action<RouteVesselInfo, double?> set)
    {
        if (!PassageFormatting.TryParse(text, PassageQuantity.Distance, DepthUnit.Metres, out var value))
            return false;
        EditVessel(v => set(v, value));
        return true;
    }

    private void EditVessel(Action<RouteVesselInfo> edit)
    {
        if (_route() is not { } route)
            return;
        route.History.Execute(RouteEditLabels.EditRouteInfo, () =>
        {
            var vessel = route.Info.Vessel ?? new RouteVesselInfo();
            edit(vessel);
            var empty = vessel.Name is null && vessel.Mmsi is null && vessel.Imo is null
                && vessel.Callsign is null && vessel.LengthMeters is null && vessel.BeamMeters is null;
            route.Info.Vessel = empty ? null : vessel;
            route.NotifyChanged();
        });
    }
}

/// <summary>
/// The inline detail of the open leg: its passage overrides (speed, draft,
/// cross-track port and starboard, required UKC), its geometry and its note.
/// One instance is reused for whichever leg is open, so its fields keep focus
/// across the edits they make.
/// </summary>
internal sealed class RouteLegDetailViewModel : ViewModelBase
{
    private readonly Func<Route?> _route;
    private readonly Func<DepthUnit> _depthUnit;

    public RouteLegDetailViewModel(Func<Route?> route, Func<DepthUnit> depthUnit)
    {
        _route = route;
        _depthUnit = depthUnit;

        Speed = LegField(Strings.Routes_PassageSpeed, RoutePassageField.Speed, PassageQuantity.Speed, "Routes.Leg.Speed");
        Draft = LegField(Strings.Routes_LegDraft, RoutePassageField.Draft, PassageQuantity.Depth, "Routes.Leg.Draft");
        RequiredUkc = LegField(Strings.Routes_PassageRequiredUkc, RoutePassageField.RequiredUkc, PassageQuantity.Depth, "Routes.Leg.RequiredUkc");
        CrossTrack = new RouteLegFieldViewModel(Strings.Routes_LegCrossTrack, CommitCrossTrack, ResetCrossTrack) { AutomationId = "Routes.Leg.CrossTrack" };
        Note = new RouteFieldViewModel(Strings.Routes_LegNote, CommitNote) { AutomationId = "Routes.Leg.Note", Placeholder = Strings.Routes_LegNotePlaceholder };

        SetRhumbCommand = new RelayCommand(() => SetGeometry(RouteLegGeometryType.Loxodrome));
        SetGreatCircleCommand = new RelayCommand(() => SetGeometry(RouteLegGeometryType.Geodesic));
        AddNoteCommand = new RelayCommand(() => IsNoteVisible = true);
    }

    /// <summary>Zero-based index of the open leg.</summary>
    public int LegIndex { get; private set; } = -1;

    private string _title = string.Empty;
    /// <summary>The header, e.g. "Leg 6 → 7".</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _meta = string.Empty;
    /// <summary>The header's distance and bearing.</summary>
    public string Meta
    {
        get => _meta;
        private set => SetProperty(ref _meta, value);
    }

    /// <summary>Speed override, in knots.</summary>
    public RouteLegFieldViewModel Speed { get; }

    /// <summary>Draft override, in the depth unit.</summary>
    public RouteLegFieldViewModel Draft { get; }

    /// <summary>Cross-track override, "port / stbd" in metres.</summary>
    public RouteLegFieldViewModel CrossTrack { get; }

    /// <summary>Required UKC override, in the depth unit.</summary>
    public RouteLegFieldViewModel RequiredUkc { get; }

    /// <summary>The leg's note.</summary>
    public RouteFieldViewModel Note { get; }

    private bool _isGreatCircle;
    /// <summary>True when the leg is a great circle; false for a rhumb line.</summary>
    public bool IsGreatCircle
    {
        get => _isGreatCircle;
        private set
        {
            if (SetProperty(ref _isGreatCircle, value))
                OnPropertyChanged(nameof(IsRhumb));
        }
    }

    /// <summary>True when the leg is a rhumb line.</summary>
    public bool IsRhumb => !IsGreatCircle;

    private bool _isNoteVisible;
    /// <summary>True when the note field shows: the leg has a note, or "+ Note" was pressed.</summary>
    public bool IsNoteVisible
    {
        get => _isNoteVisible;
        private set => SetProperty(ref _isNoteVisible, value);
    }

    /// <summary>Makes the leg a rhumb line.</summary>
    public ICommand SetRhumbCommand { get; }

    /// <summary>Makes the leg a great circle.</summary>
    public ICommand SetGreatCircleCommand { get; }

    /// <summary>Shows the note field.</summary>
    public ICommand AddNoteCommand { get; }

    /// <summary>Shows leg <paramref name="legIndex"/>. A different leg hides an empty note field.</summary>
    public void Show(int legIndex, string title, string meta)
    {
        var route = _route();
        if (route is null || legIndex < 0 || legIndex >= route.Legs.Count)
            return;

        if (legIndex != LegIndex)
            IsNoteVisible = false;
        LegIndex = legIndex;
        Title = title;
        Meta = meta;

        var unit = _depthUnit();
        ShowField(Speed, route, RoutePassageField.Speed, PassageQuantity.Speed, unit);
        ShowField(Draft, route, RoutePassageField.Draft, PassageQuantity.Depth, unit);
        ShowField(RequiredUkc, route, RoutePassageField.RequiredUkc, PassageQuantity.Depth, unit);

        var leg = route.Legs[legIndex];
        var overridden = leg.PortCrossTrackDistanceLimitMeters is not null || leg.StarboardCrossTrackDistanceLimitMeters is not null;
        CrossTrack.Show(
            overridden
                ? RoutePassageCardViewModel.FormatCrossTrack(
                    route.GetEffectiveLegValue(legIndex, RoutePassageField.PortCrossTrack),
                    route.GetEffectiveLegValue(legIndex, RoutePassageField.StarboardCrossTrack))
                : null,
            RoutePassageCardViewModel.FormatCrossTrack(route.Passage.PortCrossTrackMeters, route.Passage.StarboardCrossTrackMeters));

        IsGreatCircle = leg.GeometryType == RouteLegGeometryType.Geodesic;
        Note.Show(leg.Note);
        if (leg.Note is not null)
            IsNoteVisible = true;
    }

    private RouteLegFieldViewModel LegField(string label, RoutePassageField field, PassageQuantity quantity, string automationId)
        => new(
            label,
            text => CommitOverride(field, quantity, text),
            () => Edit(route => route.ResetLegOverride(LegIndex, field)))
        {
            AutomationId = automationId,
        };

    private void ShowField(RouteLegFieldViewModel view, Route route, RoutePassageField field, PassageQuantity quantity, DepthUnit unit)
        => view.Show(
            PassageFormatting.FormatOrNull(route.Legs[LegIndex].GetOverride(field), quantity, unit),
            PassageFormatting.FormatOrNull(route.Passage.Get(field), quantity, unit));

    private bool CommitOverride(RoutePassageField field, PassageQuantity quantity, string? text)
    {
        if (!PassageFormatting.TryParse(text, quantity, _depthUnit(), out var value))
            return false;
        if (field == RoutePassageField.Speed && value <= 0)
            return false;
        Edit(route => route.SetLegOverride(LegIndex, field, value));
        return true;
    }

    private bool CommitCrossTrack(string? text)
    {
        if (!RoutePassageCardViewModel.TryParseCrossTrack(text, out var port, out var starboard))
            return false;
        Edit(route =>
        {
            route.SetLegOverride(LegIndex, RoutePassageField.PortCrossTrack, port);
            route.SetLegOverride(LegIndex, RoutePassageField.StarboardCrossTrack, starboard);
        });
        return true;
    }

    private void ResetCrossTrack() => Edit(route =>
    {
        route.ResetLegOverride(LegIndex, RoutePassageField.PortCrossTrack);
        route.ResetLegOverride(LegIndex, RoutePassageField.StarboardCrossTrack);
    });

    private bool CommitNote(string? text)
    {
        if (_route() is { } route && LegIndex >= 0 && LegIndex < route.Legs.Count)
        {
            var leg = route.Legs[LegIndex];
            route.History.Execute(RouteEditLabels.EditLeg, () =>
            {
                leg.Note = text;
                route.NotifyChanged();
            });
        }
        return true;
    }

    private void SetGeometry(RouteLegGeometryType geometry)
    {
        if (_route() is not { } route || LegIndex < 0 || LegIndex >= route.Legs.Count)
            return;
        var leg = route.Legs[LegIndex];
        if (leg.GeometryType == geometry)
            return;
        route.History.Execute(RouteEditLabels.EditLeg, () =>
        {
            leg.GeometryType = geometry;
            route.NotifyChanged();
        });
    }

    private void Edit(Action<Route> edit)
    {
        if (_route() is { } route && LegIndex >= 0 && LegIndex < route.Legs.Count)
            route.History.Execute(RouteEditLabels.EditLeg, () => edit(route));
    }
}
