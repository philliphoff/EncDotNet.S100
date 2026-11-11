using System.Globalization;
using System.Text;
using System.Xml;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>One dataset listed in an exchange catalogue.</summary>
/// <param name="FileName">The dataset's path relative to the exchange set root, with forward slashes.</param>
/// <param name="ProductIdentifier">The product, such as <c>"S-101"</c>.</param>
/// <param name="ProductName">The product specification's name, such as <c>"Electronic Navigational Chart"</c>.</param>
/// <param name="ProductVersion">The product specification's edition, such as <c>"2.0.0"</c>.</param>
/// <param name="EncodingFormat">The S-100 encoding format, such as <c>"ISO/IEC 8211"</c> or <c>"HDF5"</c>.</param>
/// <param name="IssueDate">The issue date.</param>
/// <param name="West">Western bound in degrees.</param>
/// <param name="South">Southern bound in degrees.</param>
/// <param name="East">Eastern bound in degrees.</param>
/// <param name="North">Northern bound in degrees.</param>
/// <param name="Comment">A free-text comment, or <c>null</c>.</param>
public sealed record CatalogueDataset(
    string FileName,
    string ProductIdentifier,
    string ProductName,
    string ProductVersion,
    string EncodingFormat,
    DateOnly IssueDate,
    double West,
    double South,
    double East,
    double North,
    string? Comment = null)
{
    /// <summary>The edition number.</summary>
    public int EditionNumber { get; init; } = 1;

    /// <summary>The update number; above zero, the dataset is an update (<c>purpose</c> <c>update</c>).</summary>
    public int UpdateNumber { get; init; }

    /// <summary>Whether the dataset is encrypted (Part 15 <c>dataProtection</c>).</summary>
    public bool DataProtection { get; init; }

    /// <summary>
    /// The dataset's Part 15 signatures. A signature on data with no
    /// <see cref="DigitalSignatureValue.DataStatus"/> is written without the
    /// <c>dataStatus</c> attribute, as some producers do (#754).
    /// </summary>
    public IReadOnlyList<DigitalSignatureValue> Signatures { get; init; } = [];
}

/// <summary>
/// Writes an S-100 Part 17 exchange catalogue (<c>CATALOG.XML</c>, S-100
/// Ed 5.2 <c>S100XC</c> schema) for unprotected datasets.
/// </summary>
/// <remarks>
/// Elements follow the Part 17 §17-4.5 order. The output is a pure function
/// of the arguments: no wall-clock time, UTF-8 without a byte-order mark,
/// <c>\n</c> line endings, and invariant number formatting. With a
/// certificate block and per-dataset signatures it also writes the Part 15
/// security elements (§15-8.11); permits are written separately
/// (<see cref="Protection.ProtectedScenarioGenerator"/>).
/// </remarks>
public static class ExchangeCatalogueBuilder
{
    private const string Xc = "http://www.iho.int/s100/xc/5.2";
    private const string Gco = "http://standards.iso.org/iso/19115/-3/gco/1.0";
    private const string Gex = "http://standards.iso.org/iso/19115/-3/gex/1.0";
    private const string Cit = "http://standards.iso.org/iso/19115/-3/cit/2.0";
    private const string Se = "http://www.iho.int/s100/se/5.2";

    /// <summary>The producer code written for every generated dataset; it marks the data as synthetic.</summary>
    public const string ProducerCode = "ZZ00";

    /// <summary>The producing organisation written into generated metadata.</summary>
    public const string Organisation = "Synthetic Hydrographic Office (fictitious; generated test data)";

    /// <summary>Builds the catalogue's bytes.</summary>
    /// <param name="identifier">The exchange catalogue identifier.</param>
    /// <param name="dateTime">The catalogue's issue time (UTC).</param>
    /// <param name="comment">The exchange catalogue comment.</param>
    /// <param name="datasets">The datasets, in the order to list them.</param>
    /// <param name="certificates">The Part 15 certificate block for the datasets' signatures, or <c>null</c>.</param>
    /// <returns>The UTF-8 encoded catalogue.</returns>
    public static byte[] Build(string identifier, DateTime dateTime, string comment, IReadOnlyList<CatalogueDataset> datasets, CertificateBlock? certificates = null)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(datasets);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("S100XC", "S100_ExchangeCatalogue", Xc);
            xml.WriteAttributeString("xmlns", "gco", null, Gco);
            xml.WriteAttributeString("xmlns", "gex", null, Gex);
            xml.WriteAttributeString("xmlns", "cit", null, Cit);
            if (certificates is not null)
            {
                xml.WriteAttributeString("xmlns", "S100SE", null, Se);
            }

