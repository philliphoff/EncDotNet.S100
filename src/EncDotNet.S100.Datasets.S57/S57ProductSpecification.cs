using System.Globalization;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Codes an S-57 dataset declares in the product specification (<c>PRSP</c>)
/// subfield of its Data Set Identification (<c>DSID</c>) field, which names the
/// product specification the exchange was produced under (S-57 Edition 3.1,
/// Part 1 §1.4.1 and Part 3 §7.3.1.1).
/// </summary>
/// <remarks>
/// <para>
/// S-57 itself enumerates only <see cref="ElectronicNavigationalChart"/> and
/// <see cref="ObjectCatalogueDataDictionary"/>. Inland ENC (IENC) producers
/// declare <see cref="InlandElectronicNavigationalChart"/>: USACE cells built to
/// the IENC 2.4 Product Specification carry <c>PRSP = 10</c> with a product
/// specification edition (<c>PRED</c>) of <c>2.4</c>, where a maritime ENC
/// carries <c>PRSP = 1</c>.
/// </para>
/// <para>
/// The distinction matters because an inland cell is encoded exactly like a
/// maritime one — same ISO 8211 records, same S-57 data model — yet draws on an
/// inland object catalogue (object classes and attributes in the 17000+ code
/// range) whose natural S-100 successor is S-401 rather than S-101.
/// </para>
/// </remarks>
public static class S57ProductSpecification
{
    /// <summary>Electronic Navigational Chart (S-57 Appendix B.1).</summary>
    public const int ElectronicNavigationalChart = 1;

    /// <summary>IHO Object Catalogue Data Dictionary (S-57 Appendix B.2).</summary>
    public const int ObjectCatalogueDataDictionary = 2;

    /// <summary>
    /// Inland Electronic Navigational Chart, as declared by IENC producers. Not
    /// enumerated by S-57 Edition 3.1 itself.
    /// </summary>
    public const int InlandElectronicNavigationalChart = 10;

    /// <summary>
    /// Parses a <c>PRSP</c> subfield value as read from a dataset. ENCs use the
    /// binary implementation, where the subfield is a one-byte integer
    /// (<c>b11</c>) that reads back as its decimal digits. The ASCII
    /// implementation's acronym form (<c>ENC</c>, <c>ODD</c>) is not recognised.
    /// </summary>
    /// <param name="value">The raw subfield value, e.g. <c>"10"</c>.</param>
    /// <param name="code">The parsed product specification code.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a non-negative integer.</returns>
    public static bool TryParse(string? value, out int code)
        => int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out code);
}
