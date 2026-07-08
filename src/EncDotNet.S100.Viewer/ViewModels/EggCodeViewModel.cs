using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model that adapts an <see cref="IceEggCode"/> (the S-411 WMO / SIGRID-3
/// ice egg-code projection) into bindable rows and annotations for the Pick
/// Report's egg-code section. The oval carries up to three ice types; special
/// values reported outside the oval (thinner fourth-class stage / partial, a
/// trace of ice of land origin, snow depth) are surfaced as scalar properties.
/// </summary>
internal sealed class EggCodeViewModel
{
    public EggCodeViewModel(IceEggCode model)
    {
        ArgumentNullException.ThrowIfNull(model);

        HasOval = model.HasOval;
        TotalConcentration = model.TotalConcentration;
        PartialConcentrations = model.PartialConcentrations;
        StagesOfDevelopment = model.StagesOfDevelopment;
        FormsOfIce = model.FormsOfIce;
        ConcentrationRowFolded = model.ConcentrationRowFolded;

        ThinnerStage = model.Annotations
            .FirstOrDefault(a => a.Role == IceEggValueRole.ThinnerStage);
        ThinnerPartial = model.Annotations
            .FirstOrDefault(a => a.Role == IceEggValueRole.ThinnerPartial);
        SnowDepth = model.Annotations
            .FirstOrDefault(a => a.Role == IceEggValueRole.SnowDepth);
        TraceOfIce = model.Annotations.Any(a => a.Role == IceEggValueRole.TraceOfIce);
    }

    /// <summary>Whether the oval is drawn (false for open water / no ice).</summary>
    public bool HasOval { get; }

    /// <summary>Top-row total concentration (<c>Ct</c>), or <c>null</c>.</summary>
    public IceEggValue? TotalConcentration { get; }

    /// <summary>Partial concentrations (<c>Ca Cb Cc</c>) shown in the oval.</summary>
    public IReadOnlyList<IceEggValue> PartialConcentrations { get; }

    /// <summary>Stages of development (<c>Sa Sb So</c>) shown in the oval.</summary>
    public IReadOnlyList<IceEggValue> StagesOfDevelopment { get; }

    /// <summary>Forms of ice / floe sizes (<c>Fa Fb Fp</c>) shown in the oval.</summary>
    public IReadOnlyList<IceEggValue> FormsOfIce { get; }

    /// <summary>True when only one ice type is present and the partial row is folded away.</summary>
    public bool ConcentrationRowFolded { get; }

    /// <summary>Stage of development of the thinner fourth class (<c>Sd</c>), reported outside the oval.</summary>
    public IceEggValue? ThinnerStage { get; }

    /// <summary>Partial concentration of the thinner fourth class, reported outside the oval.</summary>
    public IceEggValue? ThinnerPartial { get; }

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

    /// <summary>True when there is at least one value reported outside the oval.</summary>
    public bool HasAnnotations =>
        ThinnerStage is not null || ThinnerPartial is not null
        || SnowDepth is not null || TraceOfIce;

    /// <summary>
    /// Composed caption for the thinner fourth-class values reported outside
    /// the oval (e.g. <c>"Sd 95 · 4/10 (not shown in egg)"</c>), or
    /// <c>null</c> when no fourth class is present.
    /// </summary>
    public string? ThinnerSummary
    {
        get
        {
            var parts = new List<string>(2);
            if (ThinnerStage is { } stage)
                parts.Add(string.Format(Culture, Resources.Strings.Pick_EggCode_ThinnerStage, stage.Text));
            if (ThinnerPartial is { } partial)
                parts.Add(string.Format(Culture, Resources.Strings.Pick_EggCode_ThinnerPartial, partial.Text));
            if (parts.Count == 0)
                return null;
            return $"{string.Join(" · ", parts)} {Resources.Strings.Pick_EggCode_NotShown}";
        }
    }

    /// <summary>Snow-depth caption (e.g. <c>"Snow 12.5 cm"</c>), or <c>null</c>.</summary>
    public string? SnowSummary =>
        SnowDepth is { } snow
            ? string.Format(Culture, Resources.Strings.Pick_EggCode_SnowDepth, snow.Text)
            : null;

    /// <summary>Trace-of-ice caption, or <c>null</c> when no trace is flagged.</summary>
    public string? TraceSummary =>
        TraceOfIce ? Resources.Strings.Pick_EggCode_Trace : null;

    /// <summary>True when <see cref="ThinnerSummary"/> is present.</summary>
    public bool ShowThinnerSummary => ThinnerSummary is not null;

    /// <summary>True when <see cref="SnowSummary"/> is present.</summary>
    public bool ShowSnowSummary => SnowSummary is not null;

    private static System.Globalization.CultureInfo Culture =>
        Resources.Strings.Culture;
}
