using System;
using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

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
        double? depthMetres = TryParseKnownDepthMetres(code, value);
        return new PickAttribute
        {
            Code = code,
            Name = decoder?.ResolveAttributeName(code),
            RawValue = value,
            DisplayValue = decoder?.ResolveListedValue(code, value),
            DepthMetresValue = depthMetres,
            Children = [],
        };
    }

    /// <summary>
    /// Well-known S-100 depth-typed attribute codes (and their S-57-era
    /// aliases) whose raw values are interpreted as metres below the
    /// vertical datum. The set is intentionally narrow: it covers the
    /// attributes whose semantics demand mariner-selectable depth units
    /// (S-100 Part 9 §4.2) and excludes vertical-distance attributes that
    /// represent heights, elevations, or clearances above the water.
    /// </summary>
    private static readonly HashSet<string> KnownDepthAttributeCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // S-101 (Edition 2.0.0, Feature Catalogue §SimpleAttributes).
            "buriedDepth",                "BURDEP",
            "depthRangeMinimumValue",     "DRVAL1",
            "depthRangeMaximumValue",     "DRVAL2",
            "valueOfDepthContour",        "VALDCO",
            "valueOfSounding",            "VALSOU",
            "defaultClearanceDepth",
            "soundingDatumDepth",
            // S-102 coverage pick (synthesised attributes — depth + vertical uncertainty in metres).
            "depth",
            "uncertainty",
        };

    /// <summary>
    /// Returns the metres value of <paramref name="rawValue"/> when
    /// <paramref name="code"/> is a known depth-typed attribute and the
    /// value parses as a finite real (invariant culture). Returns
    /// <c>null</c> for non-depth codes, non-numeric values, NaN/Infinity,
    /// and dataset sentinel "NoData" markers (e.g. <c>"—"</c>) so the
    /// presentation layer keeps showing the raw text untouched.
    /// </summary>
    internal static double? TryParseKnownDepthMetres(string code, string rawValue)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(rawValue))
            return null;
        if (!KnownDepthAttributeCodes.Contains(code))
            return null;
        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres))
            return null;
        if (!double.IsFinite(metres))
            return null;
        return metres;
    }

    /// <summary>
    /// Builds a depth-typed <see cref="PickAttribute"/> for a value that
    /// is already known to be metres (e.g. an S-102 coverage sample).
    /// Sets <see cref="PickAttribute.DepthMetresValue"/> so the viewer
    /// can re-format the value through the active <see cref="DepthUnit"/>
    /// without re-parsing <see cref="PickAttribute.RawValue"/>.
    /// </summary>
    public static PickAttribute BuildDepthLeaf(string code, string name, double metres)
    {
        var raw = metres.ToString("0.##########", CultureInfo.InvariantCulture);
        return new PickAttribute
        {
            Code = code,
            Name = name,
            RawValue = raw,
            DisplayValue = DepthFormatting.Format(metres, DepthUnit.Metres),
            DepthMetresValue = metres,
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
