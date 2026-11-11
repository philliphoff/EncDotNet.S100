namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Stable, renderer-neutral identity for one dataset loaded into a map.
/// </summary>
/// <remarks>
/// The value is host-assigned and is used to correlate dataset state,
/// portrayal sub-layers, interoperability rules, and lifecycle operations.
/// </remarks>
public readonly record struct MapDatasetId
{
    /// <summary>
    /// Creates a dataset identifier.
    /// </summary>
    /// <param name="value">The non-empty, host-stable identifier value.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is empty or consists only of white-space
    /// characters.
    /// </exception>
    public MapDatasetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The underlying host-stable identifier value.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The identifier value, or an empty string for the default value.</returns>
    public override string ToString() => Value ?? string.Empty;
}
