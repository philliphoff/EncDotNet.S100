using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features.Diagnostics;

namespace EncDotNet.S100.Features;

/// <summary>
/// Parses S-100 Part 5 Feature Catalogue XML into a <see cref="FeatureCatalogue"/>. The
/// <c>S100FC</c>, <c>S100Base</c> and <c>S100CI</c> namespace URIs are taken from the root
/// element's declarations, so versioned namespaces (e.g. <c>http://www.iho.int/S100FC/5.2</c>)
/// are accepted. Each parse emits an <c>s100.featurecatalogue.parse</c> tracing activity.
/// </summary>
public static class FeatureCatalogueReader
{
    // Namespace URIs — resolved from the root element at parse time to handle
    // versioned namespaces (e.g. "http://www.iho.int/S100FC/5.2").
    [ThreadStatic] private static XNamespace? _s100fc;
    [ThreadStatic] private static XNamespace? _s100base;
    [ThreadStatic] private static XNamespace? _s100ci;
    private static XNamespace S100FC => _s100fc ?? "http://www.iho.int/S100FC";
    private static XNamespace S100Base => _s100base ?? "http://www.iho.int/S100Base";
    private static XNamespace S100CI => _s100ci ?? "http://www.iho.int/S100CI";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Reads a Feature Catalogue from an XML stream.</summary>
    /// <param name="stream">Stream positioned at the start of the catalogue XML. It is read to the end but not disposed.</param>
    /// <returns>The parsed catalogue.</returns>
    /// <exception cref="XmlException">
    /// The XML is malformed or has no root element, or a required element or attribute is
    /// missing: the catalogue's <c>name</c> or <c>versionNumber</c>; the
    /// <c>name</c> or <c>code</c> of an attribute, role, association or type (plus a simple
    /// attribute's <c>valueType</c>, or a listed value's <c>label</c> and <c>code</c>); or a
    /// binding's <c>multiplicity</c> (with its <c>S100Base:lower</c>) or the <c>ref</c> of its
    /// <c>attribute</c>, <c>association</c>, <c>role</c>, <c>featureType</c> or
    /// <c>informationType</c> reference. The message names the missing element or attribute
    /// and, where there is one, the <c>code</c> of the enclosing catalogue entry.
    /// </exception>
    /// <exception cref="FormatException">A multiplicity's <c>S100Base:lower</c> is not an integer.</exception>
    /// <remarks>
    /// The document is not validated against the S-100 FC schema. Elements and attributes that
    /// the returned model declares non-nullable are checked for presence, so a catalogue that
    /// lacks one fails with an <see cref="XmlException"/> rather than yielding
    /// <see langword="null"/> in a non-nullable property. The catalogue's <c>versionDate</c> is
    /// lenient and reads as an empty string when absent. Optional elements that are absent
    /// yield <see langword="null"/> or empty collections.
    /// </remarks>
    public static FeatureCatalogue Read(Stream stream)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("s100.featurecatalogue.parse");
        var doc = XDocument.Load(stream);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    /// <summary>Reads a Feature Catalogue from an XML file.</summary>
    /// <param name="path">Path (or URI) of the catalogue XML file.</param>
    /// <returns>The parsed catalogue.</returns>
    /// <exception cref="XmlException">
    /// The XML is malformed or has no root element, or a required element or attribute is
    /// missing: the catalogue's <c>name</c> or <c>versionNumber</c>; the
    /// <c>name</c> or <c>code</c> of an attribute, role, association or type (plus a simple
    /// attribute's <c>valueType</c>, or a listed value's <c>label</c> and <c>code</c>); or a
    /// binding's <c>multiplicity</c> (with its <c>S100Base:lower</c>) or the <c>ref</c> of its
    /// <c>attribute</c>, <c>association</c>, <c>role</c>, <c>featureType</c> or
    /// <c>informationType</c> reference. The message names the missing element or attribute
    /// and, where there is one, the <c>code</c> of the enclosing catalogue entry.
    /// </exception>
    /// <exception cref="FormatException">A multiplicity's <c>S100Base:lower</c> is not an integer.</exception>
    /// <remarks>
    /// The document is not validated against the S-100 FC schema. Elements and attributes that
    /// the returned model declares non-nullable are checked for presence, so a catalogue that
    /// lacks one fails with an <see cref="XmlException"/> rather than yielding
    /// <see langword="null"/> in a non-nullable property. The catalogue's <c>versionDate</c> is
    /// lenient and reads as an empty string when absent. Optional elements that are absent
    /// yield <see langword="null"/> or empty collections.
    /// </remarks>
    /// <exception cref="IOException">The file cannot be opened or read (e.g. <see cref="FileNotFoundException"/>).</exception>
    public static FeatureCatalogue Read(string path)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("s100.featurecatalogue.parse");
        activity?.SetTag("s100.featurecatalogue.path", path);
        var doc = XDocument.Load(path);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    /// <summary>
    /// Resolves the actual namespace URI for a given prefix by scanning
    /// the root element's declared namespaces. Falls back to the base URI
    /// without a version suffix.
    /// </summary>
    private static XNamespace ResolveNamespace(XElement root, string prefix, string fallback)
    {
        foreach (var attr in root.Attributes())
        {
            if (attr.IsNamespaceDeclaration && attr.Name.LocalName == prefix)
            {
                return (XNamespace)attr.Value;
            }
        }

        return fallback;
    }

