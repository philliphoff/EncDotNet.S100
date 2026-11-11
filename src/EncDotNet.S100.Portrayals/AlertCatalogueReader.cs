using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 alert catalogue XML file (S-100 Part 9 §9-17, the
/// <c>S100AlertCatalog</c> schema) into an <see cref="AlertCatalogue"/>.
/// </summary>
/// <remarks>
/// Elements are matched by local name, so any edition of the alert catalogue
/// namespace is read. Unknown priorities are skipped, and a use with none
/// falls back to <see cref="AlertPriority.Indication"/>.
/// </remarks>
public static class AlertCatalogueReader
{
    /// <summary>Reads an alert catalogue from a stream.</summary>
    /// <param name="stream">The alert catalogue XML.</param>
    /// <returns>The alert catalogue.</returns>
    /// <exception cref="XmlException">The stream isn't an alert catalogue.</exception>
    public static AlertCatalogue Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var root = XDocument.Load(stream).Root ?? throw new XmlException("Missing root element.");
        if (root.Name.LocalName != "alertCatalog")
        {
            throw new XmlException($"Expected an alertCatalog root element, not {root.Name.LocalName}.");
        }

        var messages = new Dictionary<string, AlertMessage>(StringComparer.Ordinal);
        foreach (var message in Children(Child(root, "messages"), "message"))
        {
            var id = (string?)message.Attribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                messages[id] = new AlertMessage(id, Child(message, "text")?.Value.Trim() ?? "");
            }
        }

        var highlights = Children(Child(root, "highlights"), "highlight")
            .Select(h => new AlertHighlight(
                (string?)h.Attribute("id") ?? "",
                (string?)h.Attribute("style") ?? "",
                (bool?)h.Attribute("optional") ?? false,
                (string?)h.Attribute("msg"),
                Children(h, "viewingGroup")
                    .Select(v => int.TryParse((string?)v.Attribute("ref"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) ? group : (int?)null)
                    .OfType<int>()
                    .ToList()))
            .ToList();

        var alerts = Children(Child(root, "alerts"), "alert")
            .Select(a => new AlertDefinition(
                (string?)a.Attribute("id") ?? "",
                Child(Child(a, "description"), "name")?.Value.Trim() ?? "",
                ReadUse(Child(a, "routePlan")),
                ReadUse(Child(a, "routeMonitor"))))
            .ToList();

        return new AlertCatalogue
        {
            Version = (string?)root.Attribute("version") ?? "",
            Alerts = alerts,
            Highlights = highlights,
            Messages = messages,
        };
    }

    // <routePlan msg=".." highlight=".."> holds one <priority>, or a
    // <priorities> list whose default="true" entry is the default.
    private static AlertUse? ReadUse(XElement? use)
    {
        if (use is null)
        {
            return null;
        }

        var priorities = new List<AlertPriority>();
        AlertPriority? preferred = null;
        foreach (var element in use.Descendants().Where(e => e.Name.LocalName == "priority"))
        {
            if (Enum.TryParse<AlertPriority>(element.Value.Trim(), ignoreCase: true, out var priority))
            {
                priorities.Add(priority);
                if ((bool?)element.Attribute("default") == true)
                {
                    preferred = priority;
                }
            }
        }

        if (priorities.Count == 0)
        {
            priorities.Add(AlertPriority.Indication);
        }

        return new AlertUse((string?)use.Attribute("msg"), (string?)use.Attribute("highlight"), priorities, preferred ?? priorities[0]);
    }

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Children(XElement? parent, string localName) =>
        parent?.Elements().Where(e => e.Name.LocalName == localName) ?? [];
}