            xml.WriteStartElement("identifier", Xc);
            xml.WriteElementString("identifier", Xc, identifier);
            xml.WriteElementString("dateTime", Xc, dateTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            xml.WriteEndElement();

            xml.WriteStartElement("contact", Xc);
            CharacterString(xml, "organization", Xc, Organisation);
            xml.WriteEndElement();

            CharacterString(xml, "exchangeCatalogueComment", Xc, comment);
            if (certificates is not null)
            {
                WriteCertificates(xml, certificates);
            }

            xml.WriteStartElement("datasetDiscoveryMetadata", Xc);
            foreach (var dataset in datasets)
            {
                WriteDataset(xml, dataset);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteDataset(XmlWriter xml, CatalogueDataset dataset)
    {
        xml.WriteStartElement("S100_DatasetDiscoveryMetadata", Xc);
        xml.WriteElementString("fileName", Xc, dataset.FileName);
        xml.WriteElementString("compressionFlag", Xc, "false");
        xml.WriteElementString("dataProtection", Xc, dataset.DataProtection ? "true" : "false");
        if (dataset.DataProtection)
        {
            xml.WriteElementString("protectionScheme", Xc, "S100p15");
        }

        if (dataset.Signatures.Count > 0)
        {
            xml.WriteElementString("digitalSignatureReference", Xc, "ECDSA-384-SHA2");
            foreach (var signature in dataset.Signatures)
            {
                WriteSignature(xml, signature);
            }
        }

        xml.WriteElementString("copyright", Xc, "false");
        xml.WriteElementString("purpose", Xc, dataset.UpdateNumber > 0 ? "update" : "newDataset");
        xml.WriteElementString("notForNavigation", Xc, "true");
        xml.WriteElementString("editionNumber", Xc, dataset.EditionNumber.ToString(CultureInfo.InvariantCulture));
        xml.WriteElementString("updateNumber", Xc, dataset.UpdateNumber.ToString(CultureInfo.InvariantCulture));
        xml.WriteElementString("issueDate", Xc, dataset.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        xml.WriteStartElement("boundingBox", Xc);
        Decimal(xml, "westBoundLongitude", dataset.West);
        Decimal(xml, "eastBoundLongitude", dataset.East);
        Decimal(xml, "southBoundLatitude", dataset.South);
        Decimal(xml, "northBoundLatitude", dataset.North);
        xml.WriteEndElement();

        xml.WriteStartElement("productSpecification", Xc);
        CharacterString(xml, "name", Xc, dataset.ProductName);
        CharacterString(xml, "version", Xc, dataset.ProductVersion);
        xml.WriteElementString("productIdentifier", Xc, dataset.ProductIdentifier);
        xml.WriteEndElement();

        xml.WriteStartElement("producingAgency", Xc);
        xml.WriteStartElement("CI_Responsibility", Cit);
        xml.WriteStartElement("role", Cit);
        xml.WriteStartElement("CI_RoleCode", Cit);
        xml.WriteAttributeString("codeList", "http://standards.iso.org/iso/19115/resources/Codelists/cat/codelists.xml#CI_RoleCode");
        xml.WriteAttributeString("codeListValue", "producer");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteStartElement("party", Cit);
        xml.WriteStartElement("CI_Organisation", Cit);
        CharacterString(xml, "name", Cit, Organisation);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteElementString("producerCode", Xc, ProducerCode);
        xml.WriteElementString("encodingFormat", Xc, dataset.EncodingFormat);
        if (dataset.Comment is { } comment)
        {
            CharacterString(xml, "comment", Xc, comment);
        }

        xml.WriteEndElement();
    }

    private static void WriteCertificates(XmlWriter xml, CertificateBlock certificates)
    {
        xml.WriteStartElement("certificates", Xc);
        if (certificates.SchemeAdministratorId is { } schemeAdministrator)
        {
            xml.WriteStartElement("schemeAdministrator", Se);
            xml.WriteAttributeString("id", schemeAdministrator);
            xml.WriteEndElement();
        }

        foreach (var certificate in certificates.Certificates)
        {
            xml.WriteStartElement("certificate", Se);
            xml.WriteAttributeString("id", certificate.Id);
            if (certificate.Issuer is { } issuer)
            {
                xml.WriteAttributeString("issuer", issuer);
            }

            xml.WriteString(Convert.ToBase64String(certificate.Value));
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private static void WriteSignature(XmlWriter xml, DigitalSignatureValue signature)
    {
        xml.WriteStartElement("digitalSignatureValue", Xc);
        xml.WriteStartElement(signature.Kind == DigitalSignatureKind.SignatureOnSignature ? "S100_SE_SignatureOnSignature" : "S100_SE_SignatureOnData", Se);
        xml.WriteAttributeString("id", signature.Id);
        xml.WriteAttributeString("certificateRef", signature.CertificateRef);
        if (signature.SignatureRef is { } signatureRef)
        {
            xml.WriteAttributeString("signatureRef", signatureRef);
        }

        if (signature.DataStatus is { } status)
        {
            xml.WriteAttributeString("dataStatus", status switch
            {
                SignatureDataStatus.Compressed => "compressed",
                SignatureDataStatus.Encrypted => "encrypted",
                _ => "unencrypted",
            });
        }

        xml.WriteString(Convert.ToBase64String(signature.Value));
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void CharacterString(XmlWriter xml, string name, string ns, string value)
    {
        xml.WriteStartElement(name, ns);
        xml.WriteElementString("CharacterString", Gco, value);
        xml.WriteEndElement();
    }

    private static void Decimal(XmlWriter xml, string name, double value)
    {
        xml.WriteStartElement(name, Gex);
        xml.WriteElementString("Decimal", Gco, value.ToString("0.0#######", CultureInfo.InvariantCulture));
        xml.WriteEndElement();
    }
}
