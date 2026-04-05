using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.ExchangeSets;

public static class ExchangeCatalogueReader
{
    private static readonly XNamespace XC = "http://www.iho.int/s100/xc/5.2";
    private static readonly XNamespace Gco = "http://standards.iso.org/iso/19115/-3/gco/1.0";
    private static readonly XNamespace Gex = "http://standards.iso.org/iso/19115/-3/gex/1.0";
    private static readonly XNamespace Cit = "http://standards.iso.org/iso/19115/-3/cit/2.0";
    private static readonly XNamespace Lan = "http://standards.iso.org/iso/19115/-3/lan/2.0";
    private static readonly XNamespace Mri = "http://standards.iso.org/iso/19115/-3/mri/1.0";

    public static ExchangeCatalogue Read(Stream stream)
    {
        var doc = XDocument.Load(stream);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    public static ExchangeCatalogue Read(string path)
    {
        var doc = XDocument.Load(path);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    private static ExchangeCatalogue ReadCatalogue(XElement root)
    {
        var identifierEl = root.Element(XC + "identifier")!;
        var contactEl = root.Element(XC + "contact");
        var defaultLocaleEl = root.Element(XC + "defaultLocale");

        return new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = (string)identifierEl.Element(XC + "identifier")!,
                DateTime = (string)identifierEl.Element(XC + "dateTime")!,
            },
            Contact = ReadContact(contactEl),
            ProductSpecification = ReadProductSpecification(root.Element(XC + "productSpecification")),
            DefaultLocaleLanguage = ReadLocaleLanguage(defaultLocaleEl),
            DefaultLocaleCharacterEncoding = ReadLocaleCharacterEncoding(defaultLocaleEl),
            Description = ReadCharacterString(root.Element(XC + "exchangeCatalogueDescription")),
            Comment = ReadCharacterString(root.Element(XC + "exchangeCatalogueComment")),
            DataServerIdentifier = (string?)root.Element(XC + "dataServerIdentifier"),
            DatasetDiscoveryMetadata = root
                .Element(XC + "datasetDiscoveryMetadata")?
                .Elements(XC + "S100_DatasetDiscoveryMetadata")
                .Select(ReadDatasetDiscovery)
                .ToList() ?? [],
            SupportFileDiscoveryMetadata = root
                .Element(XC + "supportFileDiscoveryMetadata")?
                .Elements(XC + "S100_SupportFileDiscoveryMetadata")
                .Select(ReadSupportFileDiscovery)
                .ToList() ?? [],
            CatalogueDiscoveryMetadata = root
                .Element(XC + "catalogueDiscoveryMetadata")?
                .Elements(XC + "S100_CatalogueDiscoveryMetadata")
                .Select(ReadCatalogueDiscovery)
                .ToList() ?? [],
        };
    }

    private static ExchangeCatalogueContact? ReadContact(XElement? element)
    {
        if (element is null) return null;

        var addressEl = element.Element(XC + "address");

        return new ExchangeCatalogueContact
        {
            Organization = ReadCharacterString(element.Element(XC + "organization")),
            Phone = ReadNestedCharacterString(element.Element(XC + "phone"), Cit + "number"),
            DeliveryPoint = ReadNestedCharacterString(addressEl, Cit + "deliveryPoint"),
            City = ReadNestedCharacterString(addressEl, Cit + "city"),
            AdministrativeArea = ReadNestedCharacterString(addressEl, Cit + "administrativeArea"),
            PostalCode = ReadNestedCharacterString(addressEl, Cit + "postalCode"),
            Country = ReadNestedCharacterString(addressEl, Cit + "country"),
        };
    }

