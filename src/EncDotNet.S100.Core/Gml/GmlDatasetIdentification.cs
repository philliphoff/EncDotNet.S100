using System;
using System.Xml.Linq;

namespace EncDotNet.S100.Gml;

/// <summary>
/// Helpers for reading the declared product-specification edition from the
/// <c>DatasetIdentificationInformation</c> block of an S-100 GML dataset.
/// </summary>
/// <remarks>
/// <para>
/// Per S-100 Part 10b, a GML dataset carries its product-specification
/// metadata in a <c>DatasetIdentificationInformation</c> element whose
/// <c>productEdition</c> child holds the declared edition (e.g.
/// <c>"2.0.0"</c>). Both the canonical S-100 GML 5.0 namespace and the legacy
/// 1.0 profile are accepted.
/// </para>
/// <para>
/// The application-schema namespace is deliberately <em>not</em> used as an
/// edition source: its trailing version segment is unreliable across products
/// (e.g. S-201's namespace <c>http://www.iho.int/S-201/gml/cs0/1.0</c> ends in
/// the CS0 GML-profile version <c>1.0</c>, not the product edition
/// <c>2.0.0</c>), so inferring an edition from it would produce spurious
/// mismatch warnings.
/// </para>
/// </remarks>
public static class GmlDatasetIdentification
{
    private static readonly XNamespace S100Ns5 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace S100Ns1 = "http://www.iho.int/S100/profile/s100gml/1.0";

    /// <summary>
    /// Reads the declared product-specification edition for the dataset rooted
    /// at <paramref name="root"/> from
    /// <c>DatasetIdentificationInformation/productEdition</c>, accepting either
    /// the S-100 GML 5.0 namespace or the legacy 1.0 profile.
    /// </summary>
    /// <param name="root">The GML dataset root element.</param>
    /// <returns>
    /// The declared edition string (e.g. <c>"2.0.0"</c>), or <c>null</c> when
    /// the dataset declares none.
    /// </returns>
    public static string? ReadDeclaredEdition(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var ns in new[] { S100Ns5, S100Ns1 })
        {
            var dsInfo = root.Element(ns + "DatasetIdentificationInformation");
            var edition = dsInfo?.Element(ns + "productEdition")?.Value;
            if (!string.IsNullOrWhiteSpace(edition))
                return edition.Trim();
        }

        return null;
    }
}
