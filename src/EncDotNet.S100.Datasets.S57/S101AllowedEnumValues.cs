using System.Collections.Frozen;
using System.Collections.Generic;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Lookup of allowable enumerate values per S-101 simple attribute, derived
/// from the bundled S-101 Feature Catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The IHO "S-57 to S-101 Conversion Guidance" (Edition 0.0.1, January 2021)
/// repeatedly states, against many feature classes, that "Values populated in
/// S-57 for these attributes other than the allowable values will not be
/// converted across to S-101" — pointing at S-101 DCEG clauses for the
/// per-feature allowable lists.
/// </para>
/// <para>
/// This helper enforces a strict baseline: any S-57 value mapped to an S-101
/// enumerate-typed attribute must appear in that attribute's
/// <see cref="SimpleAttribute.ListedValues"/>. The S-101 FC's listed values
/// are a superset of any per-feature DCEG drop set, so anything failing this
/// check is definitely invalid in S-101 regardless of feature class.
/// Per-feature DCEG drop sets (further restricting the allowed values for a
/// specific feature class) are out of scope here and may be layered on top
/// later.
/// </para>
/// <para>
/// Non-enumerate attributes (real, integer, text, date, …) and unknown
/// attribute codes are treated as unconstrained and pass through.
/// </para>
/// </remarks>
public sealed class S101AllowedEnumValues
{
    private readonly FrozenDictionary<string, FrozenSet<string>> _byAttributeCode;

    private static readonly Lazy<S101AllowedEnumValues> _default = new(LoadDefault);

    private S101AllowedEnumValues(FrozenDictionary<string, FrozenSet<string>> byAttributeCode)
    {
        _byAttributeCode = byAttributeCode;
    }

    /// <summary>
    /// Lazily-loaded singleton built from the S-101 Feature Catalogue
    /// embedded in <see cref="Specification"/>.
    /// </summary>
    public static S101AllowedEnumValues Default => _default.Value;

    /// <summary>
    /// Builds an instance from a parsed S-101 <see cref="FeatureCatalogue"/>.
    /// </summary>
    public static S101AllowedEnumValues FromFeatureCatalogue(FeatureCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var dict = new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sa in catalogue.SimpleAttributes)
        {
            if (!string.Equals(sa.ValueType, "enumeration", StringComparison.OrdinalIgnoreCase))
                continue;
            if (sa.ListedValues.Count == 0)
                continue;

            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var lv in sa.ListedValues)
            {
                if (!string.IsNullOrEmpty(lv.Code))
                    values.Add(lv.Code);
            }
            dict[sa.Code] = values.ToFrozenSet(StringComparer.Ordinal);
        }

        return new S101AllowedEnumValues(dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="value"/> is an allowable code
    /// for the S-101 simple attribute named <paramref name="s101AttributeCode"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> for any attribute that is not enumerate-typed or
    /// is not present in the bundled FC, so non-enum attributes pass through
    /// unchanged.
    /// </remarks>
    public bool IsAllowed(string s101AttributeCode, string value)
    {
        if (string.IsNullOrEmpty(s101AttributeCode))
            return true;
        if (!_byAttributeCode.TryGetValue(s101AttributeCode, out var allowed))
            return true; // not an enumerate attribute (or unknown) → no constraint
        return allowed.Contains(value);
    }

    /// <summary>
    /// Returns <c>true</c> if the named S-101 attribute is enumerate-typed
    /// (i.e. carries a closed list of allowable codes).
    /// </summary>
    public bool IsEnumerated(string s101AttributeCode)
        => !string.IsNullOrEmpty(s101AttributeCode)
        && _byAttributeCode.ContainsKey(s101AttributeCode);

    private static S101AllowedEnumValues LoadDefault()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException(
                "Bundled S-101 Feature Catalogue not found in EncDotNet.S100.Specifications.");
        var fc = FeatureCatalogueReader.Read(stream);
        return FromFeatureCatalogue(fc);
    }
}