    private static ProductSpecification? ReadProductSpecification(XElement? element)
    {
        if (element is null) return null;

        var numberStr = (string?)element.Element(XC + "number");

        return new ProductSpecification
        {
            Name = (string?)element.Element(XC + "name"),
            Version = (string?)element.Element(XC + "version"),
            Date = (string?)element.Element(XC + "date"),
            ProductIdentifier = (string?)element.Element(XC + "productIdentifier"),
            Number = int.TryParse(numberStr, CultureInfo.InvariantCulture, out var n) ? n : null,
            CompliancyCategory = (string?)element.Element(XC + "compliancyCategory"),
        };
    }

    private static DatasetDiscoveryMetadata ReadDatasetDiscovery(XElement element)
    {
        var defaultLocaleEl = element.Element(XC + "defaultLocale");

        return new DatasetDiscoveryMetadata
        {
            FileName = (string)element.Element(XC + "fileName")!,
            Description = ReadCharacterString(element.Element(XC + "description")),
            CompressionFlag = ParseBool(element, "compressionFlag"),
            DataProtection = ParseBool(element, "dataProtection"),
            DigitalSignatureReference = (string?)element.Element(XC + "digitalSignatureReference"),
            Copyright = ParseBool(element, "copyright"),
            Classification = ReadCodeListValue(element.Element(XC + "classification")),
            Purpose = (string?)element.Element(XC + "purpose"),
            NotForNavigation = ParseBool(element, "notForNavigation"),
            SpecificUsage = ReadSpecificUsage(element.Element(XC + "specificUsage")),
            EditionNumber = ParseInt(element, "editionNumber"),
            UpdateNumber = ParseInt(element, "updateNumber"),
            UpdateApplicationDate = (string?)element.Element(XC + "updateApplicationDate"),
            IssueDate = (string?)element.Element(XC + "issueDate"),
            BoundingBox = ReadBoundingBox(element.Element(XC + "boundingBox")),
            ProductSpecification = ReadProductSpecification(element.Element(XC + "productSpecification")),
            ProducingAgency = ReadProducingAgency(element.Element(XC + "producingAgency")),
            EncodingFormat = (string?)element.Element(XC + "encodingFormat"),
            DataCoverages = element
                .Elements(XC + "dataCoverage")
                .Select(ReadDataCoverage)
                .ToList(),
            DefaultLocaleLanguage = ReadLocaleLanguage(defaultLocaleEl),
            DefaultLocaleCharacterEncoding = ReadLocaleCharacterEncoding(defaultLocaleEl),
            MetadataDateStamp = (string?)element.Element(XC + "metadataDateStamp"),
            NavigationPurpose = (string?)element.Element(XC + "navigationPurpose"),
        };
    }

    private static SupportFileDiscoveryMetadata ReadSupportFileDiscovery(XElement element)
    {
        return new SupportFileDiscoveryMetadata
        {
            FileName = (string)element.Element(XC + "fileName")!,
            RevisionStatus = (string?)element.Element(XC + "revisionStatus"),
            EditionNumber = ParseInt(element, "editionNumber"),
            IssueDate = (string?)element.Element(XC + "issueDate"),
            SupportFileSpecificationName = (string?)element
                .Element(XC + "supportFileSpecification")?
                .Element(XC + "name"),
            DataType = (string?)element.Element(XC + "dataType"),
            CompressionFlag = ParseBool(element, "compressionFlag"),
            DigitalSignatureReference = (string?)element.Element(XC + "digitalSignatureReference"),
            SupportedResources = element
                .Elements(XC + "supportedResource")
                .Select(e => e.Value.Trim())
                .ToList(),
            ResourcePurpose = (string?)element.Element(XC + "resourcePurpose"),
        };
    }

