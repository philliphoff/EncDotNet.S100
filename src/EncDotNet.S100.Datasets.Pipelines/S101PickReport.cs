using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Builds the S-101 (and S-401) pick report from a feature's ISO 8211 record
/// rather than the portrayal's attribute dictionary, which keeps one value per
/// code: every value of a repeated attribute gets its own row and complex
/// attributes nest their sub-attributes (S-98 15.1; S-164 FeatureInformation1).
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

    private static string AttributeCode(S101Document document, ushort numericCode) =>
        document.AttributeTypeCatalogue.TryGetValue(numericCode, out var code)
            ? code
            : numericCode.ToString(CultureInfo.InvariantCulture);
}