    private static FeatureCatalogue ReadCatalogue(XElement root)
    {
        // Resolve versioned namespaces from the document's declarations
        _s100fc = ResolveNamespace(root, "S100FC", "http://www.iho.int/S100FC");
        _s100base = ResolveNamespace(root, "S100Base", "http://www.iho.int/S100Base");
        _s100ci = ResolveNamespace(root, "S100CI", "http://www.iho.int/S100CI");
        var versionNumber = RequiredValue(root, S100FC + "versionNumber");
        var productId = (string?)root.Element(S100FC + "productId");
        CatalogueRef? catalogueRef = null;
        if (!string.IsNullOrWhiteSpace(productId)
            && SpecName.TryNormalize(productId, out var canonicalName)
            && SpecVersion.TryParse(versionNumber, out var parsedVersion))
        {
            catalogueRef = new CatalogueRef(canonicalName, parsedVersion);
        }
        return new FeatureCatalogue
        {
            Name = RequiredValue(root, S100FC + "name"),
            Scope = (string?)root.Element(S100FC + "scope"),
            FieldOfApplication = (string?)root.Element(S100FC + "fieldOfApplication"),
            ProductId = productId,
            VersionNumber = versionNumber,
            // versionDate is mandatory in the schema but not needed to use the
            // catalogue; hand-written catalogues often omit it, so read leniently.
            VersionDate = (string?)root.Element(S100FC + "versionDate") ?? "",
            CatalogueRef = catalogueRef,
            Producer = ReadProducer(root.Element(S100FC + "producer")),
            Classification = (string?)root.Element(S100FC + "classification"),
            SimpleAttributes = root
                .Element(S100FC + "S100_FC_SimpleAttributes")?
                .Elements(S100FC + "S100_FC_SimpleAttribute")
                .Select(ReadSimpleAttribute)
                .ToList() ?? [],
            ComplexAttributes = root
                .Element(S100FC + "S100_FC_ComplexAttributes")?
                .Elements(S100FC + "S100_FC_ComplexAttribute")
                .Select(ReadComplexAttribute)
                .ToList() ?? [],
            Roles = root
                .Element(S100FC + "S100_FC_Roles")?
                .Elements(S100FC + "S100_FC_Role")
                .Select(ReadRole)
                .ToList() ?? [],
            InformationAssociations = root
                .Element(S100FC + "S100_FC_InformationAssociations")?
                .Elements(S100FC + "S100_FC_InformationAssociation")
                .Select(ReadInformationAssociation)
                .ToList() ?? [],
            FeatureAssociations = root
                .Element(S100FC + "S100_FC_FeatureAssociations")?
                .Elements(S100FC + "S100_FC_FeatureAssociation")
                .Select(ReadFeatureAssociation)
                .ToList() ?? [],
            InformationTypes = root
                .Element(S100FC + "S100_FC_InformationTypes")?
                .Elements(S100FC + "S100_FC_InformationType")
                .Select(ReadInformationType)
                .ToList() ?? [],
            FeatureTypes = root
                .Element(S100FC + "S100_FC_FeatureTypes")?
                .Elements(S100FC + "S100_FC_FeatureType")
                .Select(ReadFeatureType)
                .ToList() ?? [],
        };
    }

