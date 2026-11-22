namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A portrayal context parameter, which the mariner sees as a display
/// setting such as Simplified symbols (S-100 Part 9 §11.4).
/// </summary>
public sealed class DisplaySettingEntry : ReferenceEntry
{
    /// <summary>The parameter type, such as <c>boolean</c> or <c>real</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The catalogue's default value.</summary>
    public required string Default { get; init; }

    /// <summary>
    /// The codes of the feature types whose rules branch on the parameter.
    /// </summary>
    public IReadOnlyList<string> ConditionedFeatureTypes { get; init; } = [];
}
