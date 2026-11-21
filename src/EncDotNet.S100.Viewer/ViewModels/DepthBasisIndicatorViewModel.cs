using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Which of the depth basis pill's whole forms is drawn (<c>docs/design/depth-basis-indicator.md</c> §2.2.1).</summary>
internal enum DepthBasisForm
{
    /// <summary>The full value, e.g. "Charted · water level not applied".</summary>
    Full,

    /// <summary>The shortened qualifier, e.g. "Charted · not applied".</summary>
    Short,

    /// <summary>The shortest value with the state's icon, e.g. "Charted".</summary>
    Compact,
}

/// <summary>
/// The status bar's depth basis pill and its flyout (#1191): what the depths
/// and safety contour on the chart are relative to. Fed by
/// <see cref="MainViewModel.UpdateDepthBasis"/>; the view picks the form that
/// fits.
/// </summary>
internal sealed class DepthBasisIndicatorViewModel : ViewModelBase
{
    private DepthBasis _basis = DepthBasis.Hidden;
    private DepthBasisForms _forms = new(string.Empty, string.Empty, string.Empty, DepthBasisTone.Neutral, false);
    private DepthBasisForm _form;
    private string _accessibleName = Resources.Strings.Label_DepthBasis;
    private string _basisLine = string.Empty;
    private string? _datumHeading;
    private IReadOnlyList<DepthBasisDatumLine> _datumLines = [];
    private IReadOnlyList<string> _sourceLines = [];
    private IReadOnlyList<string> _waterLevelLines = [];

    /// <summary>The basis shown.</summary>
    public DepthBasis Basis => _basis;

    /// <summary>True when a depth-bearing dataset is loaded and active.</summary>
    public bool IsVisible => _basis.Kind != DepthBasisKind.None;

    /// <summary>The full form: the tooltip, the flyout's first line and <c>get_depth_basis</c>'s <c>display</c>.</summary>
    public string FullText => _forms.Full + ReliefSuffix;

    /// <summary>The short form.</summary>
    public string ShortText => _forms.Short + ReliefSuffix;

    /// <summary>The compact form.</summary>
    public string CompactText => _forms.Compact + ReliefCompactSuffix;

    /// <summary>The forms, longest first, for the view to measure.</summary>
    public IReadOnlyList<string> FormTexts => [FullText, ShortText, CompactText];

    /// <summary>The form drawn; set by the view from the room the status bar has.</summary>
    public DepthBasisForm Form
    {
        get => _form;
        set
        {
            if (SetProperty(ref _form, value))
                OnPropertyChanged(nameof(DisplayedText));
        }
    }

    /// <summary>The value drawn on the pill: one whole form, never truncated.</summary>
    public string DisplayedText => _form switch
    {
        DepthBasisForm.Short => ShortText,
        DepthBasisForm.Compact => CompactText,
        _ => FullText,
    };

    /// <summary>True when the state has an icon (S-104 in view, not applied); it stays in every form.</summary>
    public bool ShowsIcon => _forms.ShowsIcon;

    /// <summary>True when the state's icon is the info icon (S-104 in view, not applied).</summary>
    public bool ShowsInfoIcon => _forms.ShowsIcon && _forms.Tone != DepthBasisTone.Caution;

    /// <summary>True for the informational accent (S-104 in view, not applied); false for neutral.</summary>
    public bool IsInfoTone => _forms.Tone == DepthBasisTone.Info;

    /// <summary>True for the adjusted accent (#1132): the chart adds water level.</summary>
    public bool IsAdjustedTone => _forms.Tone == DepthBasisTone.Adjusted;

    /// <summary>True for the caution accent (#1132): adjustment is on but doesn't apply in view.</summary>
    public bool IsCautionTone => _forms.Tone == DepthBasisTone.Caution;

    /// <summary>The accessible name, with the state, e.g. "Depth basis: Charted, water level not applied".</summary>
    public string AccessibleName
    {
        get => _accessibleName;
        private set => SetProperty(ref _accessibleName, value);
    }

