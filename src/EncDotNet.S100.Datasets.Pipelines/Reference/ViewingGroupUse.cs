namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A viewing group a feature type's rule sets, with the geometry or case
/// that sets it (design note D6).
/// </summary>
public sealed class ViewingGroupUse : RuleUse
{
    /// <summary>The viewing group and its display category.</summary>
    public required ViewingGroupInfo ViewingGroup { get; init; }
}
