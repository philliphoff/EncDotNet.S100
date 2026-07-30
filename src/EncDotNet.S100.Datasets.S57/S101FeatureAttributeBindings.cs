using System.Collections.Frozen;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Lookup of which S-101 feature classes directly bind a given (complex or
/// simple) attribute, derived from the attribute bindings declared on each
/// feature type in the bundled S-101 Feature Catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The S-57 → S-101 translation assembles several S-101 <em>complex</em>
/// attributes (for example <c>rhythmOfLight</c> and the date-range complexes
/// <c>fixedDateRange</c> / <c>periodicDateRange</c> / <c>surveyDateRange</c>)
/// from flat S-57 attributes. A complex attribute may only be emitted on a
/// feature whose Feature Catalogue entry actually binds it; emitting it on any
/// other feature would be non-conformant. Because the same S-57 attribute pair
/// (e.g. <c>PERSTA</c>/<c>PEREND</c>) is only meaningful where the destination
/// complex is bound, this lookup gates the assembly on the resolved S-101
/// feature class.
/// </para>
/// <para>
/// Only <em>direct</em> attribute bindings are indexed. The bundled S-101 FC
/// denormalises bindings onto concrete feature types (for example
/// <c>rhythmOfLight</c> is listed on <c>LightAllAround</c> rather than on an
/// abstract light super-type), so super-type inheritance does not need to be
/// resolved here.
/// </para>
/// </remarks>
public sealed class S101FeatureAttributeBindings
{
    private readonly FrozenDictionary<string, FrozenSet<string>> _featureCodesByAttribute;

    private static readonly Lazy<S101FeatureAttributeBindings> LazyDefault = new(LoadDefault);

    private S101FeatureAttributeBindings(FrozenDictionary<string, FrozenSet<string>> featureCodesByAttribute)
    {
        _featureCodesByAttribute = featureCodesByAttribute;
    }

    /// <summary>
    /// Lazily-loaded singleton built from the S-101 Feature Catalogue embedded
    /// in <see cref="Specification"/>.
    /// </summary>
    public static S101FeatureAttributeBindings Default => LazyDefault.Value;

    /// <summary>
    /// Builds an instance from a parsed S-101 <see cref="FeatureCatalogue"/>.
    /// </summary>
    public static S101FeatureAttributeBindings FromFeatureCatalogue(FeatureCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var byAttribute = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ft in catalogue.FeatureTypes)
        {
            if (string.IsNullOrEmpty(ft.Code))
                continue;

            foreach (var binding in ft.AttributeBindings)
            {
                if (string.IsNullOrEmpty(binding.AttributeRef))
                    continue;

                if (!byAttribute.TryGetValue(binding.AttributeRef, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    byAttribute[binding.AttributeRef] = set;
                }

                set.Add(ft.Code);
            }
        }

        var frozen = byAttribute.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToFrozenSet(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        return new S101FeatureAttributeBindings(frozen);
    }

    /// <summary>
    /// Returns <c>true</c> if the S-101 feature class named
    /// <paramref name="featureCode"/> directly binds the attribute named
    /// <paramref name="attributeCode"/> in the Feature Catalogue.
    /// </summary>
    public bool Binds(string? featureCode, string attributeCode)
    {
        if (string.IsNullOrEmpty(featureCode) || string.IsNullOrEmpty(attributeCode))
            return false;
        return _featureCodesByAttribute.TryGetValue(attributeCode, out var features)
            && features.Contains(featureCode);
    }

    private static S101FeatureAttributeBindings LoadDefault()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException(
                "Bundled S-101 Feature Catalogue not found in EncDotNet.S100.Specifications.");
        var fc = FeatureCatalogueReader.Read(stream);
        return FromFeatureCatalogue(fc);
    }
}
