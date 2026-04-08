using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 area fill XML file and produces an <see cref="AreaFill"/>.
/// </summary>
/// <remarks>
/// S-100 Ed 5.2 Part 9 area fill files define solid fill colors and/or pattern references.
/// The expected XML structure is:
/// <code>
/// &lt;areaFill&gt;
///   &lt;color&gt;DEPVS&lt;/color&gt;
///   &lt;pattern&gt;
///     &lt;symbol&gt;HATCH01&lt;/symbol&gt;
///   &lt;/pattern&gt;
/// &lt;/areaFill&gt;
/// </code>
/// The color element contains a color token resolved against the active palette at render time.
/// The pattern element references an SVG symbol used as a repeating tile.
/// </remarks>
public static class AreaFillReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";

    /// <summary>
    /// Reads an area fill definition from a stream.
    /// </summary>
    /// <param name="stream">The stream containing the area fill XML.</param>
    /// <param name="name">The area fill name (from the catalogue item ID).</param>
    public static AreaFill Read(Stream stream, string name)
    {
        var doc = XDocument.Load(stream);
        var root = doc.Root ?? throw new System.Xml.XmlException("Missing root element.");

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
        return parent.Element(XName.Get(localName)) ?? parent.Element(PC + localName);
    }
}