    private static Producer? ReadProducer(XElement? element)
    {
        if (element is null) return null;

        var org = element
            .Element(S100CI + "party")?
            .Element(S100CI + "CI_Organisation");

        var contactInfo = org?.Element(S100CI + "contactInfo");

        return new Producer
        {
            Role = (string?)element.Element(S100CI + "role"),
            OrganisationName = (string?)org?.Element(S100CI + "name"),
            AdministrativeArea = (string?)contactInfo?.Element(S100CI + "address")?.Element(S100CI + "administrativeArea"),
            Country = (string?)contactInfo?.Element(S100CI + "address")?.Element(S100CI + "country"),
            ElectronicMailAddress = (string?)contactInfo?.Element(S100CI + "address")?.Element(S100CI + "electronicMailAddress"),
            Linkage = (string?)contactInfo?.Element(S100CI + "onlineResource")?.Element(S100CI + "linkage"),
        };
    }

    private static SimpleAttribute ReadSimpleAttribute(XElement element)
    {
        return new SimpleAttribute
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            ValueType = RequiredValue(element, S100FC + "valueType"),
            Uom = ReadUnitOfMeasure(element.Element(S100FC + "uom")),
            ListedValues = element
                .Element(S100FC + "listedValues")?
                .Elements(S100FC + "listedValue")
                .Select(ReadListedValue)
                .ToList() ?? [],
        };
    }

    private static ListedValue ReadListedValue(XElement element)
    {
        return new ListedValue
        {
            Label = RequiredValue(element, S100FC + "label"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
        };
    }

    /// <summary>
    /// Reads a simple attribute's <c>&lt;S100FC:uom&gt;</c> element into a
    /// <see cref="UnitOfMeasure"/>, or returns <c>null</c> when the element
    /// is absent or carries no <c>S100Base:name</c>.
    /// </summary>
    private static UnitOfMeasure? ReadUnitOfMeasure(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var name = (string?)element.Element(S100Base + "name");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return new UnitOfMeasure(name, (string?)element.Element(S100Base + "symbol"));
    }

    private static ComplexAttribute ReadComplexAttribute(XElement element)
    {
        return new ComplexAttribute
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            SubAttributeBindings = element
                .Elements(S100FC + "subAttributeBinding")
                .Select(ReadSubAttributeBinding)
                .ToList(),
        };
    }

    private static SubAttributeBinding ReadSubAttributeBinding(XElement element)
    {
        return new SubAttributeBinding
        {
            Multiplicity = ReadMultiplicity(RequiredElement(element, S100FC + "multiplicity")),
            AttributeRef = RequiredRef(element, S100FC + "attribute"),
            Sequential = string.Equals((string?)element.Attribute("sequential"), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static Role ReadRole(XElement element)
    {
        return new Role
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
        };
    }

    private static InformationAssociation ReadInformationAssociation(XElement element)
    {
        return new InformationAssociation
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
            RoleRefs = element
                .Elements(S100FC + "role")
                .Select(e => RequiredAttribute(e, "ref"))
                .ToList(),
        };
    }

    private static FeatureAssociation ReadFeatureAssociation(XElement element)
    {
        return new FeatureAssociation
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
            RoleRefs = element
                .Elements(S100FC + "role")
                .Select(e => RequiredAttribute(e, "ref"))
                .ToList(),
        };
    }

    private static InformationType ReadInformationType(XElement element)
    {
        return new InformationType
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
            SuperType = (string?)element.Element(S100FC + "superType"),
            AttributeBindings = element
                .Elements(S100FC + "attributeBinding")
                .Select(ReadAttributeBinding)
                .ToList(),
            InformationBindings = element
                .Elements(S100FC + "informationBinding")
                .Select(ReadInformationBinding)
                .ToList(),
        };
    }

    private static FeatureType ReadFeatureType(XElement element)
    {
        return new FeatureType
        {
            Name = RequiredValue(element, S100FC + "name"),
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = RequiredValue(element, S100FC + "code"),
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
            SuperType = (string?)element.Element(S100FC + "superType"),
            AttributeBindings = element
                .Elements(S100FC + "attributeBinding")
                .Select(ReadAttributeBinding)
                .ToList(),
            FeatureBindings = element
                .Elements(S100FC + "featureBinding")
                .Select(ReadFeatureBinding)
                .ToList(),
            InformationBindings = element
                .Elements(S100FC + "informationBinding")
                .Select(ReadInformationBinding)
                .ToList(),
            FeatureUseType = (string?)element.Element(S100FC + "featureUseType"),
            PermittedPrimitives = element
                .Elements(S100FC + "permittedPrimitives")
                .Select(e => (string)e)
                .ToList(),
        };
    }

    private static AttributeBinding ReadAttributeBinding(XElement element)
    {
        return new AttributeBinding
        {
            Multiplicity = ReadMultiplicity(RequiredElement(element, S100FC + "multiplicity")),
            AttributeRef = RequiredRef(element, S100FC + "attribute"),
            Sequential = string.Equals((string?)element.Attribute("sequential"), "true", StringComparison.OrdinalIgnoreCase),
            PermittedValues = element
                .Element(S100FC + "permittedValues")?
                .Elements(S100FC + "value")
                .Select(e => (string)e)
                .ToList() ?? [],
        };
    }

    private static FeatureBinding ReadFeatureBinding(XElement element)
    {
        return new FeatureBinding
        {
            Multiplicity = ReadMultiplicity(RequiredElement(element, S100FC + "multiplicity")),
            AssociationRef = RequiredRef(element, S100FC + "association"),
            RoleRef = RequiredRef(element, S100FC + "role"),
            FeatureTypeRef = RequiredRef(element, S100FC + "featureType"),
            FeatureTypeRefs = element.Elements(S100FC + "featureType").Select(e => RequiredAttribute(e, "ref")).ToList(),
            RoleType = (string?)element.Attribute("roleType"),
        };
    }

    private static InformationBinding ReadInformationBinding(XElement element)
    {
        return new InformationBinding
        {
            Multiplicity = ReadMultiplicity(RequiredElement(element, S100FC + "multiplicity")),
            AssociationRef = RequiredRef(element, S100FC + "association"),
            RoleRef = RequiredRef(element, S100FC + "role"),
            InformationTypeRef = RequiredRef(element, S100FC + "informationType"),
            InformationTypeRefs = element.Elements(S100FC + "informationType").Select(e => RequiredAttribute(e, "ref")).ToList(),
            RoleType = (string?)element.Attribute("roleType"),
        };
    }

    private static Multiplicity ReadMultiplicity(XElement element)
    {
        var upperElement = element.Element(S100Base + "upper");
        var isNil = string.Equals((string?)upperElement?.Attribute(Xsi + "nil"), "true", StringComparison.OrdinalIgnoreCase);
        var isInfinite = string.Equals((string?)upperElement?.Attribute("infinite"), "true", StringComparison.OrdinalIgnoreCase);

        int? upper = null;
        if (upperElement is not null && !isNil && !isInfinite)
        {
            if (int.TryParse(upperElement.Value, out var u))
            {
                upper = u;
            }
        }

        return new Multiplicity
        {
            Lower = (int)RequiredElement(element, S100Base + "lower"),
            Upper = upper,
            IsInfinite = isInfinite,
        };
    }

    private static XElement RequiredElement(XElement parent, XName name) =>
        parent.Element(name)
            ?? throw new XmlException(
                $"Feature catalogue element '{parent.Name.LocalName}'{DescribeOwner(parent)} is missing required element '{name.LocalName}'.");

    private static string RequiredValue(XElement parent, XName name) => (string)RequiredElement(parent, name);

    private static string RequiredAttribute(XElement element, string localName) =>
        (string?)element.Attribute(localName)
            ?? throw new XmlException(
                $"Feature catalogue element '{element.Name.LocalName}'{DescribeOwner(element)} is missing required attribute '{localName}'.");

    private static string RequiredRef(XElement parent, XName name) =>
        RequiredAttribute(RequiredElement(parent, name), "ref");

    /// <summary>
    /// Names the nearest enclosing catalogue entry (the closest element,
    /// starting at <paramref name="element"/>, with an <c>S100FC:code</c>
    /// child) so a missing-element error can be located in a large catalogue.
    /// </summary>
    private static string DescribeOwner(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            var code = (string?)current.Element(S100FC + "code");
            if (!string.IsNullOrWhiteSpace(code))
                return $" in '{code.Trim()}'";
        }

        return "";
    }
}
