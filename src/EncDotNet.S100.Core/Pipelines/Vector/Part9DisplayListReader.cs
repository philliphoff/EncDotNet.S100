using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Reads an S-100 Part 9 drawing-instruction display list from XML produced
/// by an XSLT portrayal rule (used by S-124, S-129, S-421, etc.) and returns
/// the unified <see cref="DrawingInstruction"/> model.
/// </summary>
/// <remarks>
/// The expected XML shape is the lower-camel-case form emitted by S-100
/// Part 9 portrayal stylesheets:
/// <code>
/// &lt;displayList&gt;
///   &lt;pointInstruction&gt;
///     &lt;featureReference&gt;...&lt;/featureReference&gt;
///     &lt;viewingGroup&gt;...&lt;/viewingGroup&gt;
///     &lt;drawingPriority&gt;...&lt;/drawingPriority&gt;
///     &lt;symbol reference="..."&gt;
///       &lt;scaleFactor&gt;1&lt;/scaleFactor&gt;
///       &lt;rotation&gt;0&lt;/rotation&gt;
///     &lt;/symbol&gt;
///   &lt;/pointInstruction&gt;
///   &lt;lineInstruction&gt; ... &lt;/lineInstruction&gt;
///   &lt;areaInstruction&gt; ... &lt;/areaInstruction&gt;
///   &lt;textInstruction&gt; ... &lt;/textInstruction&gt;
/// &lt;/displayList&gt;
/// </code>
/// </remarks>
public static class Part9DisplayListReader
{
    /// <summary>
    /// Parses the given XSLT-output document into drawing instructions.
    /// Unknown element names are skipped silently so the reader is tolerant
    /// of stylesheets that emit additional metadata.
    /// </summary>
    public static IReadOnlyList<DrawingInstruction> Read(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var instructions = new List<DrawingInstruction>();
        var root = doc.Root;
        if (root is null) return instructions;

        foreach (var element in root.Elements())
        {
            var instruction = ReadInstruction(element);
            if (instruction is not null)
                instructions.Add(instruction);
        }

        return instructions;
    }

    private static DrawingInstruction? ReadInstruction(XElement element)
    {
        var featureReference = element.Element("featureReference")?.Value
            ?? element.Attribute("featureReference")?.Value
            ?? "";

        var viewingGroup = ParseInt(element.Element("viewingGroup")?.Value);
        var drawingPriority = ParseInt(element.Element("drawingPriority")?.Value);
        var plane = ParsePlane(element.Element("displayPlane")?.Value);
        var scaleMin = ParseNullableInt(element.Element("scaleMinimum")?.Value);
        var scaleMax = ParseNullableInt(element.Element("scaleMaximum")?.Value);

        switch (element.Name.LocalName)
        {
            case "pointInstruction":
                return ReadPoint(element, featureReference, viewingGroup, drawingPriority, plane, scaleMin, scaleMax);
            case "lineInstruction":
                return ReadLine(element, featureReference, viewingGroup, drawingPriority, plane, scaleMin, scaleMax);
            case "areaInstruction":
                return ReadArea(element, featureReference, viewingGroup, drawingPriority, plane, scaleMin, scaleMax);
            case "textInstruction":
                return ReadText(element, featureReference, viewingGroup, drawingPriority, plane, scaleMin, scaleMax);
            default:
                return null;
        }
    }

    private static PointInstruction? ReadPoint(
        XElement element, string featureReference, int viewingGroup, int drawingPriority,
        DisplayPlane plane, int? scaleMin, int? scaleMax)
    {
        var symbolEl = element.Element("symbol");
        if (symbolEl is null) return null;

        var symbolRef = symbolEl.Attribute("reference")?.Value
            ?? symbolEl.Element("reference")?.Value;
        if (symbolRef is null) return null;

        var scaleFactor = ParseDouble(symbolEl.Element("scaleFactor")?.Value, 1.0);
        var rotation = ParseNullableDouble(symbolEl.Element("rotation")?.Value);
        var offsetX = ParseDouble(symbolEl.Element("offset")?.Element("x")?.Value);
        var offsetY = ParseDouble(symbolEl.Element("offset")?.Element("y")?.Value);

        return new PointInstruction
        {
            FeatureReference = featureReference,
            ViewingGroup = viewingGroup,
            DrawingPriority = drawingPriority,
            Plane = plane,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            SymbolReference = symbolRef,
            SymbolScale = scaleFactor,
            Rotation = rotation,
            LocalOffsetX = offsetX,
            LocalOffsetY = offsetY,
        };
    }

