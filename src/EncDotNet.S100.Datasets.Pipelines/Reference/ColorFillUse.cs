namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A plain colour fill a feature type's rule draws, such as the
/// <c>ColorFill:DEPVS</c> of a shallow Depth Area (S-100 Part 9a, area
/// instructions).
/// </summary>
public sealed class ColorFillUse : RuleUse
{
    /// <summary>The colour token, such as <c>DEPVS</c>.</summary>
    public required string Token { get; init; }
}
