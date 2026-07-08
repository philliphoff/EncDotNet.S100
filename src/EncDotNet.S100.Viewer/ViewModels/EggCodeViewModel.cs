using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model that adapts an <see cref="IceEggCode"/> (the S-411 WMO / SIGRID-3
/// ice egg-code projection) into bindable rows and annotations for the Pick
/// Report side panel. The oval carries up to three ice types; thinner fourth /
/// fifth classes are surfaced as trailing rows rendered outside the oval, and
/// other special values (a trace of ice of land origin, snow depth) as scalar
/// properties.
/// </summary>
internal sealed class EggCodeViewModel : INotifyPropertyChanged
{
    public EggCodeViewModel(IceEggCode model)
    {
        ArgumentNullException.ThrowIfNull(model);

        HasOval = model.HasOval;
        TotalConcentration = model.TotalConcentration;
        PartialConcentrations = model.PartialConcentrations;
        StagesOfDevelopment = model.StagesOfDevelopment;
        FormsOfIce = model.FormsOfIce;
        TrailingPartialConcentrations = model.TrailingPartialConcentrations;
        TrailingStagesOfDevelopment = model.TrailingStagesOfDevelopment;
        TrailingFormsOfIce = model.TrailingFormsOfIce;
        ConcentrationRowFolded = model.ConcentrationRowFolded;

        SnowDepth = model.Annotations
            .FirstOrDefault(a => a.Role == IceEggValueRole.SnowDepth);
        TraceOfIce = model.Annotations.Any(a => a.Role == IceEggValueRole.TraceOfIce);
    }

    /// <summary>Whether the oval is drawn (false for open water / no ice).</summary>
    public bool HasOval { get; }

    private string? _hoveredDescription;

    /// <summary>
    /// Prose meaning of the egg-code value the pointer is currently over, shown
    /// in the description region below the egg. <c>null</c> when the pointer is
    /// not over any value. Presented below the egg (rather than as a per-cell
    /// tooltip) so the mariner can read a value's meaning without the tooltip
    /// obscuring neighbouring values they may want to compare against.
    /// </summary>
    public string? HoveredDescription
    {
        get => _hoveredDescription;
        set
        {
            if (_hoveredDescription == value)
                return;
            _hoveredDescription = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HoveredDescriptionDisplay));
            OnPropertyChanged(nameof(IsHoveringValue));
        }
    }

    /// <summary>True when the pointer is over an egg-code value.</summary>
    public bool IsHoveringValue => _hoveredDescription is not null;

    /// <summary>
    /// Text for the description region: the hovered value's meaning, or the
    /// hover hint when no value is under the pointer.
    /// </summary>
    public string HoveredDescriptionDisplay =>
        _hoveredDescription ?? Resources.Strings.Pick_EggCode_HoverHint;

    /// <summary>Top-row total concentration (<c>Ct</c>), or <c>null</c>.</summary>
    public IceEggValue? TotalConcentration { get; }

    /// <summary>Partial concentrations (<c>Ca Cb Cc</c>) shown in the oval.</summary>
    public IReadOnlyList<IceEggValue> PartialConcentrations { get; }

    /// <summary>Stages of development (<c>Sa Sb Sc</c>) shown in the oval.</summary>
    public IReadOnlyList<IceEggValue> StagesOfDevelopment { get; }

    /// <summary>Forms of ice / floe sizes (<c>Fa Fb Fc</c>) shown in the oval.</summary>
    public IReadOnlyList<IceEggValue> FormsOfIce { get; }

    /// <summary>
    /// Partial concentrations of the thinner fourth / fifth classes
    /// (<c>Cd Ce</c>), rendered outside the oval to the right of the partial row.
    /// </summary>
    public IReadOnlyList<IceEggValue> TrailingPartialConcentrations { get; }

    /// <summary>
    /// Stages of development of the thinner fourth / fifth classes
    /// (<c>Sd Se</c>), rendered outside the oval to the right of the stage row.
    /// </summary>
    public IReadOnlyList<IceEggValue> TrailingStagesOfDevelopment { get; }

    /// <summary>
    /// Forms of ice / floe sizes of the thinner fourth / fifth classes
    /// (<c>Fd Fe</c>), rendered outside the oval to the right of the form row.
    /// </summary>
    public IReadOnlyList<IceEggValue> TrailingFormsOfIce { get; }

    /// <summary>True when only one ice type is present and the partial row is folded away.</summary>
    public bool ConcentrationRowFolded { get; }

    /// <summary>Snow depth (centimetres), reported outside the oval.</summary>
    public IceEggValue? SnowDepth { get; }

    /// <summary>Whether a trace of ice of land origin is flagged outside the oval.</summary>
    public bool TraceOfIce { get; }

    /// <summary>True when a top-row total concentration is present.</summary>
    public bool ShowTotalConcentration => TotalConcentration is not null;

    /// <summary>True when the partial-concentration row should be shown.</summary>
    public bool ShowPartialConcentrations => PartialConcentrations.Count > 0;

    /// <summary>True when the stage-of-development row should be shown.</summary>
    public bool ShowStagesOfDevelopment => StagesOfDevelopment.Count > 0;

    /// <summary>True when the form-of-ice row should be shown.</summary>
    public bool ShowFormsOfIce => FormsOfIce.Count > 0;

    /// <summary>True when trailing partial concentrations (<c>Cd Ce</c>) are present.</summary>
    public bool ShowTrailingPartialConcentrations => TrailingPartialConcentrations.Count > 0;

    /// <summary>True when trailing stages of development (<c>Sd Se</c>) are present.</summary>
    public bool ShowTrailingStagesOfDevelopment => TrailingStagesOfDevelopment.Count > 0;

    /// <summary>True when trailing forms of ice (<c>Fd Fe</c>) are present.</summary>
    public bool ShowTrailingFormsOfIce => TrailingFormsOfIce.Count > 0;

    // The oval outline caps its first and last visible rows with half-ellipse
    // domes and draws straight vertical sides only on the interior rows between
    // them. Because any of the partial / stage / form rows may be absent, the
    // bottom cap must attach to whichever row is bottom-most rather than being
    // pinned to the form row (which would vanish when floe sizes are omitted).

    /// <summary>True when the total-concentration row is the only row (both first and last).</summary>
    public bool TotalConcentrationIsLast =>
        ShowTotalConcentration && !ShowPartialConcentrations &&
        !ShowStagesOfDevelopment && !ShowFormsOfIce;

    /// <summary>True when the partial-concentration row is the bottom-most row shown.</summary>
    public bool PartialConcentrationsIsLast =>
        ShowPartialConcentrations && !ShowStagesOfDevelopment && !ShowFormsOfIce;

    /// <summary>True when the stage-of-development row is the bottom-most row shown.</summary>
    public bool StagesOfDevelopmentIsLast =>
        ShowStagesOfDevelopment && !ShowFormsOfIce;

    /// <summary>True when the form-of-ice row is the bottom-most row shown.</summary>
    public bool FormsOfIceIsLast => ShowFormsOfIce;

    /// <summary>True when the partial row is an interior row (straight vertical sides).</summary>
    public bool ShowPartialConcentrationsAsMiddle =>
        ShowPartialConcentrations && !PartialConcentrationsIsLast;

    /// <summary>True when the stage row is an interior row (straight vertical sides).</summary>
    public bool ShowStagesOfDevelopmentAsMiddle =>
        ShowStagesOfDevelopment && !StagesOfDevelopmentIsLast;

    /// <summary>Minimum height for the partial row cell (tall enough for a dome when it is the last row).</summary>
    public double PartialConcentrationsCellMinHeight => PartialConcentrationsIsLast ? 42d : 0d;

    /// <summary>Minimum height for the stage row cell (tall enough for a dome when it is the last row).</summary>
    public double StagesOfDevelopmentCellMinHeight => StagesOfDevelopmentIsLast ? 42d : 0d;

    /// <summary>True when there is at least one value reported outside the oval.</summary>
    public bool HasAnnotations =>
        SnowDepth is not null || TraceOfIce;

    /// <summary>Snow-depth caption (e.g. <c>"Snow 12.5 cm"</c>), or <c>null</c>.</summary>
    public string? SnowSummary =>
        SnowDepth is { } snow
            ? string.Format(Culture, Resources.Strings.Pick_EggCode_SnowDepth, snow.Text)
            : null;

    /// <summary>Trace-of-ice caption, or <c>null</c> when no trace is flagged.</summary>
    public string? TraceSummary =>
        TraceOfIce ? Resources.Strings.Pick_EggCode_Trace : null;

    /// <summary>True when <see cref="SnowSummary"/> is present.</summary>
    public bool ShowSnowSummary => SnowSummary is not null;

    private static System.Globalization.CultureInfo Culture =>
        Resources.Strings.Culture;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
