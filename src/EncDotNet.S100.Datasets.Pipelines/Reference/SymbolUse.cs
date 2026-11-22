namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A portrayal item (symbol, line style or area fill), or a family of
/// symbols, that a feature type's rule draws.
/// </summary>
public sealed class SymbolUse : RuleUse
{
    /// <summary>
    /// The item id, such as <c>BCNLAT21</c>, or the family prefix, such as
    /// <c>SOUNDG</c>, when <see cref="IsFamily"/> is set.
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>The kind of item: symbol, line style or area fill.</summary>
    public required ReferenceKind ItemKind { get; init; }

    /// <summary>
    /// <see langword="true"/> when the rule assembles the item's id from a
    /// prefix and a computed value, as SNDFRM04 does for <c>SOUNDG</c>
    /// digits (design note D3).
    /// </summary>
    public bool IsFamily { get; init; }
}
