using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Addresses one entry of a <see cref="ChartReferenceIndex"/>: a product
/// specification, the kind of entry and its catalogue code.
/// </summary>
/// <param name="Spec">The product specification the entry belongs to.</param>
/// <param name="Kind">The kind of entry.</param>
/// <param name="Code">
/// The entry's code: a feature type or attribute code, a portrayal item id,
/// an item family prefix such as <c>SOUNDG</c>, or a context parameter id.
/// </param>
public readonly record struct ReferenceKey(SpecRef Spec, ReferenceKind Kind, string Code)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Spec.Name}/{Kind}/{Code}";
}
