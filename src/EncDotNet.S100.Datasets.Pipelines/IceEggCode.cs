using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Semantic role of a single value shown in a WMO / SIGRID-3 ice "egg code"
/// diagram (S-411 Edition 1.2.1 Annex A — sea-ice / lake-ice concentration,
/// stage of development, and form of ice). The role tells a renderer where the
/// value belongs in the oval (or outside it) and lets object-information UIs
/// attach the correct hover definition without re-deriving it from position.
/// </summary>
public enum IceEggValueRole
{
    /// <summary>Total concentration (<c>Ct</c>), shown on the top row of the oval.</summary>
    TotalConcentration,

    /// <summary>Partial concentration of an ice type (<c>Ca</c>/<c>Cb</c>/<c>Cc</c>), second row.</summary>
    PartialConcentration,

    /// <summary>Stage of development of an ice type (<c>Sa</c>/<c>Sb</c>/<c>Sc</c>), third row.</summary>
    StageOfDevelopment,

    /// <summary>Form of ice / floe size of an ice type (<c>Fa</c>/<c>Fb</c>/<c>Fc</c>), bottom row.</summary>
    FormOfIce,

    /// <summary>A trace of ice of land origin, flagged outside the oval (dots).</summary>
    TraceOfIce,

    /// <summary>Snow depth (centimetres), reported beside the egg.</summary>
    SnowDepth,
}

/// <summary>
/// A single value cell in an <see cref="IceEggCode"/> diagram, carrying the raw
/// display token, its semantic <see cref="Role"/>, and the source attribute
/// code it was derived from (for hover definitions).
/// </summary>
public sealed record IceEggValue
{
    /// <summary>The raw display token exactly as it should appear (e.g. <c>"70"</c>, <c>"9+"</c>, <c>"X"</c>, <c>"4-6"</c>).</summary>
    public required string Text { get; init; }

    /// <summary>Where this value belongs in the egg diagram.</summary>
    public required IceEggValueRole Role { get; init; }

    /// <summary>
    /// The WMO / SIGRID-3 positional symbol for this value (e.g. <c>"Ct"</c>,
    /// <c>"Ca"</c>/<c>"Cb"</c>/<c>"Cc"</c>, <c>"Sa"</c>/<c>"Sb"</c>/<c>"Sc"</c>,
    /// <c>"Fa"</c>/<c>"Fb"</c>/<c>"Fc"</c>, and <c>"Cd"</c>/<c>"Sd"</c>/<c>"Fd"</c>
    /// and <c>"Ce"</c>/<c>"Se"</c>/<c>"Fe"</c> for the thinner fourth / fifth
    /// classes reported outside the oval), or <c>null</c> for values without a
    /// positional symbol (S-411 Edition 1.2.1 Annex A; WMO No. 259).
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>
    /// The S-411 source attribute code the value came from (e.g. <c>"iceact"</c>,
    /// <c>"iceapc"</c>, <c>"icesod"</c>, <c>"iceflz"</c>, <c>"snowDepth"</c>), or
    /// <c>null</c> when it is a purely derived marker (e.g. a trace flag).
    /// </summary>
    public string? SourceCode { get; init; }

    /// <summary>
    /// The prose definition of this value resolved from the S-411 Feature
    /// Catalogue enumeration (e.g. <c>"Grey Ice"</c> for a stage-of-development
    /// code), or <c>null</c> when no enumerated meaning was available.
    /// </summary>
    public string? Definition { get; init; }
}

