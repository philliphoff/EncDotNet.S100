using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Processes S-100 SVG symbols by resolving CSS class-based styling to inline
/// attributes, removing layout/metadata elements, and stripping processing
/// instructions that reference external CSS stylesheets.
/// </summary>
/// <remarks>
/// S-100 Part 9 defines SVG symbols using CSS classes that reference colour tokens
/// from the active colour palette. This processor converts those classes to
/// inline attributes so the SVG can be rendered without external CSS resources.
/// <para>
/// CSS class conventions:
/// <list type="bullet">
///   <item><c>fTOKEN</c> — fill colour (e.g. <c>fCHBLK</c> → <c>fill:#000000</c>)</item>
///   <item><c>sTOKEN</c> — stroke colour (e.g. <c>sOUTLW</c> → <c>stroke:#464646</c>)</item>
///   <item><c>f0</c> — fill:none</item>
///   <item><c>sl</c> — round line caps and joins</item>
///   <item><c>layout</c> — layout-only elements (not rendered)</item>
/// </list>
/// </para>
/// </remarks>
public static class SvgProcessor
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Processes an S-100 SVG symbol by:
    /// <list type="number">
    ///   <item>Removing <c>&lt;?xml-stylesheet?&gt;</c> processing instructions</item>
    ///   <item>Removing layout elements (symbolBox, svgBox, pivotPoint)</item>
    ///   <item>Resolving CSS class names to inline style attributes using the given palette</item>
    ///   <item>Optionally scaling <c>stroke-width</c> values by a given factor</item>
    ///   <item>Removing metadata, title, and desc elements</item>
    /// </list>
    /// </summary>
    /// <param name="svgContent">The raw SVG content string.</param>
    /// <param name="palette">
    /// The colour palette to resolve tokens against. When <c>null</c>, tokens
    /// are resolved to black as a fallback.
    /// </param>
    /// <param name="strokeWidthScale">
    /// Optional multiplier applied to all <c>stroke-width</c> attribute values.
    /// Pass <c>null</c> to leave stroke widths in their original mm units.
    /// For example, pass <c>3.78</c> to convert mm to pixels at 96 DPI.
    /// </param>
    /// <returns>The processed SVG content string with inline styles and no external dependencies.</returns>
    public static string Process(string svgContent, ColorPalette? palette, double? strokeWidthScale = null)
    {
        var doc = XDocument.Parse(svgContent);
        var svg = doc.Root!;

        // 1. Remove xml-stylesheet processing instructions (they reference external CSS
        //    files that standalone renderers cannot resolve)
        foreach (var pi in doc.Nodes().OfType<XProcessingInstruction>().ToList())
            pi.Remove();

        // 2. Remove elements with class containing "layout"
        var layoutElements = svg.Descendants()
            .Where(e => (e.Attribute("class")?.Value ?? "").Contains("layout"))
            .ToList();
        foreach (var el in layoutElements)
            el.Remove();

        // 3. Process remaining elements: resolve CSS classes to inline styles
        foreach (var el in svg.Descendants().ToList())
        {
            var classAttr = el.Attribute("class");
            if (classAttr is null) continue;

            var classes = classAttr.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? fill = null;
            string? stroke = null;
            string? strokeLinecap = null;
            string? strokeLinejoin = null;
            List<string>? unrecognized = null;

            foreach (var cls in classes)
            {
                if (cls == "f0")
                {
                    fill = "none";
                }
                else if (cls == "sl")
                {
                    strokeLinecap = "round";
                    strokeLinejoin = "round";
                }
                else if (cls.Length > 1 && cls[0] == 'f' && char.IsUpper(cls[1]))
                {
                    // Fill class: fTOKEN (e.g. fCHBLK → fill:#000000)
                    if (palette is not null && palette.TryResolve(cls[1..], out var fHex))
                        fill = fHex;
                }
                else if (cls.Length > 1 && cls[0] == 's' && char.IsUpper(cls[1]))
                {
                    // Stroke class: sTOKEN (e.g. sCHBLK → stroke:#000000)
                    if (palette is not null && palette.TryResolve(cls[1..], out var sHex))
                        stroke = sHex;
                }
                else
                {
                    // Unrecognized class — preserve it
                    unrecognized ??= [];
                    unrecognized.Add(cls);
                }
            }

            // Apply resolved inline attributes, preserving any existing ones
            if (fill is not null && el.Attribute("fill") is null)
                el.SetAttributeValue("fill", fill);
            if (stroke is not null && el.Attribute("stroke") is null)
                el.SetAttributeValue("stroke", stroke);
            if (strokeLinecap is not null && el.Attribute("stroke-linecap") is null)
                el.SetAttributeValue("stroke-linecap", strokeLinecap);
            if (strokeLinejoin is not null && el.Attribute("stroke-linejoin") is null)
                el.SetAttributeValue("stroke-linejoin", strokeLinejoin);

            // Update or remove the class attribute
            if (unrecognized is { Count: > 0 })
                classAttr.Value = string.Join(' ', unrecognized);
            else
                classAttr.Remove();
        }

        // 4. Scale stroke-width values if requested
        if (strokeWidthScale is not null)
        {
            foreach (var el in svg.Descendants())
            {
                var sw = el.Attribute("stroke-width");
                if (sw is not null &&
                    double.TryParse(sw.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                {
                    sw.Value = (val * strokeWidthScale.Value).ToString("G", CultureInfo.InvariantCulture);
                }
            }
        }

        // 5. Remove metadata (not needed for rendering)
        svg.Elements(SvgNs + "metadata").Remove();
        svg.Elements(SvgNs + "title").Remove();
        svg.Elements(SvgNs + "desc").Remove();

        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
