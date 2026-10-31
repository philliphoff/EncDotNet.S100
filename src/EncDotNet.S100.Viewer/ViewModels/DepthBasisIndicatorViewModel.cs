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
    public string FullText => _forms.Full;

    /// <summary>The short form.</summary>
    public string ShortText => _forms.Short;

    /// <summary>The compact form.</summary>
    public string CompactText => _forms.Compact;

    /// <summary>The forms, longest first, for the view to measure.</summary>
    public IReadOnlyList<string> FormTexts => [_forms.Full, _forms.Short, _forms.Compact];

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
        DepthBasisForm.Short => _forms.Short,
        DepthBasisForm.Compact => _forms.Compact,
        _ => _forms.Full,
    };

    /// <summary>True when the state has an icon (S-104 in view, not applied); it stays in every form.</summary>
    public bool ShowsIcon => _forms.ShowsIcon;

    /// <summary>True for the informational accent (S-104 in view, not applied); false for neutral.</summary>
    public bool IsInfoTone => _forms.Tone == DepthBasisTone.Info;

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
    public IReadOnlyList<string> WhereAppliedLines { get; } = DepthBasisText.WhereAppliedLines();

    /// <summary>Shows a basis.</summary>
    /// <param name="basis">The basis.</param>
    /// <param name="waterLevelLoaded">True when an active S-104 dataset is loaded, in view or not.</param>
    /// <param name="format">The user's Local/UTC setting.</param>
    public void Update(DepthBasis basis, bool waterLevelLoaded, TimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var wasVisible = IsVisible;
        _basis = basis;
        _forms = DepthBasisText.Pill(basis);
        OnPropertyChanged(nameof(Basis));
        OnPropertyChanged(nameof(FullText));
        OnPropertyChanged(nameof(ShortText));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(FormTexts));
        OnPropertyChanged(nameof(DisplayedText));
        OnPropertyChanged(nameof(ShowsIcon));
        OnPropertyChanged(nameof(IsInfoTone));
        AccessibleName = DepthBasisText.AccessibleName(basis);

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
            BasisLine = DepthBasisText.BasisLine(basis);
            DatumHeading = DepthBasisText.DatumHeading(basis);
            DatumLines = DepthBasisText.DatumLines(basis);
            SourceLines = DepthBasisText.SourceLines(basis);
            WaterLevelLines = DepthBasisText.WaterLevelLines(basis, waterLevelLoaded, format);
        }

        if (wasVisible != IsVisible)
            OnPropertyChanged(nameof(IsVisible));
    }
}
