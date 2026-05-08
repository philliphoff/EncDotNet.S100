using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Pivot and bounding-box metrics extracted from an S-100 Part 9 SVG symbol,
/// used by renderers that center symbol bitmaps on an anchor (instead of on
/// the SVG's pivot point) to recover the spec-mandated placement.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Part 9 §11.5 places a point symbol so that the symbol's
/// <c>pivotPoint</c> coincides with the feature anchor.  The IHO portrayal
/// catalogue encodes the pivot as a <c>&lt;circle class="pivotPoint" cx cy&gt;</c>
/// element with the rendered glyph laid out in an asymmetric <c>viewBox</c>
/// around it.  Composite symbols (e.g. multi-digit soundings drawn from
/// SOUNDG/SOUNDS pieces) rely on this pivot convention so that adjacent
/// glyphs sit side-by-side rather than on top of each other.
/// </para>
/// <para>
/// Map renderers that center an SVG's bounding box on the anchor (such as
/// Mapsui's <c>ImageStyle</c>) ignore the pivot, so the per-symbol shift
/// returned by <see cref="PivotToBoundsCenterMm"/> must be added to the
/// renderer's offset to restore the correct placement.
/// </para>
/// </remarks>
public sealed class SvgPivotMetrics
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Offset, in millimetres, from the SVG's pivot point to the centre of
    /// its <c>viewBox</c> rectangle (i.e. <c>viewBoxCenter - pivot</c>).
    /// </summary>
    /// <remarks>
    /// Add this offset to a renderer's symbol offset to translate a
    /// bounds-centred SVG so that its pivot lands on the original anchor.
    /// SVG user units in S-100 symbols are millimetres, matching the
    /// <c>width="…mm"</c> declaration on the root element.
    /// </remarks>
    public (double X, double Y) PivotToBoundsCenterMm { get; }

    /// <summary>
    /// Pivot-to-bounds-centre offset expressed as a fraction of the
    /// <c>viewBox</c> size (range typically [-0.5, +0.5] per axis).  Suitable
    /// for renderers whose symbol-offset API is relative to the rendered
    /// symbol size (e.g. Mapsui's <c>RelativeOffset</c>).
    /// </summary>
    /// <remarks>
    /// X = +0.5 means the pivot sits at the left edge of the viewBox;
    /// X = -0.5 means it sits at the right edge.  Adding this offset to a
    /// "0 = centred" relative-offset property shifts the symbol so the pivot
    /// — not the bbox centre — coincides with the feature anchor.
    /// </remarks>
    public (double X, double Y) RelativeOffset { get; }

    private SvgPivotMetrics(
        (double X, double Y) pivotToBoundsCenterMm,
        (double X, double Y) relativeOffset)
    {
        PivotToBoundsCenterMm = pivotToBoundsCenterMm;
        RelativeOffset = relativeOffset;
    }

    /// <summary>
    /// Parses the <c>viewBox</c> and the <c>&lt;circle class="pivotPoint"&gt;</c>
    /// element from an S-100 SVG symbol and returns its pivot-to-bounds-centre
    /// offset.  Returns <c>null</c> when the SVG cannot be parsed, has no
    /// <c>viewBox</c>, or has no recognisable pivot element (in which case
    /// callers should treat the offset as zero).
    /// </summary>
    public static SvgPivotMetrics? TryParse(string svgContent)
    {
        if (string.IsNullOrEmpty(svgContent))
            return null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(svgContent);
        }
        catch
        {
            return null;
        }

        var svg = doc.Root;
        if (svg is null)
            return null;

        var viewBox = svg.Attribute("viewBox")?.Value;
        if (string.IsNullOrWhiteSpace(viewBox))
            return null;

        var parts = viewBox.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return null;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbX) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbY) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbW) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbH))
        {
            return null;
        }

        var pivot = svg.Descendants(SvgNs + "circle")
            .FirstOrDefault(e => (e.Attribute("class")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("pivotPoint"));
        if (pivot is null)
            return null;

        if (!double.TryParse(pivot.Attribute("cx")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            px = 0.0;
        if (!double.TryParse(pivot.Attribute("cy")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
            py = 0.0;

        var centerX = vbX + vbW / 2.0;
        var centerY = vbY + vbH / 2.0;

        var dx = centerX - px;
        var dy = centerY - py;
        var rx = vbW > 0 ? dx / vbW : 0.0;
        var ry = vbH > 0 ? dy / vbH : 0.0;

        return new SvgPivotMetrics((dx, dy), (rx, ry));
    }
}
