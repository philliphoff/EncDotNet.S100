using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A simple attribute of the feature catalogue, with its listed values.
/// </summary>
public sealed class AttributeEntry : ReferenceEntry
{
    /// <summary>The S-57 acronym (the feature catalogue's alias), such as <c>COLOUR</c>.</summary>
    public string? Alias { get; init; }

    /// <summary>The value type, such as <c>enumeration</c> or <c>real</c>.</summary>
    public required string ValueType { get; init; }

    /// <summary>The listed values, with their labels, codes and definitions.</summary>
    public IReadOnlyList<ListedValue> ListedValues { get; init; } = [];

    /// <summary>
    /// The codes of the feature types whose rules test the attribute, so
    /// that its value changes how they are drawn.
    /// </summary>
    public IReadOnlyList<string> ConditionedFeatureTypes { get; init; } = [];
}
