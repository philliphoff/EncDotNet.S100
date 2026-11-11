using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Builds the S-101 (and S-401) pick report from a feature's ISO 8211 record
/// rather than the portrayal's attribute dictionary, which keeps one value per
/// code: every value of a repeated attribute gets its own row and complex
/// attributes nest their sub-attributes (S-98 15.1; S-164 FeatureInformation1).
/// The report also follows the feature's information and feature associations.
/// </summary>
internal static class S101PickReport
{
    /// <summary>
    /// The pick-report rows of a record's attributes (ATTR with ATIX/PAIX,
    /// S-100 Part 10a), codes resolved through the cell's attribute catalogue.
    /// </summary>
    /// <param name="document">The cell, for its attribute-code catalogue.</param>
    /// <param name="attributes">The feature or information record's attributes.</param>
    /// <param name="decoder">Decodes names and listed values, or <c>null</c>.</param>
    /// <returns>The top-level rows, in record order.</returns>
    public static IReadOnlyList<PickAttribute> Attributes(
        S101Document document, IReadOnlyList<S101Attribute> attributes, FeatureCatalogueDecoder? decoder)
    {
        var rows = new FeatureInfoBuilder.AttributeRow[attributes.Count];
        for (var i = 0; i < attributes.Count; i++)
        {
            var attribute = attributes[i];
            rows[i] = new FeatureInfoBuilder.AttributeRow(
                AttributeCode(document, attribute.NumericCode), attribute.Value, attribute.ParentIndex);
        }

        return FeatureInfoBuilder.BuildTree(rows, decoder);
    }

    /// <summary>
    /// One row per information type the feature links to (S-100 Part 10a
    /// INAS; S-98 15.1 asks the report to follow information associations),
    /// named by the information type and holding its attributes, so the
    /// notes, texts and picture names it carries read as part of the report.
    /// A record linked more than once is shown once; a dangling link or an
    /// information type with no values is left out.
    /// </summary>
    /// <param name="document">The cell.</param>
    /// <param name="feature">The feature record.</param>
    /// <param name="decoder">Decodes names and listed values, or <c>null</c>.</param>
    /// <returns>The information rows, in association order.</returns>
    public static IReadOnlyList<PickAttribute> InformationRows(
        S101Document document, S101FeatureRecord feature, FeatureCatalogueDecoder? decoder)
    {
        var result = new List<PickAttribute>();
        var seen = new HashSet<uint>();
        foreach (var association in feature.InformationAssociations)
        {
            if (!seen.Add(association.RecordId)
                || !document.InformationTypes.TryGetValue(association.RecordId, out var information))
            {
                continue;
            }

            var children = Attributes(document, information.Attributes, decoder);
            if (children.Count == 0)
                continue;

            var code = Acronym(document.InformationTypeCatalogue, information.InformationTypeCode);
            result.Add(new PickAttribute
            {
                Code = code,
                Name = decoder?.ResolveInformationTypeName(code),
                RawValue = string.Empty,
                Children = children,
            });
        }

        return result;
    }

    /// <summary>
    /// The features the feature links to (S-100 Part 10a FACS: aggregations,
    /// compositions and other feature associations), each named by its
    /// association, role and the target's type and name, with the target's
    /// record id as <see cref="FeatureReference.TargetRef"/> so the pick
    /// report can follow it.
    /// </summary>
    /// <param name="document">The cell.</param>
    /// <param name="feature">The feature record.</param>
    /// <param name="lookup">Finds a feature record by its record id.</param>
    /// <param name="decoder">Decodes names, or <c>null</c>.</param>
    /// <returns>The references, in association order.</returns>
    public static IReadOnlyList<FeatureReference> FeatureReferences(
        S101Document document,
        S101FeatureRecord feature,
        Func<uint, S101FeatureRecord?> lookup,
        FeatureCatalogueDecoder? decoder)
    {
        var result = new List<FeatureReference>(feature.FeatureAssociations.Count);
        foreach (var association in feature.FeatureAssociations)
        {
            result.Add(new FeatureReference
            {
                Role = Acronym(document.FeatureAssociationCatalogue, association.NumericCode),
                ArcRole = Acronym(document.RoleCatalogue, association.RoleCode),
                TargetRef = association.RecordId.ToString(CultureInfo.InvariantCulture),
                TargetName = lookup(association.RecordId) is { } target
                    ? TargetName(document, target, decoder)
                    : null,
            });
        }

        return result;
    }

    /// <summary>
    /// The <c>fileReference</c> values (and their S-57 aliases) of the
    /// information types the feature links to, for resolving their text.
    /// </summary>
    /// <param name="document">The cell.</param>
    /// <param name="feature">The feature record.</param>
    /// <returns>The file names, in association order.</returns>
    public static IEnumerable<string> InformationFileReferences(S101Document document, S101FeatureRecord feature)
    {
        foreach (var association in feature.InformationAssociations)
        {
            if (!document.InformationTypes.TryGetValue(association.RecordId, out var information))
                continue;

            foreach (var attribute in information.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Value)
                    && FeatureInfoBuilder.FileReferenceAttributeCodes.Contains(AttributeCode(document, attribute.NumericCode)))
                {
                    yield return attribute.Value;
                }
            }
        }
    }

    // "Light All Around" or "Light All Around: Bell Rock".
    private static string TargetName(S101Document document, S101FeatureRecord target, FeatureCatalogueDecoder? decoder)
    {
        var code = Acronym(document.FeatureTypeCatalogue, target.FeatureTypeCode);
        var type = decoder?.ResolveFeatureTypeName(code) ?? code;
        return FeatureName(document, target) is { } name ? $"{type}: {name}" : type;
    }

    // The first featureName/name value.
    private static string? FeatureName(S101Document document, S101FeatureRecord feature)
    {
        var attributes = feature.Attributes;
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i].ParentIndex == 0 || string.IsNullOrWhiteSpace(attributes[i].Value))
                continue;
            var parent = attributes[i].ParentIndex - 1;
            if (parent < attributes.Count
                && AttributeCode(document, attributes[i].NumericCode) == "name"
                && AttributeCode(document, attributes[parent].NumericCode) == "featureName")
            {
                return attributes[i].Value;
            }
        }

        return null;
    }

    private static string Acronym(IReadOnlyDictionary<ushort, string> catalogue, ushort numericCode) =>
        catalogue.TryGetValue(numericCode, out var code)
            ? code
            : numericCode.ToString(CultureInfo.InvariantCulture);

    private static string AttributeCode(S101Document document, ushort numericCode) =>
        Acronym(document.AttributeTypeCatalogue, numericCode);
}
