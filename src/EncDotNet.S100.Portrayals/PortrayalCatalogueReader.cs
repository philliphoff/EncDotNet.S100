using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Parses S-100 Part 9 Portrayal Catalogue XML (<c>portrayal_catalogue.xml</c>)
/// into a <see cref="PortrayalCatalogue"/>. Elements are matched both
/// unqualified and in the <c>http://www.iho.int/S100PortrayalCatalog/5.2</c>
/// namespace. Only the catalogue document is read; referenced asset and rule
/// files are not opened.
/// </summary>
public static class PortrayalCatalogueReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";

    /// <summary>Reads a portrayal catalogue from an XML stream.</summary>
    /// <param name="stream">Stream positioned at the start of the catalogue XML. It is not disposed.</param>
    /// <returns>The parsed catalogue.</returns>
    /// <exception cref="XmlException">
    /// The XML is malformed or has no root element, or a required element or
    /// attribute is missing: the <c>fileName</c>, <c>fileType</c> or
    /// <c>fileFormat</c> of a catalogue item or rule file, a rule file's
    /// <c>ruleType</c>, a context parameter's <c>type</c> or <c>default</c>, or
    /// the <c>id</c> of a rule file, context parameter, viewing group layer,
    /// display mode or display plane. The message names the missing element or
    /// attribute.
    /// </exception>
    /// <remarks>
    /// The document is not validated against the Part 9 schema. Elements and
    /// attributes that the returned model declares non-nullable are checked for
    /// presence, so a catalogue that lacks one fails with an
    /// <see cref="XmlException"/> rather than yielding <see langword="null"/> in a
    /// non-nullable property. The root <c>productId</c> and <c>version</c>, a
    /// catalogue item's <c>id</c> and a <c>description</c>'s <c>name</c> are
    /// lenient and read as empty strings when absent; <c>viewingGroup</c> entries
    /// without an <c>id</c> are treated as references and skipped.
    /// </remarks>
    public static PortrayalCatalogue Read(Stream stream)
    {
        var doc = XDocument.Load(stream);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    /// <summary>Reads a portrayal catalogue from an XML file or URI.</summary>
    /// <param name="path">Path or URI of the catalogue XML file.</param>
    /// <returns>The parsed catalogue.</returns>
    /// <exception cref="XmlException">
    /// The XML is malformed or has no root element, or a required element or
    /// attribute is missing: the <c>fileName</c>, <c>fileType</c> or
    /// <c>fileFormat</c> of a catalogue item or rule file, a rule file's
    /// <c>ruleType</c>, a context parameter's <c>type</c> or <c>default</c>, or
    /// the <c>id</c> of a rule file, context parameter, viewing group layer,
    /// display mode or display plane. The message names the missing element or
    /// attribute.
    /// </exception>
    /// <exception cref="IOException">The file cannot be read (e.g. it does not exist).</exception>
    /// <remarks>
    /// See <see cref="Read(Stream)"/> for which elements are required and which
    /// are read leniently.
    /// </remarks>
    public static PortrayalCatalogue Read(string path)
    {
        var doc = XDocument.Load(path);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

    private static PortrayalCatalogue ReadCatalogue(XElement root)
    {
        // The root element may be namespace-prefixed or not; resolve it.
        string productId = (string?)root.Attribute("productId") ?? "";
        string version = (string?)root.Attribute("version") ?? "";

        CatalogueRef? catalogueRef = null;
        if (!string.IsNullOrWhiteSpace(productId)
            && SpecName.TryNormalize(productId, out var canonicalName)
            && SpecVersion.TryParse(version, out var parsedVersion))
        {
            catalogueRef = new CatalogueRef(canonicalName, parsedVersion);
        }

        return new PortrayalCatalogue
        {
            ProductId = productId,
            Version = version,
            CatalogueRef = catalogueRef,
            AlertCatalog = ReadCatalogItem(root.Element(Unqualified("alertCatalog")) ?? root.Element(PC + "alertCatalog")),
            Pixmaps = ReadCatalogItems(root, "pixmaps", "pixmap"),
            ColorProfiles = ReadCatalogItems(root, "colorProfiles", "colorProfile"),
            Symbols = ReadCatalogItems(root, "symbols", "symbol"),
            StyleSheets = ReadCatalogItems(root, "styleSheets", "styleSheet"),
            LineStyles = ReadCatalogItems(root, "lineStyles", "lineStyle"),
            AreaFills = ReadCatalogItems(root, "areaFills", "areaFill"),
            ViewingGroups = ReadViewingGroups(root),
            FoundationModeViewingGroupIds = ReadFoundationMode(root),
            ViewingGroupLayers = ReadViewingGroupLayers(root),
            DisplayModes = ReadDisplayModes(root),
            DisplayPlanes = ReadDisplayPlanes(root),
            ContextParameters = ReadContextParameters(root),
            RuleFiles = ReadRuleFiles(root),
        };
    }

    private static XName Unqualified(string localName) => XName.Get(localName);

    private static XElement? FindElement(XElement parent, string localName)
    {
        return parent.Element(Unqualified(localName)) ?? parent.Element(PC + localName);
    }

    private static IEnumerable<XElement> FindElements(XElement parent, string localName)
    {
        var unqualified = parent.Elements(Unqualified(localName));
        var qualified = parent.Elements(PC + localName);
        return unqualified.Concat(qualified);
    }

    private static string RequiredElementValue(XElement parent, string localName) =>
        (string?)FindElement(parent, localName)
            ?? throw new XmlException(
                $"Portrayal catalogue element '{parent.Name.LocalName}' is missing required element '{localName}'.");

    private static string RequiredAttribute(XElement element, string localName) =>
        (string?)element.Attribute(localName)
            ?? throw new XmlException(
                $"Portrayal catalogue element '{element.Name.LocalName}' is missing required attribute '{localName}'.");

    private static List<CatalogItem> ReadCatalogItems(XElement root, string containerName, string itemName)
    {
        var container = FindElement(root, containerName);
        if (container is null) return [];

        return FindElements(container, itemName)
            .Select(ReadCatalogItem)
            .Where(item => item is not null)
            .Cast<CatalogItem>()
            .ToList();
    }

    private static CatalogItem? ReadCatalogItem(XElement? element)
    {
        if (element is null) return null;

        return new CatalogItem
        {
            Id = (string?)element.Attribute("id") ?? "",
            Description = ReadDescription(element),
            FileName = RequiredElementValue(element, "fileName"),
            FileType = RequiredElementValue(element, "fileType"),
            FileFormat = RequiredElementValue(element, "fileFormat"),
        };
    }

    private static Description ReadDescription(XElement parent)
    {
        var desc = FindElement(parent, "description");
        if (desc is null)
        {
            return new Description { Name = "" };
        }

        return new Description
        {
            Name = (string?)FindElement(desc, "name") ?? "",
            DescriptionText = (string?)FindElement(desc, "description"),
            Language = (string?)FindElement(desc, "language"),
        };
    }

    private static List<ViewingGroup> ReadViewingGroups(XElement root)
    {
        var container = FindElement(root, "viewingGroups");
        if (container is null) return [];

        return FindElements(container, "viewingGroup")
            .Where(e => e.Attribute("id") is not null) // skip viewing group ID references
            .Select(e => new ViewingGroup
            {
                Id = RequiredAttribute(e, "id"),
                Description = ReadDescription(e),
            })
            .ToList();
    }

    private static List<string> ReadFoundationMode(XElement root)
    {
        var container = FindElement(root, "foundationMode");
        if (container is null) return [];

        return FindElements(container, "viewingGroup")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToList();
    }

    private static List<ViewingGroupLayer> ReadViewingGroupLayers(XElement root)
    {
        var container = FindElement(root, "viewingGroupLayers");
        if (container is null) return [];

        return FindElements(container, "viewingGroupLayer")
            .Select(e => new ViewingGroupLayer
            {
                Id = RequiredAttribute(e, "id"),
                Description = ReadDescription(e),
                ViewingGroupIds = FindElements(e, "viewingGroup")
                    .Select(vg => vg.Value.Trim())
                    .Where(v => v.Length > 0)
                    .ToList(),
            })
            .ToList();
    }

    private static List<DisplayMode> ReadDisplayModes(XElement root)
    {
        var container = FindElement(root, "displayModes");
        if (container is null) return [];

        return FindElements(container, "displayMode")
            .Select(e => new DisplayMode
            {
                Id = RequiredAttribute(e, "id"),
                Description = ReadDescription(e),
                ViewingGroupLayerIds = FindElements(e, "viewingGroupLayer")
                    .Select(vgl => vgl.Value.Trim())
                    .Where(v => v.Length > 0)
                    .ToList(),
            })
            .ToList();
    }

    private static List<DisplayPlane> ReadDisplayPlanes(XElement root)
    {
        var container = FindElement(root, "displayPlanes");
        if (container is null) return [];

        return FindElements(container, "displayPlane")
            .Select(e =>
            {
                var orderAttr = (string?)e.Attribute("order");
                int? order = int.TryParse(orderAttr, out var o) ? o : null;

                return new DisplayPlane
                {
                    Id = RequiredAttribute(e, "id"),
                    Order = order,
                    Description = ReadDescription(e),
                };
            })
            .ToList();
    }

    private static List<ContextParameter> ReadContextParameters(XElement root)
    {
        var container = FindElement(root, "context");
        if (container is null) return [];

        return FindElements(container, "parameter")
            .Select(ReadContextParameter)
            .ToList();
    }

    private static ContextParameter ReadContextParameter(XElement element)
    {
        var validate = FindElement(element, "validate");

        ContextParameterValidation? validation = null;
        if (validate is not null)
        {
            var errorMessageElement = FindElement(validate, "errorMessage");
            validation = new ContextParameterValidation
            {
                XPath = (string?)FindElement(validate, "xpath"),
                Regex = (string?)FindElement(validate, "regex"),
                ErrorMessage = errorMessageElement is not null
                    ? (string?)FindElement(errorMessageElement, "text")
                    : null,
            };
        }

        return new ContextParameter
        {
            Id = RequiredAttribute(element, "id"),
            Description = ReadDescription(element),
            Type = RequiredElementValue(element, "type"),
            Default = RequiredElementValue(element, "default"),
            Enable = (string?)element.Attribute("enable"),
            Validation = validation,
        };
    }

    private static List<RuleFile> ReadRuleFiles(XElement root)
    {
        var container = FindElement(root, "rules");
        if (container is null) return [];

        return FindElements(container, "ruleFile")
            .Select(e => new RuleFile
            {
                Id = RequiredAttribute(e, "id"),
                Description = ReadDescription(e),
                FileName = RequiredElementValue(e, "fileName"),
                FileType = RequiredElementValue(e, "fileType"),
                FileFormat = RequiredElementValue(e, "fileFormat"),
                RuleType = RequiredElementValue(e, "ruleType"),
            })
            .ToList();
    }
}
