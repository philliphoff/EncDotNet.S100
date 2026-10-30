using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Reads an S-100 line style XML file and produces a <see cref="LineStyle"/>:
/// its pen, dashes, interval and the symbols placed along the line, or the
/// line styles of a composite line style.
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

        // A composite line style stacks its inline line styles (S-100 Part 9).
        if (root.Name.LocalName == "compositeLineStyle")
        {
            var components = root.Elements()
                .Where(e => e.Name.LocalName == "lineStyle")
                .Select(e => ReadLineStyle(e, name))
                .ToList();
            var first = components.FirstOrDefault();
            return new LineStyle
            {
                Name = name,
                Width = first?.Width ?? 1.0f,
                Color = first?.Color ?? "#000000",
                DashPattern = first?.DashPattern,
                Components = components,
            };
        }

        return ReadLineStyle(root, name);
    }

    private static LineStyle ReadLineStyle(XElement root, string name)
    {
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

            // Width may be expressed as either a child element <width>
            // or an attribute width="..."  on <pen>.
            var widthElement = FindElement(pen, "width");
            var widthText = widthElement?.Value.Trim() ?? pen.Attribute("width")?.Value;
            if (widthText is not null &&
                float.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            {
                width = w;
            }
        }

        // Parse dash pattern entries.
        // S-100 line style files use either <start>+<stop> or <start>+<length>
        // children inside <dash>; we accept both forms and emit
        // (offset, segmentLength) pairs.
        var dashElements = FindElements(root, "dash").ToList();
        if (dashElements.Count > 0)
        {
            var dashes = new List<float>();
            foreach (var dash in dashElements)
            {
                var start = FindElement(dash, "start");
                var stop = FindElement(dash, "stop") ?? FindElement(dash, "length");

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

        float intervalLength = 0;
        if (FindElement(root, "intervalLength") is { } interval
            && float.TryParse(interval.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var il))
        {
            intervalLength = il;
        }

        var symbols = new List<LineStyleSymbol>();
        foreach (var symbol in FindElements(root, "symbol"))
        {
            var reference = symbol.Attribute("reference")?.Value;
            var position = FindElement(symbol, "position");
            if (!string.IsNullOrWhiteSpace(reference) && position is not null
                && float.TryParse(position.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
            {
                symbols.Add(new LineStyleSymbol(reference, at));
            }
        }

        return new LineStyle
        {
            Name = name,
            Width = width,
            Color = color,
            DashPattern = dashPattern,
            IntervalLength = intervalLength,
            HasPen = pen is not null,
            Symbols = symbols,
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