    private static LineInstruction? ReadLine(
        XElement element, string featureReference, int viewingGroup, int drawingPriority,
        DisplayPlane plane, int? scaleMin, int? scaleMax)
    {
        string? lineStyleRef = null;
        string? color = null;
        double width = 0;
        List<(double Offset, double Length)>? dashes = null;

        var refEl = element.Element("lineStyleReference");
        if (refEl is not null)
            lineStyleRef = refEl.Attribute("reference")?.Value ?? refEl.Value;

        var inlineStyle = element.Element("lineStyle");
        if (inlineStyle is not null)
        {
            var pen = inlineStyle.Element("pen");
            if (pen is not null)
            {
                color = pen.Element("color")?.Value ?? pen.Attribute("color")?.Value;
                width = ParseDouble(pen.Attribute("width")?.Value
                    ?? pen.Element("width")?.Value);
            }

            // S-421 (and other XSLT pipelines) emit dash patterns as
            // <dash><start>0</start><length>3.6</length></dash> children of
            // <lineStyle>; carry them through so the renderer can switch to
            // a dashed pen.
            foreach (var dashEl in inlineStyle.Elements("dash"))
            {
                var start = ParseDouble(dashEl.Element("start")?.Value);
                var length = ParseDouble(dashEl.Element("length")?.Value);
                (dashes ??= []).Add((start, length));
            }
        }

        if (lineStyleRef is null && color is null) return null;

        return new LineInstruction
        {
            FeatureReference = featureReference,
            ViewingGroup = viewingGroup,
            DrawingPriority = drawingPriority,
            Plane = plane,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            LineStyleReference = lineStyleRef,
            LineColor = color,
            LineWidth = width,
            Dashes = dashes,
        };
    }

    private static AreaInstruction? ReadArea(
        XElement element, string featureReference, int viewingGroup, int drawingPriority,
        DisplayPlane plane, int? scaleMin, int? scaleMax)
    {
        string? areaFillRef = null;
        string? fillColor = null;
        double? transparency = null;

        var fillRefEl = element.Element("areaFillReference");
        if (fillRefEl is not null)
            areaFillRef = fillRefEl.Attribute("reference")?.Value ?? fillRefEl.Value;

        var colorFillEl = element.Element("colorFill");
        if (colorFillEl is not null)
        {
            fillColor = colorFillEl.Element("color")?.Value;
            var transp = colorFillEl.Element("transparency")?.Value;
            transparency = ParseNullableDouble(transp);
        }

        if (areaFillRef is null && fillColor is null) return null;

        return new AreaInstruction
        {
            FeatureReference = featureReference,
            ViewingGroup = viewingGroup,
            DrawingPriority = drawingPriority,
            Plane = plane,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            AreaFillReference = areaFillRef,
            FillColor = fillColor,
            Transparency = transparency,
        };
    }

