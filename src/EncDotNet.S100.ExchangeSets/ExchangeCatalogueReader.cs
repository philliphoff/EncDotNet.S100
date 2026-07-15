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
    private static readonly XNamespace S100SE = "http://www.iho.int/s100/se/5.0";

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
        XNamespace lan = root.GetNamespaceOfPrefix("lan") ?? "http://standards.iso.org/iso/19115/-3/lan/1.0";

        var identifierEl = root.Element(xc + "identifier")!;

        return new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = (string)identifierEl.Element(xc + "identifier")!,
                DateTime = (string)identifierEl.Element(xc + "dateTime")!,
            },
            Contact = ReadContact(root.Element(xc + "contact"), xc),
            ProductSpecification = ReadProductSpecification(root.Element(xc + "productSpecification"), xc),
            DefaultLocale = ReadPTLocale(root.Element(xc + "defaultLocale"), lan),
            OtherLocales = root.Elements(xc + "otherLocale").Select(e => ReadPTLocales(e, lan)).ToList(),
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
        CompliancyCategory? comp = null;
        string? compStr = (string?)element.Element(xc + "compliancyCategory");
        if (compStr != null)
            comp = (CompliancyCategory)Enum.Parse(typeof(CompliancyCategory), compStr);
        return new ProductSpecification
        {
            Name = (string?)element.Element(xc + "name"),
            Version = (string?)element.Element(xc + "version"),
            Date = ParseDate((string?)element.Element(xc + "date")),
            ProductIdentifier = (string?)element.Element(xc + "productIdentifier"),
            Number = int.TryParse(numberStr, CultureInfo.InvariantCulture, out var n) ? n : null,
            CompliancyCategory = comp,
        };
    }

    private static DatasetDiscoveryMetadata ReadDatasetDiscovery(XElement element, XNamespace xc, XNamespace lan)
    {
        Purpose? purpose = null;
        string? purposeStr = (string?)element.Element(xc + "purpose");
        if (!string.IsNullOrWhiteSpace(purposeStr) &&  Enum.TryParse<Purpose>(purposeStr, ignoreCase: true, out var parsedPurpose))
            purpose = (Purpose)Enum.Parse(typeof(Purpose), purposeStr);


        NavigationPurpose? navigationPurpose = null;
        string? navPurposeStr = (string?)element.Element(xc + "navigationPurpose");
        if (!string.IsNullOrWhiteSpace(navPurposeStr) && Enum.TryParse<NavigationPurpose>(navPurposeStr, ignoreCase: true, out var parsedNavigationPurpose))
            navigationPurpose = parsedNavigationPurpose;
        return new DatasetDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            FilePath = (string?)element.Element(xc + "filePath"),
            Description = ReadCharacterString(element.Element(xc + "description")),
            DatasetId = (string?)element.Element(xc + "datasetID"),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DataProtection = ParseBool(element, "dataProtection", xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            DigitalSignatureAlgorithm = ParseSignatureAlgorithm(element, xc),
            DigitalSignatureValue = ReadDigitalSignatureValue(element.Element(xc + "digitalSignatureValue")),
            ExpectedHash = ReadExpectedHash(element),
            Copyright = ParseBool(element, "copyright", xc),
            Classification = ReadCodeListValue(element.Element(xc + "classification")),
            Purpose = purpose,
            NotForNavigation = ParseBool(element, "notForNavigation", xc),
            SpecificUsage = ReadSpecificUsage(element.Element(xc + "specificUsage")),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            UpdateNumber = ParseInt(element, "updateNumber", xc),
            UpdateApplicationDate = ParseDate((string?)element.Element(xc + "updateApplicationDate")),
            ReferenceId = (string?)element.Element(xc + "referenceID"),
            IssueDate = ParseDate((string?)element.Element(xc + "issueDate")),
            IssueTime = ParseTime((string?)element.Element(xc + "issueTime")),
            BoundingBox = ReadBoundingBox(element.Element(xc + "boundingBox")),
            TemporalExtent = ReadTempoalExtent(element, xc),
            ProductSpecification = ReadProductSpecification(element.Element(xc + "productSpecification"), xc),
            ProducingAgency = ReadProducingAgency(element.Element(xc + "producingAgency")),
            EncodingFormat = (string?)element.Element(xc + "encodingFormat"),
            DataCoverages = element
                .Elements(xc + "dataCoverage")
                .Select(e => ReadDataCoverage(e, xc))
                .ToList(),
            Comment = (string?)element.Element(xc + "comment"),
            DefaultLocale = ReadPTLocale(element.Element(xc + "defaultLocale"), lan),
            OtherLocales = element.Elements(xc + "otherLocale").Select(e => ReadPTLocales(e, lan)).ToList(),
            MetadataDateStamp = ParseDate((string?)element.Element(xc + "metadataDateStamp")),
            ReplaceData = ParseBool(element, "replaceData", xc),
            NavigationPurpose = navigationPurpose,
            ResourceMaintenance = ReadResourceMaintenance(element.Element(xc + "resourceMaintenance"))
        };
    }

    private static TemporalExtent? ReadTempoalExtent(XElement element, XNamespace xc)
    {
        var timeInstantEl = element.Element(xc + "temporalExtent");
        if (timeInstantEl is null) return null;
        var beginStr = (string?)timeInstantEl.Element(xc + "timeInstantBegin");
        var endStr = (string?)timeInstantEl.Element(xc + "timeInstantEnd");
        DateTime? begin = null;
        DateTime? end = null;
        if (DateTime.TryParse(beginStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var b))
            begin = b;
        if (DateTime.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var e))
            end = e;
        return new TemporalExtent
        {
            TimeInstantBegin = begin,
            TimeInstantEnd = end
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
            DigitalSignatureValue = ReadDigitalSignatureValue(element.Element(xc + "digitalSignatureValue")),
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
        return new CatalogueDiscoveryMetadata
        {
            FileName = (string)element.Element(xc + "fileName")!,
            FilePath = (string?)element.Element(xc + "filePath"),
            Purpose = (string?)element.Element(xc + "purpose"),
            EditionNumber = ParseInt(element, "editionNumber", xc),
            Scope = (string?)element.Element(xc + "scope"),
            VersionNumber = (string?)element.Element(xc + "versionNumber"),
            IssueDate = ParseDate((string?)element.Element(xc + "issueDate")),
            ProductSpecification = ReadProductSpecification(element.Element(xc + "productSpecification"), xc),
            DigitalSignatureReference = (string?)element.Element(xc + "digitalSignatureReference"),
            DigitalSignatureAlgorithm = ParseSignatureAlgorithm(element, xc),
            DigitalSignatureValue = ReadDigitalSignatureValue(element.Element(xc + "digitalSignatureValue")),
            ExpectedHash = ReadExpectedHash(element),
            CompressionFlag = ParseBool(element, "compressionFlag", xc),
            DefaultLocale = ReadPTLocale(element.Element(xc + "defaultLocale"), lan),
            OtherLocales = element.Elements(xc + "otherLocale").Select(e => ReadPTLocales(e, lan)).ToList(),
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
        var optStr = (string?)element.Element(xc + "optimumDisplayScale");
        var resStr = (string?)element.Element(xc + "approximateGridResolution");
        TemporalExtent? temporalExtent = ReadTempoalExtent(element, xc);

        return new DataCoverage
        {
            BoundingPolygon = element.Element(xc + "boundingPolygon")?.ToString(),
            MaximumDisplayScale = int.TryParse(maxStr, CultureInfo.InvariantCulture, out var max) ? max : null,
            MinimumDisplayScale = int.TryParse(minStr, CultureInfo.InvariantCulture, out var min) ? min : null,
            OptimumDisplayScale = int.TryParse(optStr, CultureInfo.InvariantCulture, out var opt) ? opt : null,
            ApproximateGridResolution = float.TryParse(resStr, CultureInfo.InvariantCulture, out var res) ? res : null,
            TemporalExtent = temporalExtent
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
            _ => DigitalSignatureAlgorithm.Unknown,
        };
    }

    /// <summary>
    /// Parses an <c>S100_SE_DigitalSignature</c> element nested inside a
    /// <c>digitalSignatureValue</c> wrapper.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-4.2.</remarks>
    private static DigitalSignatureValue? ReadDigitalSignatureValue(XElement? wrapper)
    {
        if (wrapper is null) return null;

        var sigEl = wrapper.Element(S100SE + "S100_SE_DigitalSignature");
        if (sigEl is null) return null;

        var id = (string?)sigEl.Attribute("id");
        var certRef = (string?)sigEl.Attribute("certificateRef");
        var base64 = sigEl.Value.Trim();

        if (id is null || certRef is null || base64.Length == 0)
            return null;

        return new DigitalSignatureValue
        {
            Id = id,
            CertificateRef = certRef,
            Value = Convert.FromBase64String(base64),
        };
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

        var saId = (string?)element.Element(S100SE + "schemeAdministrator")?.Attribute("id");

        var certs = element
            .Elements(S100SE + "certificate")
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
            .ToList()!;

        return new CertificateBlock
        {
            SchemeAdministratorId = saId,
            Certificates = certs!,
        };
    }

    private static MaintenanceInformation? ReadResourceMaintenance(XElement? wrapper)
    {
        if (wrapper is null) return null;

        var sigEl = wrapper.Element(S100SE + "resourceMaintenance");
        if (sigEl is null) return null;

        var mfreq = (string?)sigEl.Attribute("maintenanceAndUpdateFrequency");
        var date = (string?)sigEl.Attribute("maintenanceDate");
        var ufreq = (string?)sigEl.Attribute("userDefinedMaintenanceFrequency");

        MaintenanceFrequencyCode? maintFreq = null;
        if (mfreq != null)
            maintFreq = (MaintenanceFrequencyCode)Enum.Parse(typeof(MaintenanceFrequencyCode), mfreq);

        return new MaintenanceInformation
        {
            MaintenanceAndUpdateFrequency = maintFreq,
            MaintenanceDate = ParseDate(date),
            UserDefinedMaintenanceFrequency = ufreq
        };
    }


    private static PT_Locale? ReadPTLocale(XElement? localeElement, XNamespace lan)
    {  
        if (localeElement is null) return null;

        XElement? moreLocal = localeElement.Element(lan + "PT_Locale");

        var langCode = moreLocal?
           .Elements(lan + "language")
           .FirstOrDefault()?
           .Elements(lan + "LanguageCode")
           .FirstOrDefault();

        var lang = (string?)langCode?.Attribute("codeListValue");
        if (lang == null)
            lang = "";


        var countryCode = moreLocal?
           .Elements(lan + "country")
           .FirstOrDefault()?
           .Elements(lan + "CountryCode")
           .FirstOrDefault();

        var country = (string?)countryCode?.Attribute("codeListValue");
        if (country == null)
            country = "";

        var charEncode = moreLocal?
           .Elements(lan + "characterEncoding")
           .FirstOrDefault()?
           .Elements(lan + "MD_CharacterSetCode")
           .FirstOrDefault();

        var encoding = (string?)charEncode?.Attribute("codeListValue");
        if (encoding == null)
            encoding = "";
       
        return new PT_Locale
        {
            Language = lang,
            Country = country,
            CharacterEncoding =encoding,
        };
    }

    private static PT_Locale ReadPTLocales(XElement localeElement, XNamespace lan)
    {
        XElement? moreLocal = localeElement.Element(lan + "PT_Locale");

        var langCode = moreLocal?
           .Elements(lan + "language")
           .FirstOrDefault()?
           .Elements(lan + "LanguageCode")
           .FirstOrDefault();

        var lang = (string?)langCode?.Attribute("codeListValue");
        if (lang == null)
            lang = "";


        var countryCode = moreLocal?
           .Elements(lan + "country")
           .FirstOrDefault()?
           .Elements(lan + "CountryCode")
           .FirstOrDefault();

        var country = (string?)countryCode?.Attribute("codeListValue");
        if (country == null)
            country = "";

        var charEncode = moreLocal?
           .Elements(lan + "characterEncoding")
           .FirstOrDefault()?
           .Elements(lan + "MD_CharacterSetCode")
           .FirstOrDefault();

        var encoding = (string?)charEncode?.Attribute("codeListValue");
        if (encoding == null)
            encoding = "";

        return new PT_Locale
        {
            Language = lang,
            Country = country,
            CharacterEncoding = encoding,
        };
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Permit dates are xs:date and may carry a trailing 'Z' (e.g. 2018-03-20Z).
        string trimmed = value.Trim().TrimEnd('Z', 'z');
        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }


    private static TimeOnly? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Permit dates are xs:date and may carry a trailing 'Z' (e.g. 2018-03-20Z).
        string trimmed = value.Trim().TrimEnd('Z', 'z');
        return TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time)
            ? time
            : null;
    }
}
