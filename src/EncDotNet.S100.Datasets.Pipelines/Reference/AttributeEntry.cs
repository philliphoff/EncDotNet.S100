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

    /// <summary>
    /// The listed values <paramref name="value"/> names: one per code of a
    /// list such as <c>3,1</c> (red, white), matched by code.
    /// </summary>
    /// <param name="value">The value as encoded.</param>
    /// <returns>
    /// The listed values, in order; <see langword="null"/> when the attribute
    /// has none, the value is blank, or a code isn't one of them.
    /// </returns>
    public IReadOnlyList<ListedValue>? ListedValuesOf(string? value)
    {
        if (ListedValues.Count == 0 || string.IsNullOrWhiteSpace(value))
            return null;
        var values = new List<ListedValue>();
        foreach (var code in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ListedValues.FirstOrDefault(v => v.Code == code) is not { } listed)
                return null;
            values.Add(listed);
        }

        return values.Count == 0 ? null : values;
    }
}