    private static TextInstruction? ReadText(
        XElement element, string featureReference, int viewingGroup, int drawingPriority,
        DisplayPlane plane, int? scaleMin, int? scaleMax)
    {
        // Text content can appear at <textPoint><element><text>...</text></element></textPoint>,
        // a direct <text> child, or a <textValue> element.
        var text = element.Descendants("text").FirstOrDefault()?.Value
            ?? element.Element("textValue")?.Value;
        if (string.IsNullOrEmpty(text)) return null;

        var fontEl = element.Descendants("font").FirstOrDefault();
        double fontSize = 10;
        string fontColor = "CHBLK";
        if (fontEl is not null)
        {
            fontSize = ParseDouble(fontEl.Element("size")?.Value, 10);
            fontColor = fontEl.Element("color")?.Value ?? fontColor;
        }

        // <bodySize>, <foreground> and <background> are the names actually
        // emitted by the S-100 Part 9 / S-421 XSL (see e.g. RouteActionPoint.xsl).
        // Both colour elements may carry an optional transparency attribute
        // in [0.0, 1.0] where 0 = opaque and 1 = fully transparent.
        var bodySize = element.Descendants("bodySize").FirstOrDefault();
        if (bodySize is not null)
        {
            fontSize = ParseDouble(bodySize.Value, fontSize);
        }
        double? fontTransparency = null;
        var foreground = element.Descendants("foreground").FirstOrDefault();
        if (foreground is not null && !string.IsNullOrWhiteSpace(foreground.Value))
        {
            fontColor = foreground.Value.Trim();
            fontTransparency = ParseNullableDouble(foreground.Attribute("transparency")?.Value);
        }
        string? backgroundColor = null;
        double? backgroundTransparency = null;
        var background = element.Descendants("background").FirstOrDefault();
        if (background is not null && !string.IsNullOrWhiteSpace(background.Value))
        {
            backgroundColor = background.Value.Trim();
            backgroundTransparency = ParseNullableDouble(background.Attribute("transparency")?.Value);
        }

        // Placement: S-100 Part 9 §11.4 distinguishes <textPoint> (anchored at
        // the feature's representative point with optional mm <offset>) and
        // <textLine> (anchored along a curve via <startOffset>/<endOffset> +
        // <placementMode>).  Some catalogues (e.g. S-421's RouteActionPoint
        // and RouteWaypointLeg XSL) emit line-placement children inside a
        // <textPoint> wrapper, so we accept either element name and infer
        // the mode from the children present.
        var placement = element.Element("textPoint") ?? element.Element("textLine");

        var hAlign = ParseHAlign(placement?.Attribute("horizontalAlignment")?.Value);
        var vAlign = ParseVAlign(placement?.Attribute("verticalAlignment")?.Value);
        var rotation = ParseNullableDouble(placement?.Attribute("rotation")?.Value);

        double? offsetX = null, offsetY = null;
        var offsetEl = placement?.Element("offset");
        if (offsetEl is not null)
        {
            offsetX = ParseNullableDouble(offsetEl.Element("x")?.Value);
            offsetY = ParseNullableDouble(offsetEl.Element("y")?.Value);
        }

        double? startOffset = ParseNullableDouble(placement?.Element("startOffset")?.Value);
        double? endOffset = ParseNullableDouble(placement?.Element("endOffset")?.Value);
        LinePlacementMode? lineMode = null;
        var modeText = placement?.Element("placementMode")?.Value
            ?? placement?.Element("linePlacement")?.Attribute("placementMode")?.Value;
        if (!string.IsNullOrWhiteSpace(modeText)
            && Enum.TryParse<LinePlacementMode>(modeText, ignoreCase: true, out var m))
        {
            lineMode = m;
        }

        // Derive a single 0–1 fraction for downstream renderers when line
        // offsets are supplied.  For Relative mode we trust the offsets
        // directly (clamped); for Absolute mode the renderer would need the
        // line length, so we leave LinePlacementPosition unset and surface
        // the raw offsets via LineStartOffset / LineEndOffset.
        double? linePos = null;
        if (startOffset.HasValue || endOffset.HasValue)
        {
            if ((lineMode ?? LinePlacementMode.Relative) == LinePlacementMode.Relative
                && startOffset.HasValue && endOffset.HasValue
                && startOffset.Value >= 0 && startOffset.Value <= 1
                && endOffset.Value >= 0 && endOffset.Value <= 1)
            {
                linePos = (startOffset.Value + endOffset.Value) / 2.0;
            }
            else
            {
                // Out-of-range relative offsets (some catalogues emit raw mm
                // here despite declaring "Relative") — fall back to midpoint.
                linePos = 0.5;
            }
        }

        return new TextInstruction
        {
            FeatureReference = featureReference,
            ViewingGroup = viewingGroup,
            DrawingPriority = drawingPriority,
            Plane = plane,
            ScaleMinimum = scaleMin,
            ScaleMaximum = scaleMax,
            Text = text,
            FontSize = fontSize,
            FontColor = fontColor,
            FontTransparency = fontTransparency,
            BackgroundColor = backgroundColor,
            BackgroundTransparency = backgroundTransparency,
            Rotation = rotation,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
            OffsetXmm = offsetX,
            OffsetYmm = offsetY,
            LineStartOffset = startOffset,
            LineEndOffset = endOffset,
            LineOffsetMode = lineMode,
            LinePlacementPosition = linePos,
        };
    }

    private static TextHorizontalAlignment ParseHAlign(string? value) =>
        Enum.TryParse<TextHorizontalAlignment>(value, ignoreCase: true, out var a)
            ? a : TextHorizontalAlignment.Center;

    private static TextVerticalAlignment ParseVAlign(string? value) =>
        Enum.TryParse<TextVerticalAlignment>(value, ignoreCase: true, out var a)
            ? a : TextVerticalAlignment.Center;

    private static int ParseInt(string? value, int defaultValue = 0) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double ParseDouble(string? value, double defaultValue = 0.0) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static double? ParseNullableDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DisplayPlane ParsePlane(string? value) =>
        Enum.TryParse<DisplayPlane>(value, ignoreCase: true, out var p) ? p : DisplayPlane.UnderRadar;
}