    /// <summary>The flyout's line on what the chart's depths are.</summary>
    public string BasisLine
    {
        get => _basisLine;
        private set => SetProperty(ref _basisLine, value);
    }

    /// <summary>"Several datums in view" when the depth sources disagree; null otherwise.</summary>
    public string? DatumHeading
    {
        get => _datumHeading;
        private set
        {
            if (SetProperty(ref _datumHeading, value))
                OnPropertyChanged(nameof(HasDatumHeading));
        }
    }

    /// <summary>True when <see cref="DatumHeading"/> has a value.</summary>
    public bool HasDatumHeading => _datumHeading is not null;

    /// <summary>The datum lines: the datum's name, or one line per datum with its datasets.</summary>
    public IReadOnlyList<DepthBasisDatumLine> DatumLines
    {
        get => _datumLines;
        private set => SetProperty(ref _datumLines, value);
    }

    /// <summary>The depth sources in view, capped with "and N more".</summary>
    public IReadOnlyList<string> SourceLines
    {
        get => _sourceLines;
        private set => SetProperty(ref _sourceLines, value);
    }

    /// <summary>The water level section's lines.</summary>
    public IReadOnlyList<string> WaterLevelLines
    {
        get => _waterLevelLines;
        private set => SetProperty(ref _waterLevelLines, value);
    }

    /// <summary>Where the viewer applies water level today.</summary>
    public IReadOnlyList<string> WhereAppliedLines
    {
        get => _whereAppliedLines;
        private set => SetProperty(ref _whereAppliedLines, value);
    }

    private IReadOnlyList<string> _whereAppliedLines = DepthBasisText.WhereAppliedLines(chartAdjustmentOn: false);

    private bool _reliefSwitchOn;
    private string _plainAccessibleName = Resources.Strings.Label_DepthBasis;
    private Action? _turnOffRelief;

    /// <summary>
    /// True while S-102 relief shading is on and an S-102 dataset is among the
    /// depth sources in view: the pill reads "Charted · Relief" and the flyout
    /// says it's on, with Off (design note <c>s102-relief-design.md</c> §3).
    /// Relief is a display aid, not the S-102 portrayal.
    /// </summary>
    public bool IsReliefShown =>
        _reliefSwitchOn && _basis.DepthSources.Any(s => string.Equals(s.Spec, "S-102", StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns relief shading off from the flyout.</summary>
    public ICommand TurnOffReliefCommand { get; }

    /// <summary>Creates the indicator.</summary>
    public DepthBasisIndicatorViewModel()
    {
        TurnOffReliefCommand = new RelayCommand(() => _turnOffRelief?.Invoke());
        UseTidePredictionsCommand = new RelayCommand(() => _useTidePredictions?.Invoke());
        RefreshStationListCommand = new RelayCommand(() => _refreshStationList?.Invoke());
    }

    private Action? _useTidePredictions;
    private Action? _refreshStationList;

    /// <summary>
    /// Whether the flyout offers <b>Use tide predictions</b> (tide predictions
    /// design D2): they're off, no S-104 meets the view and a station is in reach.
    /// </summary>
    public bool OffersTidePredictions => _basis.OffersPredictions && _useTidePredictions is not null;

    /// <summary>Whether the flyout shows the stale station list notice (D15).</summary>
    public bool IsStationListStale => _basis.PredictionsStale && _refreshStationList is not null;

    /// <summary>"NOAA has revised the station list since 3 Mar 2026. …".</summary>
    public string StationListStaleText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Resources.Strings.Status_DepthBasisPopup_StaleFormat,
        _basis.StationListDate is { } date ? TimeFormatting.FormatDateOnly(DateOnly.FromDateTime(date.UtcDateTime)) : string.Empty);

    /// <summary>Turns tide predictions on, from the flyout's offer.</summary>
    public ICommand UseTidePredictionsCommand { get; }

    /// <summary>Downloads the station list again, from the stale notice.</summary>
    public ICommand RefreshStationListCommand { get; }

    /// <summary>Sets what the flyout's tide prediction offer and stale notice do.</summary>
    /// <param name="useTidePredictions">Turns the source switch on.</param>
    /// <param name="refreshStationList">Downloads NOAA's station list again.</param>
    public void SetTidePredictions(Action useTidePredictions, Action refreshStationList)
    {
        ArgumentNullException.ThrowIfNull(useTidePredictions);
        ArgumentNullException.ThrowIfNull(refreshStationList);
        _useTidePredictions = useTidePredictions;
        _refreshStationList = refreshStationList;
        RaiseTidePredictions();
    }

    private void RaiseTidePredictions()
    {
        OnPropertyChanged(nameof(OffersTidePredictions));
        OnPropertyChanged(nameof(IsStationListStale));
        OnPropertyChanged(nameof(StationListStaleText));
    }

    /// <summary>
    /// Shows whether the S-102 relief shading switch is on, and how the
    /// flyout's Off turns it off.
    /// </summary>
    /// <param name="on">The Display controls Relief shading switch.</param>
    /// <param name="turnOff">Turns the switch off.</param>
    public void SetRelief(bool on, Action turnOff)
    {
        ArgumentNullException.ThrowIfNull(turnOff);
        _turnOffRelief = turnOff;
        if (_reliefSwitchOn == on)
            return;
        _reliefSwitchOn = on;
        RaiseRelief();
    }

    private string ReliefSuffix => IsReliefShown ? Resources.Strings.Status_DepthBasis_ReliefSuffix : string.Empty;

    private string ReliefCompactSuffix => IsReliefShown ? Resources.Strings.Status_DepthBasis_ReliefSuffix_Compact : string.Empty;

    private void RaiseRelief()
    {
        OnPropertyChanged(nameof(IsReliefShown));
        OnPropertyChanged(nameof(FullText));
        OnPropertyChanged(nameof(ShortText));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(FormTexts));
        OnPropertyChanged(nameof(DisplayedText));
        AccessibleName = IsReliefShown
            ? _plainAccessibleName + Resources.Strings.Status_DepthBasis_ReliefAccessibleSuffix
            : _plainAccessibleName;
    }

