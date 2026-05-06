using System.Collections.Generic;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Shared helpers for assembling <see cref="PickAttribute"/> trees from
/// the (simple-attribute dictionary + complex-attribute list) shape that
/// every GML dataset reader exposes today. Centralised here so each
/// product processor's <c>GetFeatureInfo</c> implementation stays a
/// thin adapter.
/// </summary>
public static class FeatureInfoBuilder
{
    /// <summary>
    /// Describes a complex attribute's sub-rows in shape-agnostic terms;
    /// each spec reader exposes its own concrete <c>ComplexAttribute</c>
    /// type with this same payload.
    /// </summary>
    public readonly record struct ComplexAttributeRow(
        string Code,
        IEnumerable<KeyValuePair<string, string>> SubAttributes);

    /// <summary>
    /// Builds a <see cref="PickAttribute"/> tree from raw simple/complex
    /// attribute payloads. When <paramref name="decoder"/> is non-null,
    /// each row is decorated with FC-resolved name/label; otherwise only
    /// the raw shape is populated.
    /// </summary>
    public static IReadOnlyList<PickAttribute> Build(
        IEnumerable<KeyValuePair<string, string>> simpleAttributes,
        IEnumerable<ComplexAttributeRow> complexAttributes,
        FeatureCatalogueDecoder? decoder)
    {
        var result = new List<PickAttribute>();

        foreach (var (code, value) in simpleAttributes)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            result.Add(BuildLeaf(code, value, decoder));
        }

        foreach (var complex in complexAttributes)
        {
            var children = new List<PickAttribute>();
            foreach (var (subCode, subValue) in complex.SubAttributes)
            {
                if (string.IsNullOrWhiteSpace(subValue))
                    continue;
                children.Add(BuildLeaf(subCode, subValue, decoder));
            }

            // Skip complex rows whose sub-attributes were all empty so the
            // panel doesn't render a header with no body underneath.
            if (children.Count == 0)
                continue;

            result.Add(new PickAttribute
            {
                Code = complex.Code,
                Name = decoder?.ResolveAttributeName(complex.Code),
                RawValue = string.Empty,
                DisplayValue = null,
                Children = children,
            });
        }

        return result;
    }

    private static PickAttribute BuildLeaf(string code, string value, FeatureCatalogueDecoder? decoder)
    {
        return new PickAttribute
        {
            Code = code,
            Name = decoder?.ResolveAttributeName(code),
            RawValue = value,
            DisplayValue = decoder?.ResolveListedValue(code, value),
            Children = [],
        };
    }

    /// <summary>
    /// Creates a flat <see cref="PickAttribute"/> tree from a plain
    /// dictionary of code → value pairs, used by S-101 (ISO 8211) where
    /// the source already exposes a flattened attribute bag.
    /// </summary>
    public static IReadOnlyList<PickAttribute> BuildFlat(
        IEnumerable<KeyValuePair<string, string?>> attributes,
        FeatureCatalogueDecoder? decoder)
    {
        var result = new List<PickAttribute>();
        foreach (var (code, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            result.Add(BuildLeaf(code, value!, decoder));
        }
        return result;
    }
}
