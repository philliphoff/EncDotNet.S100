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
/// <param name="Attributes">The pick's attributes (S-101 codes, or S-57 acronyms with an alias).</param>
/// <param name="Geometry">The feature's geometry, or <see cref="S100GeometryType.None"/> when unknown.</param>
internal sealed record ReferenceFeature(string FeatureType, IReadOnlyList<PickAttribute> Attributes, S100GeometryType Geometry)
{
    /// <summary>The feature a pick names.</summary>
    /// <param name="hit">The pick.</param>
    /// <returns>The feature.</returns>
    public static ReferenceFeature Of(PickHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new ReferenceFeature(hit.FeatureType, hit.Attributes, hit.Geometry);
    }
}
