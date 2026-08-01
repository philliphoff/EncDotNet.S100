using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.ExchangeSets.Diagnostics;

namespace EncDotNet.S100.ExchangeSets;

public static class ExchangeCatalogueReader
{
    private static readonly XNamespace Gco = "http://standards.iso.org/iso/19115/-3/gco/1.0";
    private static readonly XNamespace Gex = "http://standards.iso.org/iso/19115/-3/gex/1.0";
    private static readonly XNamespace Cit = "http://standards.iso.org/iso/19115/-3/cit/2.0";
    private static readonly XNamespace Mri = "http://standards.iso.org/iso/19115/-3/mri/1.0";
    /// <summary>
    /// S-100 security-schema namespaces accepted for backwards-compatible
    /// exchange catalogue parsing.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.11.</remarks>
    private static readonly HashSet<string> S100SecurityNamespaces =
    [
        "http://www.iho.int/s100/se/5.0",
        "http://www.iho.int/s100/se/5.1",
        "http://www.iho.int/s100/se/5.2",
    ];

    public static ExchangeCatalogue Read(Stream stream)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("s100.exchangeset.parse");
        var doc = XDocument.Load(stream);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    public static ExchangeCatalogue Read(string path)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("s100.exchangeset.parse");
        activity?.SetTag("s100.exchangeset.path", path);
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
            Certificates = ReadCertificateBlock(root.Element(xc + "certificates")),
            DatasetDiscoveryMetadata = CollectDiscoveryRecords(
                    root, xc, "datasetDiscoveryMetadata", "_DatasetDiscoveryMetadata")
                .Select(e => ReadDatasetDiscovery(e, xc, lan))
                .ToList(),
            SupportFileDiscoveryMetadata = ReadSupportFileDiscoveries(root, xc),
            CatalogueDiscoveryMetadata = CollectDiscoveryRecords(
                    root, xc, "catalogueDiscoveryMetadata", "_CatalogueDiscoveryMetadata")
                .Select(e => ReadCatalogueDiscovery(e, xc, lan))
                .ToList(),
        };
    }

    /// <summary>
    /// Collects typed discovery records (e.g. <c>S100_DatasetDiscoveryMetadata</c>)
    /// from a catalogue, tolerating both layouts seen in the wild. Modern
    /// S-100 (Edition 5.x, Part 17) nests the records inside a wrapper
    /// element (e.g. <c>datasetDiscoveryMetadata</c>); the legacy
    /// <c>S100EC</c> schema used by some products — notably JCOMM/IHO
    /// S-411 — places the same <c>*_DatasetDiscoveryMetadata</c> records
    /// directly under the catalogue root with no wrapper. When the wrapper
    /// contains typed records its children are used; otherwise the root is
    /// scanned directly.
    /// </summary>
    private static IEnumerable<XElement> CollectDiscoveryRecords(
        XElement root, XNamespace xc, string wrapperName, string suffix)
    {
        var wrapper = root.Element(xc + wrapperName);
        if (wrapper is not null)
        {
            var wrappedMatches = wrapper
                .Elements()
                .Where(e => e.Name.LocalName.EndsWith(suffix, StringComparison.Ordinal))
                .ToList();

            if (wrappedMatches.Count > 0)
            {
                return wrappedMatches;
            }
        }

        return root
            .Elements()
            .Where(e => e.Name.LocalName.EndsWith(suffix, StringComparison.Ordinal));
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
        var digitalSignatures = ReadDigitalSignatures(element, xc);

        return new DatasetDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            FilePath = (string?)element.Element(xc + "filePath"),
            Description = ReadCharacterString(element.Element(xc + "description")),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DataProtection = ParseBool(element, "dataProtection", xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            DigitalSignatureAlgorithm = ParseSignatureAlgorithm(element, xc),
            DigitalSignatureValue = digitalSignatures.FirstOrDefault(),
            DigitalSignatures = digitalSignatures,
            ExpectedHash = ReadExpectedHash(element),
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

    /// <summary>
    /// Reads all support-file discovery records from the catalogue root,
    /// tolerating both encodings seen in the wild: a single
    /// <c>supportFileDiscoveryMetadata</c> container wrapping typed
    /// <c>*_SupportFileDiscoveryMetadata</c> children, and repeated
    /// <c>supportFileDiscoveryMetadata</c> elements that carry the fields
    /// inline. S-100 Edition 5.2.1 Part 17.
    /// </summary>
    private static List<SupportFileDiscoveryMetadata> ReadSupportFileDiscoveries(XElement root, XNamespace xc)
    {
        var result = new List<SupportFileDiscoveryMetadata>();

        foreach (var container in root.Elements(xc + "supportFileDiscoveryMetadata"))
        {
            var typed = container
                .Elements()
                .Where(e => e.Name.LocalName.EndsWith("_SupportFileDiscoveryMetadata", StringComparison.Ordinal))
                .ToList();

            if (typed.Count > 0)
            {
                result.AddRange(typed.Select(e => ReadSupportFileDiscovery(e, xc)));
                continue;
            }

            // Inline (repeated-sibling) form: the container itself is the record.
            if (container.Element(xc + "fileName") is not null)
                result.Add(ReadSupportFileDiscovery(container, xc));
        }

        return result;
    }

    private static SupportFileDiscoveryMetadata ReadSupportFileDiscovery(XElement element, XNamespace xc)
    {
        var digitalSignatures = ReadDigitalSignatures(element, xc);

        return new SupportFileDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            // S-100 Edition 5.2.1 Part 17: support file discovery declares its
            // directory via <fileLocation>; some producers instead reuse the
            // dataset-style <filePath>. Accept either so support files placed in
            // a sub-directory (e.g. "support/") resolve correctly.
            FilePath = (string?)element.Element(xc + "filePath")
                ?? (string?)element.Element(xc + "fileLocation"),
            RevisionStatus = (string?)element.Element(xc + "revisionStatus"),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            IssueDate = (string?)element.Element(xc + "issueDate"),
            SupportFileSpecificationName = (string?)element
                .Element(xc + "supportFileSpecification")?
                .Element(xc + "name"),
            DataType = (string?)element.Element(xc + "dataType"),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            DigitalSignatureAlgorithm = ParseSignatureAlgorithm(element, xc),
            DigitalSignatureValue = digitalSignatures.FirstOrDefault(),
            DigitalSignatures = digitalSignatures,
            ExpectedHash = ReadExpectedHash(element),
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
        var digitalSignatures = ReadDigitalSignatures(element, xc);

        return new CatalogueDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            FilePath = (string?)element.Element(xc + "filePath"),
            Purpose = (string?)element.Element(xc + "purpose"),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            Scope = (string?)element.Element(xc + "scope"),
            VersionNumber = (string?)element.Element(xc + "versionNumber"),
            IssueDate = (string?)element.Element(xc + "issueDate"),
            ProductSpecification = ReadProductSpecification(element.Element(xc + "productSpecification"), xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            DigitalSignatureAlgorithm = ParseSignatureAlgorithm(element, xc),
            DigitalSignatureValue = digitalSignatures.FirstOrDefault(),
            DigitalSignatures = digitalSignatures,
            ExpectedHash = ReadExpectedHash(element),
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

    /// <summary>
    /// Parses the <c>digitalSignatureReference</c> element value into a
    /// <see cref="DigitalSignatureAlgorithm"/> enum.
    /// </summary>
    private static DigitalSignatureAlgorithm ParseSignatureAlgorithm(XElement parent, XNamespace xc)
    {
        var value = (string?)parent.Element(xc + "digitalSignatureReference");
        return value?.Trim().ToUpperInvariant() switch
        {
            "DSA" => DigitalSignatureAlgorithm.DSA,
            "ECDSA" => DigitalSignatureAlgorithm.ECDSA,
            "ECDSA-384-SHA2" => DigitalSignatureAlgorithm.ECDSA384SHA2,
            _ => DigitalSignatureAlgorithm.Unknown,
        };
    }

    /// <summary>
    /// Parses every digital signature wrapper on a discovery metadata record.
    /// </summary>
    /// <remarks>
    /// S-100 Edition 5.2.1 Part 15 §15-8.8 and §15-8.11.3 through
    /// §15-8.11.6.
    /// </remarks>
    private static IReadOnlyList<DigitalSignatureValue> ReadDigitalSignatures(
        XElement parent,
        XNamespace xc)
    {
        var signatures = new List<DigitalSignatureValue>();
        foreach (var wrapper in parent.Elements(xc + "digitalSignatureValue"))
        {
            var signatureElements = wrapper.Elements().ToList();
            if (signatureElements.Count != 1)
            {
                throw new XmlException(
                    "A digitalSignatureValue element must contain exactly one signature element.");
            }

            var signatureElement = signatureElements[0];
            if (!IsSecurityNamespace(signatureElement.Name.Namespace))
            {
                throw new XmlException(
                    $"Unexpected digital signature namespace '{signatureElement.Name.NamespaceName}'.");
            }

            signatures.Add(ReadDigitalSignature(signatureElement));
        }

        return signatures;
    }

    private static DigitalSignatureValue ReadDigitalSignature(XElement element)
    {
        var kind = element.Name.LocalName switch
        {
            "S100_SE_DigitalSignature" => DigitalSignatureKind.Legacy,
            "S100_SE_SignatureOnData" => DigitalSignatureKind.SignatureOnData,
            "S100_SE_SignatureOnSignature" => DigitalSignatureKind.SignatureOnSignature,
            _ => throw new XmlException(
                $"Unsupported digital signature element '{element.Name.LocalName}'."),
        };
        ValidateSignatureAttributes(element, kind);

        SignatureDataStatus? dataStatus = kind == DigitalSignatureKind.SignatureOnData
            ? ParseDataStatus(RequiredAttribute(element, "dataStatus"))
            : null;
        var signatureRef = kind == DigitalSignatureKind.SignatureOnSignature
            ? RequiredAttribute(element, "signatureRef")
            : null;

        return new DigitalSignatureValue
        {
            Kind = kind,
            Id = RequiredAttribute(element, "id"),
            CertificateRef = RequiredAttribute(element, "certificateRef"),
            Value = ParseSignatureValue(element),
            DataStatus = dataStatus,
            SignatureRef = signatureRef,
        };
    }

    private static void ValidateSignatureAttributes(
        XElement element,
        DigitalSignatureKind kind)
    {
        if (kind != DigitalSignatureKind.SignatureOnData &&
            element.Attribute("dataStatus") is not null)
        {
            throw new XmlException(
                $"Digital signature '{element.Name.LocalName}' cannot declare dataStatus.");
        }

        if (kind != DigitalSignatureKind.SignatureOnSignature &&
            element.Attribute("signatureRef") is not null)
        {
            throw new XmlException(
                $"Digital signature '{element.Name.LocalName}' cannot declare signatureRef.");
        }
    }

    private static SignatureDataStatus ParseDataStatus(string value) =>
        value switch
        {
            "unencrypted" => SignatureDataStatus.Unencrypted,
            "compressed" => SignatureDataStatus.Compressed,
            "encrypted" => SignatureDataStatus.Encrypted,
            _ => throw new XmlException($"Unsupported signature dataStatus '{value}'."),
        };

    private static string RequiredAttribute(XElement element, string localName)
    {
        var value = ((string?)element.Attribute(localName))?.Trim();
        return !string.IsNullOrEmpty(value)
            ? value
            : throw new XmlException(
                $"Digital signature '{element.Name.LocalName}' is missing required attribute '{localName}'.");
    }

    private static byte[] ParseSignatureValue(XElement element)
    {
        var value = element.Value.Trim();
        if (value.Length == 0)
        {
            throw new XmlException(
                $"Digital signature '{element.Name.LocalName}' has an empty value.");
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new XmlException(
                $"Digital signature '{element.Name.LocalName}' is not valid base64.", ex);
        }
    }

    /// <summary>
    /// Discovers an S-100 cryptographic hash MRN
    /// (<c>urn:mrn:iho:s100:hash:&lt;algorithm&gt;:&lt;hex&gt;</c>) declared
    /// within a discovery-metadata element. The specification defines the MRN
    /// namespace but not a fixed catalogue slot, so this scans the element's
    /// descendant values best-effort and returns the first that parses.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    private static CryptographicHash? ReadExpectedHash(XElement element)
    {
        foreach (var node in element.DescendantNodesAndSelf().OfType<XText>())
        {
            if (CryptographicHash.TryParse(node.Value, out var hash))
                return hash;
        }

        foreach (var attribute in element.Descendants().Attributes())
        {
            if (CryptographicHash.TryParse(attribute.Value, out var hash))
                return hash;
        }

        return null;
    }

    /// <summary>
    /// Parses the <c>certificates</c> block containing the scheme administrator
    /// identifier and embedded X.509 certificates.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-5.</remarks>
    private static CertificateBlock? ReadCertificateBlock(XElement? element)
    {
        if (element is null) return null;

        var saId = (string?)element
            .Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "schemeAdministrator" &&
                IsSecurityNamespace(child.Name.Namespace))?
            .Attribute("id");

        var certs = element
            .Elements()
            .Where(child =>
                child.Name.LocalName == "certificate" &&
                IsSecurityNamespace(child.Name.Namespace))
            .Select(c =>
            {
                var id = (string?)c.Attribute("id");
                var issuer = (string?)c.Attribute("issuer");
                var base64 = c.Value.Trim();

                if (id is null || base64.Length == 0)
                    return null;

                return new CertificateEntry
                {
                    Id = id,
                    Issuer = issuer,
                    Value = Convert.FromBase64String(base64),
                };
            })
            .Where(c => c is not null)
            .Cast<CertificateEntry>()
            .ToList();

        return new CertificateBlock
        {
            SchemeAdministratorId = saId,
            Certificates = certs,
        };
    }

    private static bool IsSecurityNamespace(XNamespace xmlNamespace) =>
        S100SecurityNamespaces.Contains(xmlNamespace.NamespaceName);
}
