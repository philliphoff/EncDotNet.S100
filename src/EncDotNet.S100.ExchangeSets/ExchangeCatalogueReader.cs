using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.ExchangeSets;

public static class ExchangeCatalogueReader
{
    private static readonly XNamespace Gco = "http://standards.iso.org/iso/19115/-3/gco/1.0";
    private static readonly XNamespace Gex = "http://standards.iso.org/iso/19115/-3/gex/1.0";
    private static readonly XNamespace Cit = "http://standards.iso.org/iso/19115/-3/cit/2.0";
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
        XNamespace xc = root.Name.Namespace;
        XNamespace lan = root.GetNamespaceOfPrefix("lan")
            ?? "http://standards.iso.org/iso/19115/-3/lan/2.0";

        var identifierEl = root.Element(xc + "identifier")!;
        var contactEl = root.Element(xc + "contact");
        var defaultLocaleEl = root.Element(xc + "defaultLocale");

        return new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = (string)identifierEl.Element(xc + "identifier")!,
                DateTime = (string)identifierEl.Element(xc + "dateTime")!,
            },
            Contact = ReadContact(contactEl, xc),
            ProductSpecification = ReadProductSpecification(root.Element(xc + "productSpecification"), xc),
            DefaultLocaleLanguage = ReadLocaleLanguage(defaultLocaleEl, lan),
            DefaultLocaleCharacterEncoding = ReadLocaleCharacterEncoding(defaultLocaleEl, lan),
            Description = ReadCharacterString(root.Element(xc + "exchangeCatalogueDescription")),
            Comment = ReadCharacterString(root.Element(xc + "exchangeCatalogueComment")),
            DataServerIdentifier = (string?)root.Element(xc + "dataServerIdentifier"),
            DatasetDiscoveryMetadata = root
                .Element(xc + "datasetDiscoveryMetadata")?
                .Elements(xc + "S100_DatasetDiscoveryMetadata")
                .Select(e => ReadDatasetDiscovery(e, xc, lan))
                .ToList() ?? [],
            SupportFileDiscoveryMetadata = root
                .Element(xc + "supportFileDiscoveryMetadata")?
                .Elements(xc + "S100_SupportFileDiscoveryMetadata")
                .Select(e => ReadSupportFileDiscovery(e, xc))
                .ToList() ?? [],
            CatalogueDiscoveryMetadata = root
                .Element(xc + "catalogueDiscoveryMetadata")?
                .Elements(xc + "S100_CatalogueDiscoveryMetadata")
                .Select(e => ReadCatalogueDiscovery(e, xc, lan))
                .ToList() ?? [],
        };
    }

    private static ExchangeCatalogueContact? ReadContact(XElement? element, XNamespace xc)
    {
        if (element is null) return null;

        var addressEl = element.Element(xc + "address");

        return new ExchangeCatalogueContact
        {
            Organization = ReadCharacterString(element.Element(xc + "organization")),
            Phone = ReadNestedCharacterString(element.Element(xc + "phone"), Cit + "number"),
            DeliveryPoint = ReadNestedCharacterString(addressEl, Cit + "deliveryPoint"),
            City = ReadNestedCharacterString(addressEl, Cit + "city"),
            AdministrativeArea = ReadNestedCharacterString(addressEl, Cit + "administrativeArea"),
            PostalCode = ReadNestedCharacterString(addressEl, Cit + "postalCode"),
            Country = ReadNestedCharacterString(addressEl, Cit + "country"),
        };
    }

    private static ProductSpecification? ReadProductSpecification(XElement? element, XNamespace xc)
    {
        if (element is null) return null;

        var numberStr = (string?)element.Element(xc + "number");

        return new ProductSpecification
        {
            Name = (string?)element.Element(xc + "name"),
            Version = (string?)element.Element(xc + "version"),
            Date = (string?)element.Element(xc + "date"),
            ProductIdentifier = (string?)element.Element(xc + "productIdentifier"),
            Number = int.TryParse(numberStr, CultureInfo.InvariantCulture, out var n) ? n : null,
            CompliancyCategory = (string?)element.Element(xc + "compliancyCategory"),
        };
    }

    private static DatasetDiscoveryMetadata ReadDatasetDiscovery(XElement element, XNamespace xc, XNamespace lan)
    {
        var defaultLocaleEl = element.Element(xc + "defaultLocale");

        return new DatasetDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            Description = ReadCharacterString(element.Element(xc + "description")),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DataProtection = ParseBool(element, "dataProtection", xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            Copyright = ParseBool(element, "copyright", xc),
            Classification = ReadCodeListValue(element.Element(xc + "classification")),
            Purpose = (string?)element.Element(xc + "purpose"),
            NotForNavigation = ParseBool(element, "notForNavigation", xc),
            SpecificUsage = ReadSpecificUsage(element.Element(xc + "specificUsage")),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            UpdateNumber = ParseInt(element, "updateNumber", xc),
            UpdateApplicationDate = (string?)element.Element(xc + "updateApplicationDate"),
            IssueDate = (string?)element.Element(xc + "issueDate"),
            BoundingBox = ReadBoundingBox(element.Element(xc + "boundingBox")),
            ProductSpecification = ReadProductSpecification(element.Element(xc + "productSpecification"), xc),
            ProducingAgency = ReadProducingAgency(element.Element(xc + "producingAgency")),
            EncodingFormat = (string?)element.Element(xc + "encodingFormat"),
            DataCoverages = element
                .Elements(xc + "dataCoverage")
                .Select(e => ReadDataCoverage(e, xc))
                .ToList(),
            DefaultLocaleLanguage = ReadLocaleLanguage(defaultLocaleEl, lan),
            DefaultLocaleCharacterEncoding = ReadLocaleCharacterEncoding(defaultLocaleEl, lan),
            MetadataDateStamp = (string?)element.Element(xc + "metadataDateStamp"),
            NavigationPurpose = (string?)element.Element(xc + "navigationPurpose"),
        };
    }

    private static SupportFileDiscoveryMetadata ReadSupportFileDiscovery(XElement element, XNamespace xc)
    {
        return new SupportFileDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            RevisionStatus = (string?)element.Element(xc + "revisionStatus"),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            IssueDate = (string?)element.Element(xc + "issueDate"),
            SupportFileSpecificationName = (string?)element
                .Element(xc + "supportFileSpecification")?
                .Element(xc + "name"),
            DataType = (string?)element.Element(xc + "dataType"),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            SupportedResources = element
                .Elements(xc + "supportedResource")
                .Select(e => e.Value.Trim())
                .ToList(),
            ResourcePurpose = (string?)element.Element(xc + "resourcePurpose"),
        };
    }

    private static CatalogueDiscoveryMetadata ReadCatalogueDiscovery(XElement element, XNamespace xc, XNamespace lan)
    {
        var defaultLocaleEl = element.Element(xc + "defaultLocale");

        return new CatalogueDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            Purpose = (string?)element.Element(xc + "purpose"),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            Scope = (string?)element.Element(xc + "scope"),
            VersionNumber = (string?)element.Element(xc + "versionNumber"),
            IssueDate = (string?)element.Element(xc + "issueDate"),
            ProductSpecification = ReadProductSpecification(element.Element(xc + "productSpecification"), xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DefaultLocaleLanguage = ReadLocaleLanguage(defaultLocaleEl, lan),
            DefaultLocaleCharacterEncoding = ReadLocaleCharacterEncoding(defaultLocaleEl, lan),
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

    private static DataCoverage ReadDataCoverage(XElement element, XNamespace xc)
    {
        var maxStr = (string?)element.Element(xc + "maximumDisplayScale");
        var minStr = (string?)element.Element(xc + "minimumDisplayScale");

        return new DataCoverage
        {
            BoundingPolygon = element.Element(xc + "boundingPolygon")?.ToString(),
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

    private static string? ReadLocaleLanguage(XElement? localeElement, XNamespace lan)
    {
        var langCode = localeElement?
            .Descendants(lan + "LanguageCode")
            .FirstOrDefault();

        return (string?)langCode?.Attribute("codeListValue");
    }

    private static string? ReadLocaleCharacterEncoding(XElement? localeElement, XNamespace lan)
    {
        var charCode = localeElement?
            .Descendants(lan + "MD_CharacterSetCode")
            .FirstOrDefault();

        return (string?)charCode?.Attribute("codeListValue");
    }

    private static bool ParseBool(XElement parent, string localName, XNamespace xc)
    {
        var value = (string?)parent.Element(xc + localName);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseInt(XElement parent, string localName, XNamespace xc)
    {
        var value = (string?)parent.Element(xc + localName);
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
