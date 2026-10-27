namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Optional capability of an <see cref="IDynamicFeatureSource"/>: describes
/// one of its features for a pick report as a title and a list of labelled,
/// formatted values with their units.
/// </summary>
/// <remarks>
/// A host's pick path checks the source for this interface. When it is
/// present the host shows <see cref="DynamicFeatureDescription.Title"/> and
/// <see cref="DynamicFeatureDescription.Properties"/> in place of its generic
/// listing of <see cref="DynamicFeature.Attributes"/>. The source knows its
/// values' meaning and units; the host doesn't have to. See
/// <c>docs/design/dynamic-source-pick.md</c>.
/// </remarks>
public interface IDynamicFeatureDescriber
{
    /// <summary>Describes <paramref name="feature"/>, one of this source's features.</summary>
    /// <param name="feature">The feature, from <see cref="IDynamicFeatureSource.CurrentFeatures"/>.</param>
    /// <param name="formatProvider">The culture to format numbers and times with; <see langword="null"/> for the invariant culture.</param>
    /// <returns>The description, or <see langword="null"/> to fall back to the host's generic listing.</returns>
    DynamicFeatureDescription? Describe(DynamicFeature feature, IFormatProvider? formatProvider = null);
}

/// <summary>The pick-report description of a <see cref="DynamicFeature"/>.</summary>
/// <param name="Title">The feature's display label, e.g. a station name.</param>
/// <param name="Properties">The feature's values, in display order.</param>
public sealed record DynamicFeatureDescription(string Title, IReadOnlyList<DynamicFeatureProperty> Properties);

/// <summary>One labelled value in a <see cref="DynamicFeatureDescription"/>.</summary>
/// <param name="Label">The value's label, e.g. <c>"Wind"</c>.</param>
/// <param name="Value">The formatted value with its unit, e.g. <c>"270° 15.6 kn (8.0 m/s)"</c>.</param>
public sealed record DynamicFeatureProperty(string Label, string Value);
