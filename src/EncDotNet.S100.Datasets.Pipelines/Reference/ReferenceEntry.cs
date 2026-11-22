namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// One entry of a <see cref="ChartReferenceIndex"/>: a feature type, a
/// portrayal item, an attribute or a display setting.
/// </summary>
public abstract class ReferenceEntry
{
    /// <summary>The key that addresses the entry.</summary>
    public required ReferenceKey Key { get; init; }

    /// <summary>The entry's catalogue code; the same as <see cref="ReferenceKey.Code"/>.</summary>
    public string Code => Key.Code;

    /// <summary>The entry's name as the catalogue gives it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The catalogue's definition or description of the entry, or
    /// <see langword="null"/> when it gives none.
    /// </summary>
    public string? Definition { get; init; }

    /// <inheritdoc/>
    public override string ToString() => Key.ToString();
}