/// <summary>
/// A render-ready projection of an S-411 sea-ice / lake-ice feature's WMO /
/// SIGRID-3 "egg code": the up-to-four stacked rows of the oval plus the
/// special values that convention reports <em>outside</em> the oval.
/// </summary>
/// <remarks>
/// <para>
/// The egg carries at most three ice types in the oval, ordered by decreasing
/// thickness. Thinner fourth / fifth classes (when present) are not drawn
/// inside the oval; each row's fourth / fifth stage, partial concentration and
/// floe size surface through <see cref="TrailingStagesOfDevelopment"/>,
/// <see cref="TrailingPartialConcentrations"/> and
/// <see cref="TrailingFormsOfIce"/> instead — rendered outside the oval to the
/// right of their row (S-411 Edition 1.2.1 Annex A; WMO No. 259 / SIGRID-3
/// egg-code conventions).
/// </para>
/// <para>
/// Special cases the model represents: a single ice type folds the
/// partial-concentration row away (<see cref="ConcentrationRowFolded"/>);
/// undetermined values pass through verbatim (e.g. <c>"9+"</c>, <c>"X"</c>,
/// ranges); and open water / no ice omits the oval entirely
/// (<see cref="HasOval"/> is <see langword="false"/> with only a
/// <see cref="TotalConcentration"/> of <c>"0"</c>).
/// </para>
/// </remarks>
public sealed record IceEggCode
{
    /// <summary>
    /// When <see langword="false"/> the oval is omitted entirely and only the
    /// <see cref="TotalConcentration"/> is shown — the WMO convention for open
    /// water / ice-free or radar-only observations.
    /// </summary>
    public bool HasOval { get; init; } = true;

    /// <summary>The top-row total concentration (<c>Ct</c>), or <c>null</c> when the source carried none.</summary>
    public IceEggValue? TotalConcentration { get; init; }

    /// <summary>
    /// Partial concentrations (<c>Ca Cb Cc</c>), at most three. Empty when the
    /// concentration row is folded for a single ice type
    /// (<see cref="ConcentrationRowFolded"/>) or when the source carried none.
    /// </summary>
    public ImmutableArray<IceEggValue> PartialConcentrations { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>Stages of development (<c>Sa Sb Sc</c>), at most three.</summary>
    public ImmutableArray<IceEggValue> StagesOfDevelopment { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>Forms of ice / floe sizes (<c>Fa Fb Fc</c>), at most three.</summary>
    public ImmutableArray<IceEggValue> FormsOfIce { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>
    /// Partial concentrations of the thinner fourth / fifth ice classes
    /// (<c>Cd Ce</c>) that do not fit the oval. Rendered outside the oval to the
    /// right of the <see cref="PartialConcentrations"/> row (S-411 Ed 1.2.1
    /// Annex A; WMO No. 259 egg-code convention).
    /// </summary>
    public ImmutableArray<IceEggValue> TrailingPartialConcentrations { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>
    /// Stages of development of the thinner fourth / fifth ice classes
    /// (<c>Sd Se</c>), rendered outside the oval to the right of the
    /// <see cref="StagesOfDevelopment"/> row.
    /// </summary>
    public ImmutableArray<IceEggValue> TrailingStagesOfDevelopment { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>
    /// Forms of ice / floe sizes of the thinner fourth / fifth ice classes
    /// (<c>Fd Fe</c>), rendered outside the oval to the right of the
    /// <see cref="FormsOfIce"/> row.
    /// </summary>
    public ImmutableArray<IceEggValue> TrailingFormsOfIce { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>
    /// Values reported outside the oval by convention: a trace of ice of land
    /// origin and snow depth. Ordered for display below the egg.
    /// </summary>
    public ImmutableArray<IceEggValue> Annotations { get; init; } = ImmutableArray<IceEggValue>.Empty;

    /// <summary>
    /// <see langword="true"/> when only a single ice type is present and the
    /// partial-concentration row has been folded away (it would merely repeat
    /// <see cref="TotalConcentration"/>).
    /// </summary>
    public bool ConcentrationRowFolded { get; init; }

    /// <summary>
    /// <see langword="true"/> when the egg carries no drawable value at all —
    /// used by the projection to elide an empty bundle.
    /// </summary>
    public bool IsEmpty =>
        TotalConcentration is null
        && PartialConcentrations.IsEmpty
        && StagesOfDevelopment.IsEmpty
        && FormsOfIce.IsEmpty
        && TrailingPartialConcentrations.IsEmpty
        && TrailingStagesOfDevelopment.IsEmpty
        && TrailingFormsOfIce.IsEmpty
        && Annotations.IsEmpty;
}
