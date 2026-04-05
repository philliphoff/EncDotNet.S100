using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Portrayals;

public static class PortrayalCatalogueReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";

    public static PortrayalCatalogue Read(Stream stream)
    {
        var doc = XDocument.Load(stream);
        return ReadCatalogue(doc.Root ?? throw new XmlException("Missing root element."));
    }

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

        return new PortrayalCatalogue
        {
            ProductId = productId,
            Version = version,
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
            Description = ReadDescription(element)!,
            FileName = (string)(FindElement(element, "fileName") ?? element.Element(PC + "fileName"))!,
            FileType = (string)(FindElement(element, "fileType") ?? element.Element(PC + "fileType"))!,
            FileFormat = (string)(FindElement(element, "fileFormat") ?? element.Element(PC + "fileFormat"))!,
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
                Id = (string)e.Attribute("id")!,
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
                Id = (string)e.Attribute("id")!,
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
                Id = (string)e.Attribute("id")!,
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
                    Id = (string)e.Attribute("id")!,
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
            Id = (string)element.Attribute("id")!,
            Description = ReadDescription(element),
            Type = (string)FindElement(element, "type")!,
            Default = (string)FindElement(element, "default")!,
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
                Id = (string)e.Attribute("id")!,
                Description = ReadDescription(e),
                FileName = (string)FindElement(e, "fileName")!,
                FileType = (string)FindElement(e, "fileType")!,
                FileFormat = (string)FindElement(e, "fileFormat")!,
                RuleType = (string)FindElement(e, "ruleType")!,
            })
            .ToList();
    }
}