    private static CatalogueDiscoveryMetadata ReadCatalogueDiscovery(XElement element)
    {
        var defaultLocaleEl = element.Element(XC + "defaultLocale");

        return new CatalogueDiscoveryMetadata
        {
            FileName = (string)element.Element(XC + "fileName")!,
            Purpose = (string?)element.Element(XC + "purpose"),
            EditionNumber = ParseInt(element, "editionNumber"),
            Scope = (string?)element.Element(XC + "scope"),
            VersionNumber = (string?)element.Element(XC + "versionNumber"),
            IssueDate = (string?)element.Element(XC + "issueDate"),
            ProductSpecification = ReadProductSpecification(element.Element(XC + "productSpecification")),
            DigitalSignatureReference = (string?)element.Element(XC + "digitalSignatureReference"),
            CompressionFlag = ParseBool(element, "compressionFlag"),
            DefaultLocaleLanguage = ReadLocaleLanguage(defaultLocaleEl),
            DefaultLocaleCharacterEncoding = ReadLocaleCharacterEncoding(defaultLocaleEl),
        };
    }

    private static BoundingBox? ReadBoundingBox(XElement? element)
    {
        if (element is null) return null;

        return new BoundingBox
        {
            WestBoundLongitude = ParseDecimal(element.Element(Gex + "westBoundLongitude")),
            EastBoundLongitude = ParseDecimal(element.Element(Gex + "eastBoundLongitude")),
            SouthBoundLatitude = ParseDecimal(element.Element(Gex + "southBoundLatitude")),
            NorthBoundLatitude = ParseDecimal(element.Element(Gex + "northBoundLatitude")),
        };
    }

    private static DataCoverage ReadDataCoverage(XElement element)
    {
        var maxStr = (string?)element.Element(XC + "maximumDisplayScale");
        var minStr = (string?)element.Element(XC + "minimumDisplayScale");

        return new DataCoverage
        {
            BoundingPolygon = element.Element(XC + "boundingPolygon")?.ToString(),
            MaximumDisplayScale = int.TryParse(maxStr, CultureInfo.InvariantCulture, out var max) ? max : null,
            MinimumDisplayScale = int.TryParse(minStr, CultureInfo.InvariantCulture, out var min) ? min : null,
        };
    }

    private static string? ReadCharacterString(XElement? element)
    {
        if (element is null) return null;
        return (string?)element.Element(Gco + "CharacterString") ?? element.Value;
    }

    private static string? ReadNestedCharacterString(XElement? parent, XName childName)
    {
        if (parent is null) return null;
        return ReadCharacterString(parent.Element(childName));
    }

    private static string? ReadCodeListValue(XElement? element)
    {
        if (element is null) return null;

        // Look for nested code list element with codeListValue attribute
        foreach (var child in element.Elements())
        {
            var attr = (string?)child.Attribute("codeListValue");
            if (attr is not null) return attr;
        }

        return element.Value;
    }

    private static string? ReadSpecificUsage(XElement? element)
    {
        if (element is null) return null;

        return ReadCharacterString(
            element.Element(Mri + "MD_Usage")?
                   .Element(Mri + "specificUsage"));
    }

    private static string? ReadProducingAgency(XElement? element)
    {
        if (element is null) return null;

        return ReadCharacterString(
            element.Element(Cit + "CI_Responsibility")?
                   .Element(Cit + "party")?
                   .Element(Cit + "CI_Organisation")?
                   .Element(Cit + "name"));
    }

    private static string? ReadLocaleLanguage(XElement? localeElement)
    {
        var langCode = localeElement?
            .Descendants(Lan + "LanguageCode")
            .FirstOrDefault();

        return (string?)langCode?.Attribute("codeListValue");
    }

    private static string? ReadLocaleCharacterEncoding(XElement? localeElement)
    {
        var charCode = localeElement?
            .Descendants(Lan + "MD_CharacterSetCode")
            .FirstOrDefault();

        return (string?)charCode?.Attribute("codeListValue");
    }

    private static bool ParseBool(XElement parent, string localName)
    {
        var value = (string?)parent.Element(XC + localName);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseInt(XElement parent, string localName)
    {
        var value = (string?)parent.Element(XC + localName);
        return int.TryParse(value, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static double ParseDecimal(XElement? element)
    {
        var dec = element?.Element(Gco + "Decimal");
        if (dec is not null && double.TryParse(dec.Value, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return 0;
    }
}
