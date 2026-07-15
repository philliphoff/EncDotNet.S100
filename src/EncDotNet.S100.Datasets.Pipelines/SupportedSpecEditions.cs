using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The set of product specification editions this application supports for each
/// S-100 product, keyed by canonical spec name (<c>"S-NNN"</c>).
/// </summary>
/// <remarks>
/// <para>
/// Used to assess a dataset's declared edition (see
/// <see cref="SpecVersionAssessment"/>): the declared edition is compared
/// against the supported edition sharing its major component. These values
/// track the editions the reader/portrayal code in this repository targets —
/// <em>not</em> the bundled Feature/Portrayal Catalogue version numbers,
/// which advance on a separate cadence (S-100 Edition 5.2.1 Part 2 §6).
/// </para>
/// <para>
/// A product appears with more than one edition when its reader handles
/// multiple (e.g. S-102 reads both edition 2.1 and 3.0).
/// </para>
/// </remarks>
public static class SupportedSpecEditions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SpecVersion>> _byName =
        new Dictionary<string, IReadOnlyList<SpecVersion>>(StringComparer.Ordinal)
        {
            // The bundled S-101 Feature and Portrayal Catalogues are Edition
            // 2.0.0; legacy 1.x datasets are still read (their pre-2.0.0
            // feature class names are mapped to 2.0.0 equivalents — see
            // S101LegacyFeatureNames), so both editions are declared supported
            // to avoid a spurious version warning on either.
            ["S-101"] = [new SpecVersion(1, 2, 0), new SpecVersion(2, 0, 0)],
            ["S-102"] = [new SpecVersion(2, 1, 0), new SpecVersion(3, 0, 0)],
            ["S-104"] = [new SpecVersion(2, 0, 0)],
            ["S-111"] = [new SpecVersion(2, 0, 0)],
            ["S-122"] = [new SpecVersion(1, 0, 0)],
            ["S-124"] = [new SpecVersion(1, 0, 0)],
            ["S-125"] = [new SpecVersion(1, 0, 0)],
            ["S-127"] = [new SpecVersion(2, 0, 0)],
            ["S-128"] = [new SpecVersion(2, 0, 0)],
            ["S-129"] = [new SpecVersion(2, 0, 0)],
            ["S-131"] = [new SpecVersion(1, 0, 0)],
            ["S-201"] = [new SpecVersion(2, 0, 0)],
            ["S-411"] = [new SpecVersion(1, 2, 1)],
            ["S-421"] = [new SpecVersion(1, 0, 0)],
        };

    /// <summary>
    /// Returns the editions the application supports for <paramref name="specName"/>
    /// (canonical or tolerant form), or an empty list when the product is not
    /// registered.
    /// </summary>
    public static IReadOnlyList<SpecVersion> For(string specName)
    {
        if (!string.IsNullOrWhiteSpace(specName)
            && SpecName.TryNormalize(specName, out var canonical)
            && _byName.TryGetValue(canonical, out var editions))
        {
            return editions;
        }

        return [];
    }

    /// <summary>
    /// Builds a <see cref="SpecVersionAssessment"/> for <paramref name="declared"/>
    /// against the editions this application supports, or <c>null</c> when the
    /// product is not registered. <paramref name="catalogue"/> is carried
    /// through for informational display only.
    /// </summary>
    public static SpecVersionAssessment? Assess(SpecRef declared, CatalogueRef? catalogue = null)
        => SpecVersionAssessment.TryCreate(declared, For(declared.Name), catalogue);
}
