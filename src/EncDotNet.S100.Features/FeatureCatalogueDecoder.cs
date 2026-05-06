using System.Collections.Generic;

namespace EncDotNet.S100.Features;

/// <summary>
/// Provides human-readable resolution of S-100 Feature Catalogue codes:
/// attribute codes → names, listed-value (enumeration) codes → labels,
/// and feature-type codes → names.
/// </summary>
/// <remarks>
/// Construction is O(n) over the catalogue; lookups are O(1). Returns
/// <c>null</c> from any resolver when no match is found, leaving callers
/// free to fall back to the raw code. Intended for "pick / object info"
/// rendering where a viewer wants to show <c>"Object name"</c> instead
/// of the bare attribute code <c>"OBJNAM"</c>, or <c>"In force"</c>
/// instead of the listed-value code <c>"1"</c>. See S-100 Edition 5.2.1
/// Part 5 (Feature Catalogue, ISO 19110) for the underlying schema.
/// </remarks>
public sealed class FeatureCatalogueDecoder
{
    private readonly Dictionary<string, SimpleAttribute> _simpleAttributesByCode;
    private readonly Dictionary<string, ComplexAttribute> _complexAttributesByCode;
    private readonly Dictionary<string, FeatureType> _featureTypesByCode;
    private readonly Dictionary<string, InformationType> _informationTypesByCode;

    /// <summary>
    /// Maps "<simpleAttributeCode>|<listedValueCode>" → label so listed-value
    /// resolution stays O(1) regardless of catalogue size.
    /// </summary>
    private readonly Dictionary<string, string> _listedValueLabels;

    public FeatureCatalogueDecoder(FeatureCatalogue catalogue)
    {
        if (catalogue is null) throw new System.ArgumentNullException(nameof(catalogue));
        Catalogue = catalogue;

        _simpleAttributesByCode = new(catalogue.SimpleAttributes.Count, System.StringComparer.OrdinalIgnoreCase);
        _listedValueLabels = new(System.StringComparer.OrdinalIgnoreCase);
        foreach (var sa in catalogue.SimpleAttributes)
        {
            _simpleAttributesByCode[sa.Code] = sa;
            foreach (var lv in sa.ListedValues)
            {
                _listedValueLabels[$"{sa.Code}|{lv.Code}"] = lv.Label;
            }
        }

        _complexAttributesByCode = new(catalogue.ComplexAttributes.Count, System.StringComparer.OrdinalIgnoreCase);
        foreach (var ca in catalogue.ComplexAttributes)
            _complexAttributesByCode[ca.Code] = ca;

        _featureTypesByCode = new(catalogue.FeatureTypes.Count, System.StringComparer.OrdinalIgnoreCase);
        foreach (var ft in catalogue.FeatureTypes)
            _featureTypesByCode[ft.Code] = ft;

        _informationTypesByCode = new(catalogue.InformationTypes.Count, System.StringComparer.OrdinalIgnoreCase);
        foreach (var it in catalogue.InformationTypes)
            _informationTypesByCode[it.Code] = it;
    }

    /// <summary>The wrapped feature catalogue.</summary>
    public FeatureCatalogue Catalogue { get; }

    /// <summary>
    /// Returns the human-readable name for a simple- or complex-attribute
    /// code, or <c>null</c> if the catalogue does not define it.
    /// </summary>
    public string? ResolveAttributeName(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        if (_simpleAttributesByCode.TryGetValue(code, out var sa)) return sa.Name;
        if (_complexAttributesByCode.TryGetValue(code, out var ca)) return ca.Name;
        return null;
    }

    /// <summary>
    /// Resolves a listed-value (enumeration) code for the given simple
    /// attribute to its display label. Returns <c>null</c> when the
    /// attribute is not enumerated, the value is not a listed value, or
    /// either is unknown to the catalogue.
    /// </summary>
    /// <param name="attributeCode">The simple attribute's code (e.g. <c>"CATPIB"</c>).</param>
    /// <param name="rawValue">The raw value from the dataset (typically a numeric code).</param>
    public string? ResolveListedValue(string attributeCode, string? rawValue)
    {
        if (string.IsNullOrEmpty(attributeCode) || string.IsNullOrEmpty(rawValue))
            return null;
        return _listedValueLabels.TryGetValue($"{attributeCode}|{rawValue}", out var label)
            ? label
            : null;
    }

    /// <summary>
    /// Returns the human-readable name for a feature-type code, or
    /// <c>null</c> if the catalogue does not define it.
    /// </summary>
    public string? ResolveFeatureTypeName(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        return _featureTypesByCode.TryGetValue(code, out var ft) ? ft.Name : null;
    }

    /// <summary>
    /// Returns the human-readable name for an information-type code, or
    /// <c>null</c> if the catalogue does not define it.
    /// </summary>
    public string? ResolveInformationTypeName(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        return _informationTypesByCode.TryGetValue(code, out var it) ? it.Name : null;
    }

    /// <summary>
    /// Returns true when the simple attribute identified by <paramref name="attributeCode"/>
    /// is an enumerated type with at least one listed value.
    /// </summary>
    public bool IsEnumeratedAttribute(string attributeCode)
    {
        return !string.IsNullOrEmpty(attributeCode)
            && _simpleAttributesByCode.TryGetValue(attributeCode, out var sa)
            && sa.ListedValues.Count > 0;
    }
}
