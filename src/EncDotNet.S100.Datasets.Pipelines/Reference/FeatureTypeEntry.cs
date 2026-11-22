namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A feature type, with what its portrayal rule draws and when.
/// </summary>
public sealed class FeatureTypeEntry : ReferenceEntry
{
    /// <summary>The S-57 acronym (the feature catalogue's alias), such as <c>BCNLAT</c>.</summary>
    public string? Alias { get; init; }

    /// <summary>Whether the feature catalogue marks the type abstract.</summary>
    public bool IsAbstract { get; init; }

    /// <summary>The permitted geometric primitives, such as <c>point</c> and <c>surface</c>.</summary>
    public IReadOnlyList<string> PermittedPrimitives { get; init; } = [];

    /// <summary>The codes of the attributes bound to the feature type.</summary>
    public IReadOnlyList<string> AttributeCodes { get; init; } = [];

    /// <summary>
    /// The feature type's own rule file, without its extension, or
    /// <see langword="null"/> when no rule names it.
    /// </summary>
    public string? RuleFile { get; init; }

    /// <summary>The symbols, line styles, area fills and symbol families the rule draws.</summary>
    public IReadOnlyList<SymbolUse> Uses { get; init; } = [];

    /// <summary>The plain colour fills the rule draws.</summary>
    public IReadOnlyList<ColorFillUse> ColorFills { get; init; } = [];

    /// <summary>The plain lines the rule draws.</summary>
    public IReadOnlyList<PenUse> Pens { get; init; } = [];

    /// <summary>
    /// Every viewing group the rule sets, by geometry or case (design note
    /// D6). The feature type's own rule comes first; groups set by shared
    /// rules carry a <see cref="RuleUse.ViaRule"/>.
    /// </summary>
    public IReadOnlyList<ViewingGroupUse> ViewingGroups { get; init; } = [];

    /// <summary>
    /// The most visible viewing group the feature type's own rule sets:
    /// the one shown by the most restrictive display mode. Its
    /// <see cref="ViewingGroupInfo.DisplayModeId"/> is the display category
    /// and its <see cref="ViewingGroupInfo.Description"/> the Chart 1
    /// section. <see langword="null"/> when the rule sets none.
    /// </summary>
    public ViewingGroupInfo? PrimaryViewingGroup { get; init; }

    /// <summary>
    /// Whether the rule draws anything: a portrayal item, a colour fill or
    /// a plain line.
    /// </summary>
    public bool IsPortrayed => Uses.Count > 0 || ColorFills.Count > 0 || Pens.Count > 0;
}
