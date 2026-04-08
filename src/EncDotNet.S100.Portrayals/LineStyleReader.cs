using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 line style XML file and produces a <see cref="LineStyle"/>.
/// </summary>
/// <remarks>
/// S-100 Ed 5.2 Part 9 line style files define pen properties and optional dash patterns.
/// The expected XML structure is:
/// <code>
/// &lt;lineStyle&gt;
///   &lt;pen&gt;
///     &lt;color&gt;CHGRF&lt;/color&gt;
///     &lt;width&gt;0.32&lt;/width&gt;
///   &lt;/pen&gt;
///   &lt;dash&gt;
///     &lt;start&gt;3.0&lt;/start&gt;
///     &lt;stop&gt;1.0&lt;/stop&gt;
///   &lt;/dash&gt;
/// &lt;/lineStyle&gt;
/// </code>
/// The color element contains a color token that is resolved against the active palette at render time.
/// </remarks>
public static class LineStyleReader
{
    private static readonly XNamespace PC = "http://www.iho.int/S100PortrayalCatalog/5.2";

    /// <summary>
    /// Reads a line style definition from a stream.
    /// </summary>
    /// <param name="stream">The stream containing the line style XML.</param>
    /// <param name="name">The line style name (from the catalogue item ID).</param>
    public static LineStyle Read(Stream stream, string name)
    {
        var doc = XDocument.Load(stream);
        var root = doc.Root ?? throw new System.Xml.XmlException("Missing root element.");

        string color = "#000000";
        float width = 1.0f;
        float[]? dashPattern = null;

        var pen = FindElement(root, "pen");
        if (pen is not null)
        {
            var colorElement = FindElement(pen, "color");
            if (colorElement is not null)
            {
                color = colorElement.Value.Trim();
            }

            var widthElement = FindElement(pen, "width");
            if (widthElement is not null &&
                float.TryParse(widthElement.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            {
                width = w;
            }
        }

        // Parse dash pattern entries
        var dashElements = FindElements(root, "dash").ToList();
        if (dashElements.Count > 0)
        {
            var dashes = new List<float>();
            foreach (var dash in dashElements)
            {
                var start = FindElement(dash, "start");
                var stop = FindElement(dash, "stop");

                if (start is not null &&
                    float.TryParse(start.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                {
                    dashes.Add(s);
                }

                if (stop is not null &&
                    float.TryParse(stop.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var e))
                {
                    dashes.Add(e);
                }
            }

            if (dashes.Count > 0)
            {
                dashPattern = [.. dashes];
            }
        }

        return new LineStyle
        {
            Name = name,
            Width = width,
            Color = color,
            DashPattern = dashPattern,
        };
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
