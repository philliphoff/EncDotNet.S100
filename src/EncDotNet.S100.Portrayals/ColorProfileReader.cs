using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 color profile XML file and produces a <see cref="ColorPalette"/>.
/// </summary>
/// <remarks>
/// S-100 Ed 5.2 Part 9 color profile files contain a list of named color tokens
/// with their sRGB values. The expected XML structure is:
/// <code>
/// &lt;colorProfile&gt;
///   &lt;color token="NODTA"&gt;
///     &lt;sRGB red="163" green="180" blue="183"/&gt;
///   &lt;/color&gt;
///   ...
/// &lt;/colorProfile&gt;
/// </code>
/// </remarks>
public static class ColorProfileReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";

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

        foreach (var colorElement in FindElements(root, "color"))
        {
            var token = (string?)colorElement.Attribute("token");
            if (string.IsNullOrEmpty(token)) continue;

            var srgb = FindElement(colorElement, "sRGB");
            if (srgb is not null)
            {
                var hex = SRgbToHex(srgb);
                if (hex is not null)
                {
                    colors[token] = hex;
                }
            }
        }

        return new ColorPalette(paletteName, colors);
    }

    private static string? SRgbToHex(XElement srgb)
    {
        var redStr = (string?)srgb.Attribute("red");
        var greenStr = (string?)srgb.Attribute("green");
        var blueStr = (string?)srgb.Attribute("blue");

        if (redStr is null || greenStr is null || blueStr is null) return null;

        if (!int.TryParse(redStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(greenStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(blueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            return null;
        }

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static XElement? FindElement(XElement parent, string localName)
    {
        return parent.Element(XName.Get(localName)) ?? parent.Element(PC + localName);
    }

    private static IEnumerable<XElement> FindElements(XElement parent, string localName)
    {
        return parent.Elements(XName.Get(localName)).Concat(parent.Elements(PC + localName));
    }
}
