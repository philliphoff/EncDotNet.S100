using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 area fill XML file and produces an <see cref="AreaFill"/>.
/// </summary>
/// <remarks>
/// S-100 Ed 5.2 Part 9 area fill files may use either the simple format:
/// <code>
/// &lt;areaFill&gt;
///   &lt;color&gt;DEPVS&lt;/color&gt;
///   &lt;pattern&gt;
///     &lt;symbol&gt;HATCH01&lt;/symbol&gt;
///   &lt;/pattern&gt;
/// &lt;/areaFill&gt;
/// </code>
/// or the S-101 catalogue format with tiling vectors:
/// <code>
/// &lt;af:symbolFill xmlns:af="http://www.iho.int/S100AreaFill/5.2"&gt;
///   &lt;areaCRS&gt;GlobalGeometry&lt;/areaCRS&gt;
///   &lt;symbol reference="DQUALB01P"/&gt;
///   &lt;v1&gt;&lt;x&gt;30.97&lt;/x&gt;&lt;y&gt;0.0&lt;/y&gt;&lt;/v1&gt;
///   &lt;v2&gt;&lt;x&gt;16.97&lt;/x&gt;&lt;y&gt;25.84&lt;/y&gt;&lt;/v2&gt;
/// &lt;/af:symbolFill&gt;
/// </code>
/// </remarks>
public static class AreaFillReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";
    private static readonly XNamespace AF = "http://www.iho.int/S100AreaFill/5.2";

    /// <summary>
    /// Reads an area fill definition from a stream.
    /// </summary>
    /// <param name="stream">The stream containing the area fill XML.</param>
    /// <param name="name">The area fill name (from the catalogue item ID).</param>
    public static AreaFill Read(Stream stream, string name)
    {
        var doc = XDocument.Load(stream);
        var root = doc.Root ?? throw new System.Xml.XmlException("Missing root element.");

        // Detect format based on root element
        if (root.Name.LocalName == "symbolFill")
        {
            return ReadSymbolFillFormat(root, name);
        }

        return ReadSimpleFormat(root, name);
    }

    /// <summary>
    /// Reads the af:symbolFill format used in S-101 portrayal catalogues.
    /// </summary>
    private static AreaFill ReadSymbolFillFormat(XElement root, string name)
    {
        // <symbol reference="DQUALB01P"/>
        var symbolElement = FindElement(root, "symbol");
        var patternSymbol = symbolElement?.Attribute("reference")?.Value?.Trim();

        double v1x = 0, v1y = 0, v2x = 0, v2y = 0;

        var v1 = FindElement(root, "v1");
        if (v1 is not null)
        {
            v1x = ParseDouble(FindElement(v1, "x"));
            v1y = ParseDouble(FindElement(v1, "y"));
        }

        var v2 = FindElement(root, "v2");
        if (v2 is not null)
        {
            v2x = ParseDouble(FindElement(v2, "x"));
            v2y = ParseDouble(FindElement(v2, "y"));
        }

        return new AreaFill
        {
            Name = name,
            PatternSymbol = patternSymbol,
            V1X = v1x,
            V1Y = v1y,
            V2X = v2x,
            V2Y = v2y,
        };
    }

    /// <summary>
    /// Reads the simple areaFill format.
    /// </summary>
    private static AreaFill ReadSimpleFormat(XElement root, string name)
    {
        string? color = null;
        string? patternSymbol = null;

        var colorElement = FindElement(root, "color");
        if (colorElement is not null)
        {
            color = colorElement.Value.Trim();
        }

        var pattern = FindElement(root, "pattern");
        if (pattern is not null)
        {
            var symbolElement = FindElement(pattern, "symbol");
            if (symbolElement is not null)
            {
                patternSymbol = symbolElement.Value.Trim();
            }
        }

        return new AreaFill
        {
            Name = name,
            Color = color,
            PatternSymbol = patternSymbol,
        };
    }

    private static XElement? FindElement(XElement parent, string localName)
    {
        return parent.Element(XName.Get(localName))
            ?? parent.Element(PC + localName)
            ?? parent.Element(AF + localName);
    }

    private static double ParseDouble(XElement? element)
    {
        if (element is null) return 0;
        return double.TryParse(element.Value.Trim(), CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
