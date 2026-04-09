using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Features;

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

    public static FeatureCatalogue Read(Stream stream)
    {
        var doc = XDocument.Load(stream);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    public static FeatureCatalogue Read(string path)
    {
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
        return new FeatureCatalogue
        {
            Name = (string)root.Element(S100FC + "name")!,
            Scope = (string?)root.Element(S100FC + "scope"),
            FieldOfApplication = (string?)root.Element(S100FC + "fieldOfApplication"),
            VersionNumber = (string)root.Element(S100FC + "versionNumber")!,
            VersionDate = (string)root.Element(S100FC + "versionDate")!,
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
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            ValueType = (string)element.Element(S100FC + "valueType")!,
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
            Label = (string)element.Element(S100FC + "label")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
        };
    }

    private static ComplexAttribute ReadComplexAttribute(XElement element)
    {
        return new ComplexAttribute
        {
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
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
            Multiplicity = ReadMultiplicity(element.Element(S100FC + "multiplicity")!),
            AttributeRef = (string)element.Element(S100FC + "attribute")!.Attribute("ref")!,
            Sequential = string.Equals((string?)element.Attribute("sequential"), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static Role ReadRole(XElement element)
    {
        return new Role
        {
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
        };
    }

    private static InformationAssociation ReadInformationAssociation(XElement element)
    {
        return new InformationAssociation
        {
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
            RoleRefs = element
                .Elements(S100FC + "role")
                .Select(e => (string)e.Attribute("ref")!)
                .ToList(),
        };
    }

    private static FeatureAssociation ReadFeatureAssociation(XElement element)
    {
        return new FeatureAssociation
        {
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
            RoleRefs = element
                .Elements(S100FC + "role")
                .Select(e => (string)e.Attribute("ref")!)
                .ToList(),
        };
    }

    private static InformationType ReadInformationType(XElement element)
    {
        return new InformationType
        {
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
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
            Name = (string)element.Element(S100FC + "name")!,
            Definition = (string?)element.Element(S100FC + "definition"),
            Code = (string)element.Element(S100FC + "code")!,
            Alias = (string?)element.Element(S100FC + "alias"),
            Remarks = (string?)element.Element(S100FC + "remarks"),
            IsAbstract = string.Equals((string?)element.Attribute("isAbstract"), "true", StringComparison.OrdinalIgnoreCase),
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
            Multiplicity = ReadMultiplicity(element.Element(S100FC + "multiplicity")!),
            AttributeRef = (string)element.Element(S100FC + "attribute")!.Attribute("ref")!,
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
            Multiplicity = ReadMultiplicity(element.Element(S100FC + "multiplicity")!),
            AssociationRef = (string)element.Element(S100FC + "association")!.Attribute("ref")!,
            RoleRef = (string)element.Element(S100FC + "role")!.Attribute("ref")!,
            FeatureTypeRef = (string)element.Element(S100FC + "featureType")!.Attribute("ref")!,
            RoleType = (string?)element.Attribute("roleType"),
        };
    }

    private static InformationBinding ReadInformationBinding(XElement element)
    {
        return new InformationBinding
        {
            Multiplicity = ReadMultiplicity(element.Element(S100FC + "multiplicity")!),
            AssociationRef = (string)element.Element(S100FC + "association")!.Attribute("ref")!,
            RoleRef = (string)element.Element(S100FC + "role")!.Attribute("ref")!,
            InformationTypeRef = (string)element.Element(S100FC + "informationType")!.Attribute("ref")!,
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
            Lower = (int)element.Element(S100Base + "lower")!,
            Upper = upper,
            IsInfinite = isInfinite,
        };
    }
}
