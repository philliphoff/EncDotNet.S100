using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// The picked feature an entry was opened from (symbol dictionary design
/// note §4.4, R13b, #1721): Open in reference and F1 pass it, so a feature
/// type's variant table rings the feature's cell and an attribute entry can
/// show the feature's own value.
/// </summary>
/// <param name="FeatureType">The pick's feature type code: an S-101 code, or an S-57 acronym.</param>
/// <param name="Attributes">
/// The pick's attributes (S-101 codes, or S-57 acronyms with an alias); or
/// <see langword="null"/> for a feature type alone, not a pick (an
/// attribute opened from a feature type entry that wasn't opened from one).
/// </param>
/// <param name="Geometry">The feature's geometry, or <see cref="S100GeometryType.None"/> when unknown.</param>
internal sealed record ReferenceFeature(string FeatureType, IReadOnlyList<PickAttribute>? Attributes, S100GeometryType Geometry)
{
    /// <summary>Whether this is a picked feature, with values, rather than a feature type alone.</summary>
    public bool IsPick => Attributes is not null;

    /// <summary>A feature type alone, without a pick's values.</summary>
    /// <param name="featureType">The feature type code.</param>
    /// <returns>The context.</returns>
    public static ReferenceFeature TypeOnly(string featureType) => new(featureType, null, S100GeometryType.None);

    /// <summary>The feature a pick names.</summary>
    /// <param name="hit">The pick.</param>
    /// <returns>The feature.</returns>
    public static ReferenceFeature Of(PickHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new ReferenceFeature(hit.FeatureType, hit.Attributes, hit.Geometry);
    }
}
