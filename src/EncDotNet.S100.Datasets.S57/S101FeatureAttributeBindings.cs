using System.Collections.Concurrent;
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
    private readonly FrozenSet<(string Feature, string Attribute)> _singleValuedBindings;
    private readonly FrozenSet<string> _featureTypeCodes;
    private readonly FrozenSet<string> _attributeCodes;
    private readonly FrozenSet<(string Feature, string Association, string InformationType)> _informationBindings;
    private readonly FrozenSet<(string Feature, string Association, string Role, string OtherFeature)> _featureAssociationBindings;

    private static readonly ConcurrentDictionary<string, Lazy<S101FeatureAttributeBindings>> BySpec =
        new(StringComparer.OrdinalIgnoreCase);

    private S101FeatureAttributeBindings(
        FrozenDictionary<string, FrozenSet<string>> featureCodesByAttribute,
        FrozenSet<(string Feature, string Attribute)> singleValuedBindings,
        FrozenSet<string> featureTypeCodes,
        FrozenSet<string> attributeCodes,
        FrozenSet<(string Feature, string Association, string InformationType)> informationBindings,
        FrozenSet<(string Feature, string Association, string Role, string OtherFeature)> featureAssociationBindings)
    {
        _featureCodesByAttribute = featureCodesByAttribute;
        _singleValuedBindings = singleValuedBindings;
        _featureTypeCodes = featureTypeCodes;
        _attributeCodes = attributeCodes;
        _informationBindings = informationBindings;
        _featureAssociationBindings = featureAssociationBindings;
    }

    /// <summary>
    /// Lazily-loaded singleton built from the S-101 Feature Catalogue embedded
    /// in <see cref="Specification"/>.
    /// </summary>
    public static S101FeatureAttributeBindings Default => ForSpec("S-101");

    /// <summary>
    /// The feature/attribute binding lookup built from the bundled Feature Catalogue of
    /// <paramref name="catalogueSpec"/> (e.g. <c>"S-101"</c> or <c>"S-401"</c>), loaded
    /// on first use and shared thereafter. Lets the S-57 translation check its
    /// output against whichever S-100 product it targets.
    /// </summary>
    /// <param name="catalogueSpec">The product whose bundled Feature Catalogue to use.</param>
    /// <returns>The shared lookup for that catalogue.</returns>
    /// <exception cref="InvalidOperationException">No Feature Catalogue is bundled for <paramref name="catalogueSpec"/>.</exception>
    public static S101FeatureAttributeBindings ForSpec(string catalogueSpec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueSpec);
        return BySpec.GetOrAdd(catalogueSpec, static spec => new(() => Load(spec))).Value;
    }

    /// <summary>
    /// Builds an instance from a parsed S-101 <see cref="FeatureCatalogue"/>.
    /// </summary>
    public static S101FeatureAttributeBindings FromFeatureCatalogue(FeatureCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var byAttribute = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var singleValued = new HashSet<(string Feature, string Attribute)>();
        var featureTypeCodes = new HashSet<string>(StringComparer.Ordinal);
        var informationBindings = new HashSet<(string Feature, string Association, string InformationType)>();
        var featureAssociationBindings = new HashSet<(string Feature, string Association, string Role, string OtherFeature)>();
        foreach (var ft in catalogue.FeatureTypes)
        {
            if (string.IsNullOrEmpty(ft.Code))
                continue;

            featureTypeCodes.Add(ft.Code);
            IndexAttributeBindings(ft.Code, ft.AttributeBindings);

            foreach (var binding in ft.InformationBindings)
            {
                foreach (var informationType in binding.InformationTypeRefs)
                    informationBindings.Add((ft.Code, binding.AssociationRef, informationType));
            }

            foreach (var binding in ft.FeatureBindings)
            {
                foreach (var other in binding.FeatureTypeRefs)
                    featureAssociationBindings.Add((ft.Code, binding.AssociationRef, binding.RoleRef, other));
            }
        }

        foreach (var it in catalogue.InformationTypes)
        {
            if (!string.IsNullOrEmpty(it.Code))
                IndexAttributeBindings(it.Code, it.AttributeBindings);
        }

        void IndexAttributeBindings(string ownerCode, IEnumerable<AttributeBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                if (string.IsNullOrEmpty(binding.AttributeRef))
                    continue;

                if (!byAttribute.TryGetValue(binding.AttributeRef, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    byAttribute[binding.AttributeRef] = set;
                }

                set.Add(ownerCode);

                var multiplicity = binding.Multiplicity;
                if (!multiplicity.IsInfinite && multiplicity.Upper is <= 1)
                    singleValued.Add((ownerCode, binding.AttributeRef));
            }
        }

        var frozen = byAttribute.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToFrozenSet(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        var attributeCodes = catalogue.SimpleAttributes.Select(a => a.Code)
            .Concat(catalogue.ComplexAttributes.Select(a => a.Code))
            .Where(code => !string.IsNullOrEmpty(code))
            .ToFrozenSet(StringComparer.Ordinal);

        return new S101FeatureAttributeBindings(
            frozen,
            singleValued.ToFrozenSet(),
            featureTypeCodes.ToFrozenSet(StringComparer.Ordinal),
            attributeCodes,
            informationBindings.ToFrozenSet(),
            featureAssociationBindings.ToFrozenSet());
    }

    /// <summary>
    /// Returns <c>true</c> if the Feature Catalogue defines a feature class
    /// named <paramref name="featureCode"/> (case-sensitive, as feature class
    /// codes are). Lets a translation skip constructs its target product lacks,
    /// such as S-101's <c>RangeSystem</c>, which S-401 does not define.
    /// </summary>
    /// <param name="featureCode">The feature class code, e.g. <c>"RangeSystem"</c>.</param>
    public bool DefinesFeatureType(string? featureCode)
        => !string.IsNullOrEmpty(featureCode) && _featureTypeCodes.Contains(featureCode);

    /// <summary>
    /// Returns <c>true</c> if the Feature Catalogue defines a simple or complex
    /// attribute named <paramref name="attributeCode"/> (case-sensitive). S-401,
    /// for instance, does not define S-101's <c>categoryOfBridge</c>.
    /// </summary>
    /// <param name="attributeCode">The attribute code, e.g. <c>"categoryOfBridge"</c>.</param>
    public bool DefinesAttribute(string? attributeCode)
        => !string.IsNullOrEmpty(attributeCode) && _attributeCodes.Contains(attributeCode);

    /// <summary>
    /// Returns <c>true</c> if the feature class (or information type) named
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

    /// <summary>
    /// Returns <c>true</c> if the S-101 feature class named
    /// <paramref name="featureCode"/> binds the attribute named
    /// <paramref name="attributeCode"/> with an upper multiplicity of at most
    /// one, so a feature instance may carry at most one occurrence of it.
    /// </summary>
    /// <param name="featureCode">S-101 feature class code (e.g. <c>Bridge</c>).</param>
    /// <param name="attributeCode">S-101 attribute code (e.g. <c>bridgeConstruction</c>).</param>
    /// <returns>
    /// <c>true</c> for a [0..1] or [1..1] binding; <c>false</c> for a
    /// multi-valued binding or when the feature does not bind the attribute.
    /// </returns>
    public bool IsSingleValued(string? featureCode, string attributeCode)
    {
        if (string.IsNullOrEmpty(featureCode) || string.IsNullOrEmpty(attributeCode))
            return false;
        return _singleValuedBindings.Contains((featureCode, attributeCode));
    }

    /// <summary>
    /// Returns <c>true</c> if the feature class named <paramref name="featureCode"/>
    /// may be linked to an instance of the information type named
    /// <paramref name="informationTypeCode"/> through the information
    /// association named <paramref name="associationCode"/>. S-401, for
    /// instance, lets <c>LockBasin</c> reach <c>TimeScheduleInGeneral</c> through
    /// <c>AdditionalInformation</c>, but not <c>Bridge</c>.
    /// </summary>
    /// <param name="featureCode">The feature class code, e.g. <c>"LockBasin"</c>.</param>
    /// <param name="associationCode">The information association code, e.g. <c>"AdditionalInformation"</c>.</param>
    /// <param name="informationTypeCode">The information type code, e.g. <c>"TimeScheduleInGeneral"</c>.</param>
    /// <returns><c>true</c> when the feature class declares that information binding.</returns>
    public bool BindsInformationType(string? featureCode, string associationCode, string informationTypeCode)
        => !string.IsNullOrEmpty(featureCode)
            && _informationBindings.Contains((featureCode, associationCode, informationTypeCode));

    /// <summary>
    /// Returns <c>true</c> if the feature class named <paramref name="featureCode"/>
    /// may be linked, through the feature association named
    /// <paramref name="associationCode"/>, to an instance of
    /// <paramref name="otherFeatureCode"/> playing the role named
    /// <paramref name="roleCode"/>. Both the S-101 and the S-401 catalogue, for
    /// instance, let a <c>Bridge</c> reach a <c>LightAllAround</c> as
    /// <c>theEquipment</c> of a <c>StructureEquipment</c> association.
    /// </summary>
    /// <param name="featureCode">The feature class carrying the binding, e.g. <c>"Bridge"</c>.</param>
    /// <param name="associationCode">The feature association code, e.g. <c>"StructureEquipment"</c>.</param>
    /// <param name="roleCode">The role the other feature plays, e.g. <c>"theEquipment"</c>.</param>
    /// <param name="otherFeatureCode">The other feature class, e.g. <c>"LightAllAround"</c>.</param>
    /// <returns><c>true</c> when the feature class declares that feature binding.</returns>
    public bool BindsFeatureAssociation(
        string? featureCode, string associationCode, string roleCode, string? otherFeatureCode)
        => !string.IsNullOrEmpty(featureCode)
            && !string.IsNullOrEmpty(otherFeatureCode)
            && _featureAssociationBindings.Contains((featureCode, associationCode, roleCode, otherFeatureCode));

    private static S101FeatureAttributeBindings Load(string catalogueSpec)
    {
        using var stream = Specification.TryOpenFeatureCatalogue(catalogueSpec)
            ?? throw new InvalidOperationException(
                $"Bundled {catalogueSpec} Feature Catalogue not found in EncDotNet.S100.Specifications.");
        var fc = FeatureCatalogueReader.Read(stream);
        return FromFeatureCatalogue(fc);
    }
}