    /// <summary>Shows a basis.</summary>
    /// <param name="basis">The basis.</param>
    /// <param name="waterLevelLoaded">True when an active S-104 dataset is loaded, in view or not.</param>
    /// <param name="format">The user's Local/UTC setting.</param>
    public void Update(DepthBasis basis, bool waterLevelLoaded, TimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var wasVisible = IsVisible;
        _basis = basis;
        _forms = DepthBasisText.Pill(basis, format);
        OnPropertyChanged(nameof(Basis));
        OnPropertyChanged(nameof(FullText));
        OnPropertyChanged(nameof(ShortText));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(FormTexts));
        OnPropertyChanged(nameof(DisplayedText));
        OnPropertyChanged(nameof(ShowsIcon));
        OnPropertyChanged(nameof(ShowsInfoIcon));
        OnPropertyChanged(nameof(IsInfoTone));
        OnPropertyChanged(nameof(IsAdjustedTone));
        OnPropertyChanged(nameof(IsCautionTone));
        WhereAppliedLines = DepthBasisText.WhereAppliedLines(basis.Kind == DepthBasisKind.Adjusted || basis.Reason is DepthBasisReason.WaterLevel or DepthBasisReason.ProductNotAdjusted);
        _plainAccessibleName = DepthBasisText.AccessibleName(basis, format);
        RaiseRelief();
        RaiseTidePredictions();

        if (basis.Kind == DepthBasisKind.None)
        {
            BasisLine = string.Empty;
            DatumHeading = null;
            DatumLines = [];
            SourceLines = [];
            WaterLevelLines = [];
        }
        else
        {
            BasisLine = DepthBasisText.BasisLine(basis, format);
            DatumHeading = DepthBasisText.DatumHeading(basis);
            DatumLines = DepthBasisText.DatumLines(basis);
            SourceLines = DepthBasisText.SourceLines(basis);
            WaterLevelLines = DepthBasisText.WaterLevelLines(basis, waterLevelLoaded, format);
        }

        if (wasVisible != IsVisible)
            OnPropertyChanged(nameof(IsVisible));
    }
}
