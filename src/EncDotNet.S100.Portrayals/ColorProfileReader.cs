using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 color profile XML file and produces a <see cref="ColorPalette"/>.
/// </summary>
/// <remarks>
/// Supports two XML formats:
/// <list type="bullet">
///   <item>
///     <term>Flat format</term>
///     <description>
///       <c>&lt;colorProfile&gt;&lt;color token="NODTA"&gt;&lt;sRGB red="163" green="180" blue="183"/&gt;&lt;/color&gt;...&lt;/colorProfile&gt;</c>
///     </description>
///   </item>
///   <item>
///     <term>S-100 palette format</term>
///     <description>
///       <c>&lt;colorProfile&gt;&lt;palette name="Day"&gt;&lt;item token="NODTA"&gt;&lt;srgb&gt;&lt;red&gt;147&lt;/red&gt;...&lt;/srgb&gt;&lt;/item&gt;...&lt;/palette&gt;...&lt;/colorProfile&gt;</c>
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class ColorProfileReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";
    private static readonly XNamespace CP = "http://www.iho.int/S100ColorProfile/5.1";

    /// <summary>
    /// Reads a color profile from a stream and returns a <see cref="ColorPalette"/>.
    /// </summary>
    /// <param name="stream">The stream containing the color profile XML.</param>
    /// <param name="paletteName">The palette name (e.g. "Day", "Dusk", "Night").</param>
    public static ColorPalette Read(Stream stream, string paletteName)
    {
        var doc = XDocument.Load(stream);
        var root = doc.Root ?? throw new System.Xml.XmlException("Missing root element.");

        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Try S-100 palette format first: <palette name="Day"><item token="..."><srgb>...</srgb></item></palette>
        var palette = FindPaletteElement(root, paletteName);
        if (palette is not null)
        {
            foreach (var item in FindElements(palette, "item"))
            {
                var token = (string?)item.Attribute("token");
                if (string.IsNullOrEmpty(token)) continue;

                var srgb = FindElement(item, "srgb") ?? FindElement(item, "sRGB");
                if (srgb is null) continue;

                var hex = SRgbChildElementsToHex(srgb) ?? SRgbAttributesToHex(srgb);
                if (hex is not null)
                {
                    colors[token] = hex;
                }
            }

            return new ColorPalette(paletteName, colors);
        }

        // Fall back to flat format: <color token="..."><sRGB red="..." green="..." blue="..."/></color>
        foreach (var colorElement in FindElements(root, "color"))
        {
            var token = (string?)colorElement.Attribute("token");
            if (string.IsNullOrEmpty(token)) continue;

            var srgb = FindElement(colorElement, "sRGB") ?? FindElement(colorElement, "srgb");
            if (srgb is not null)
            {
                var hex = SRgbAttributesToHex(srgb) ?? SRgbChildElementsToHex(srgb);
                if (hex is not null)
                {
                    colors[token] = hex;
                }
            }
        }

        return new ColorPalette(paletteName, colors);
    }

    /// <summary>
    /// Parses sRGB from attributes: <c>&lt;sRGB red="163" green="180" blue="183"/&gt;</c>.
    /// </summary>
    private static string? SRgbAttributesToHex(XElement srgb)
    {
        var redStr = (string?)srgb.Attribute("red");
        var greenStr = (string?)srgb.Attribute("green");
        var blueStr = (string?)srgb.Attribute("blue");

        if (redStr is null || greenStr is null || blueStr is null) return null;

        return TryParseRgbHex(redStr, greenStr, blueStr);
    }

    /// <summary>
    /// Parses sRGB from child elements: <c>&lt;srgb&gt;&lt;red&gt;147&lt;/red&gt;&lt;green&gt;174&lt;/green&gt;&lt;blue&gt;187&lt;/blue&gt;&lt;/srgb&gt;</c>.
    /// </summary>
    private static string? SRgbChildElementsToHex(XElement srgb)
    {
        var redEl = FindElement(srgb, "red");
        var greenEl = FindElement(srgb, "green");
        var blueEl = FindElement(srgb, "blue");

        if (redEl is null || greenEl is null || blueEl is null) return null;

        return TryParseRgbHex(redEl.Value, greenEl.Value, blueEl.Value);
    }

    private static string? TryParseRgbHex(string redStr, string greenStr, string blueStr)
    {
        if (!int.TryParse(redStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(greenStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(blueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            return null;
        }

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static XElement? FindPaletteElement(XElement root, string paletteName)
    {
        foreach (var el in FindElements(root, "palette"))
        {
            var name = (string?)el.Attribute("name");
            if (string.Equals(name, paletteName, StringComparison.OrdinalIgnoreCase))
                return el;
        }

        return null;
    }

    private static XElement? FindElement(XElement parent, string localName)
    {
        return parent.Element(XName.Get(localName))
            ?? parent.Element(PC + localName)
            ?? parent.Element(CP + localName);
    }

    private static IEnumerable<XElement> FindElements(XElement parent, string localName)
    {
        return parent.Elements(XName.Get(localName))
            .Concat(parent.Elements(PC + localName))
            .Concat(parent.Elements(CP + localName));
    }
}
