using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Parses an S-100 Part 9 symbol SVG into <see cref="SymbolGeometry"/>:
/// paths and circles with palette-token colours.
/// </summary>
/// <remarks>
/// <para>
/// Styling follows what the SVG renderers draw after the symbol SVG
/// processor has inlined the class colours: classes are read with
/// <see cref="SvgSymbolClasses"/>; an explicit presentation attribute beats
/// a class (the processor only fills in attributes that are absent); an
/// inline <c>style</c> declaration beats both; and the paint properties
/// inherit from the parent element as in SVG.
/// </para>
/// <para>
/// Supported elements: <c>path</c>, <c>circle</c>, <c>rect</c>,
/// <c>ellipse</c>, <c>line</c>, <c>polyline</c>, <c>polygon</c> and
/// <c>g</c>. Anything else that would draw (<c>text</c>, an element with a
/// <c>transform</c>, a pattern paint) is listed in
/// <see cref="SymbolGeometry.Unsupported"/> rather than guessed at.
/// </para>
/// </remarks>
public static class SymbolGeometryParser
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    /// <summary>Parses a symbol's SVG, or returns <see langword="null"/> when it is not well-formed SVG.</summary>
    /// <param name="svgContent">The raw symbol SVG (class colours unresolved).</param>
    /// <param name="name">The symbol identifier.</param>
    /// <returns>The geometry, or <see langword="null"/>.</returns>
    public static SymbolGeometry? TryParse(string svgContent, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(svgContent))
            return null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(svgContent);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "svg")
            return null;

        var shapes = new List<SymbolShape>();
        var unsupported = new List<string>();
        (double X, double Y) pivot = (0, 0);
        var hasPivot = false;

        var rootStyle = Cascade(InheritedStyle.Initial, root, SvgSymbolClasses.Read(root.Attribute("class")?.Value));
        Walk(root, rootStyle, shapes, unsupported, ref pivot, ref hasPivot);

        return new SymbolGeometry
        {
            Name = name,
            ViewBox = ParseViewBox(root.Attribute("viewBox")?.Value),
            Pivot = pivot,
            HasPivot = hasPivot,
            Shapes = shapes,
            Unsupported = unsupported,
        };
    }

    private static void Walk(
        XElement parent,
        InheritedStyle style,
        List<SymbolShape> shapes,
        List<string> unsupported,
        ref (double X, double Y) pivot,
        ref bool hasPivot)
    {
        foreach (var el in parent.Elements())
        {
            if (el.Name.Namespace != SvgNs && el.Name.Namespace != XNamespace.None)
                continue; // foreign metadata (iho:*)

            var local = el.Name.LocalName;
            if (local is "title" or "desc" or "metadata" or "defs" or "style")
                continue;

            var classes = SvgSymbolClasses.Read(el.Attribute("class")?.Value);
            if (classes.IsLayout)
            {
                if (classes.IsPivotPoint && local == "circle" && !hasPivot)
                {
                    pivot = (Number(el, "cx"), Number(el, "cy"));
                    hasPivot = true;
                }

                continue;
            }

            if (el.Attribute("transform") is not null)
            {
                unsupported.Add(local);
                continue;
            }

            var own = Cascade(style, el, classes);

            if (local == "g")
            {
                Walk(el, own, shapes, unsupported, ref pivot, ref hasPivot);
                continue;
            }

            if (own.PaintServer)
            {
                unsupported.Add(local);
                continue;
            }

            var paint = own.ToPaint();
            SymbolShape? shape = local switch
            {
                "path" => PathOf(SvgPathData.Parse(el.Attribute("d")?.Value), paint),
                "circle" => new SymbolCircle { CenterX = Number(el, "cx"), CenterY = Number(el, "cy"), Radius = Number(el, "r"), Paint = paint },
                "rect" => PathOf(Rect(el), paint),
                "ellipse" => PathOf(Ellipse(el), paint),
                "line" => PathOf([new SymbolMoveTo(Number(el, "x1"), Number(el, "y1")), new SymbolLineTo(Number(el, "x2"), Number(el, "y2"))], paint),
                "polyline" => PathOf(Poly(el.Attribute("points")?.Value, close: false), paint),
                "polygon" => PathOf(Poly(el.Attribute("points")?.Value, close: true), paint),
                _ => null,
            };

            if (shape is not null)
                shapes.Add(shape);
            else if (local is not ("path" or "rect" or "ellipse" or "line" or "polyline" or "polygon"))
                unsupported.Add(local);
        }
    }

    private static SymbolPath? PathOf(IReadOnlyList<SymbolPathSegment> segments, SymbolPaint paint) =>
        segments.Count == 0 ? null : new SymbolPath { Segments = segments, Paint = paint };

    private static List<SymbolPathSegment> Rect(XElement el)
    {
        var x = Number(el, "x");
        var y = Number(el, "y");
        var w = Number(el, "width");
        var h = Number(el, "height");
        if (w <= 0 || h <= 0)
            return [];

        var rxAttr = el.Attribute("rx");
        var ryAttr = el.Attribute("ry");
        var rx = rxAttr is not null ? Number(el, "rx") : ryAttr is not null ? Number(el, "ry") : 0;
        var ry = ryAttr is not null ? Number(el, "ry") : rx;
        rx = Math.Clamp(rx, 0, w / 2);
        ry = Math.Clamp(ry, 0, h / 2);

        if (rx == 0 || ry == 0)
        {
            return
            [
                new SymbolMoveTo(x, y),
                new SymbolLineTo(x + w, y),
                new SymbolLineTo(x + w, y + h),
                new SymbolLineTo(x, y + h),
                SymbolClosePath.Instance,
            ];
        }

        return
        [
            new SymbolMoveTo(x + rx, y),
            new SymbolLineTo(x + w - rx, y),
            new SymbolArcTo(rx, ry, 0, false, true, x + w, y + ry),
            new SymbolLineTo(x + w, y + h - ry),
            new SymbolArcTo(rx, ry, 0, false, true, x + w - rx, y + h),
            new SymbolLineTo(x + rx, y + h),
            new SymbolArcTo(rx, ry, 0, false, true, x, y + h - ry),
            new SymbolLineTo(x, y + ry),
            new SymbolArcTo(rx, ry, 0, false, true, x + rx, y),
            SymbolClosePath.Instance,
        ];
    }

    private static List<SymbolPathSegment> Ellipse(XElement el)
    {
        var cx = Number(el, "cx");
        var cy = Number(el, "cy");
        var rx = Number(el, "rx");
        var ry = Number(el, "ry");
        if (rx <= 0 || ry <= 0)
            return [];

        return
        [
            new SymbolMoveTo(cx + rx, cy),
            new SymbolArcTo(rx, ry, 0, false, true, cx - rx, cy),
            new SymbolArcTo(rx, ry, 0, false, true, cx + rx, cy),
            SymbolClosePath.Instance,
        ];
    }

    private static List<SymbolPathSegment> Poly(string? points, bool close)
    {
        var numbers = SvgPathData.Numbers(points);
        var segments = new List<SymbolPathSegment>(numbers.Count / 2 + 1);
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            segments.Add(i == 0
                ? new SymbolMoveTo(numbers[i], numbers[i + 1])
                : new SymbolLineTo(numbers[i], numbers[i + 1]));
        }

        if (close && segments.Count > 0)
            segments.Add(SymbolClosePath.Instance);
        return segments;
    }

    private static SymbolRect? ParseViewBox(string? value)
    {
        var n = SvgPathData.Numbers(value);
        return n.Count == 4 ? new SymbolRect(n[0], n[1], n[2], n[3]) : null;
    }

    private static double Number(XElement el, string attribute) =>
        TryNumber(el.Attribute(attribute)?.Value, out var v) ? v : 0;

    private static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();
        // Lengths in symbol SVGs are unitless user units; tolerate an "mm"/"px" suffix.
        var end = t.Length;
        while (end > 0 && char.IsLetter(t[end - 1]))
            end--;
        return double.TryParse(t.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Applies an element's classes, presentation attributes and inline
    /// style, in that ascending precedence, over the inherited style.
    /// </summary>
    private static InheritedStyle Cascade(InheritedStyle inherited, XElement el, SvgSymbolClassStyle classes)
    {
        var s = inherited with { PaintServer = false };

        // 1. Classes (S-100 conventions).
        if (classes.FillNone)
            s = s with { Fill = null };
        else if (classes.FillToken is not null)
            s = s with { Fill = SymbolColor.FromToken(classes.FillToken) };
        if (classes.StrokeToken is not null)
            s = s with { Stroke = SymbolColor.FromToken(classes.StrokeToken) };
        if (classes.RoundCapsAndJoins)
            s = s with { LineCap = SymbolLineCap.Round, LineJoin = SymbolLineJoin.Round };

        // 2. Presentation attributes.
        foreach (var attr in el.Attributes())
        {
            if (attr.Name.Namespace == XNamespace.None)
                s = Apply(s, attr.Name.LocalName, attr.Value);
        }

        // 3. Inline style declarations.
        var style = el.Attribute("style")?.Value;
        if (!string.IsNullOrWhiteSpace(style))
        {
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':');
                if (colon > 0)
                    s = Apply(s, declaration[..colon].Trim(), declaration[(colon + 1)..].Trim());
            }
        }

        return s;
    }

    private static InheritedStyle Apply(InheritedStyle s, string property, string value)
    {
        switch (property)
        {
            case "fill":
                return Paint(value, out var fill, out var fillServer)
                    ? s with { Fill = fill, PaintServer = s.PaintServer || fillServer }
                    : s;
            case "stroke":
                return Paint(value, out var stroke, out var strokeServer)
                    ? s with { Stroke = stroke, PaintServer = s.PaintServer || strokeServer }
                    : s;
            case "fill-opacity":
                return TryNumber(value, out var fo) ? s with { FillOpacity = Math.Clamp(fo, 0, 1) } : s;
            case "stroke-opacity":
                return TryNumber(value, out var so) ? s with { StrokeOpacity = Math.Clamp(so, 0, 1) } : s;
            case "opacity":
                // Group opacity is not modelled separately; fold it into both paints.
                return TryNumber(value, out var o)
                    ? s with { FillOpacity = s.FillOpacity * Math.Clamp(o, 0, 1), StrokeOpacity = s.StrokeOpacity * Math.Clamp(o, 0, 1) }
                    : s;
            case "stroke-width":
                return TryNumber(value, out var sw) ? s with { StrokeWidth = sw } : s;
            case "fill-rule":
                return value switch
                {
                    "evenodd" => s with { FillRule = SymbolFillRule.EvenOdd },
                    "nonzero" => s with { FillRule = SymbolFillRule.NonZero },
                    _ => s,
                };
            case "stroke-linecap":
                return value switch
                {
                    "butt" => s with { LineCap = SymbolLineCap.Butt },
                    "round" => s with { LineCap = SymbolLineCap.Round },
                    "square" => s with { LineCap = SymbolLineCap.Square },
                    _ => s,
                };
            case "stroke-linejoin":
                return value switch
                {
                    "miter" => s with { LineJoin = SymbolLineJoin.Miter },
                    "round" => s with { LineJoin = SymbolLineJoin.Round },
                    "bevel" => s with { LineJoin = SymbolLineJoin.Bevel },
                    _ => s,
                };
            default:
                return s;
        }
    }

    /// <summary>Reads a paint value; returns <see langword="false"/> to keep the inherited paint.</summary>
    private static bool Paint(string value, out SymbolColor? color, out bool paintServer)
    {
        color = null;
        paintServer = false;
        var v = value.Trim();
        if (v.Length == 0 || v == "inherit")
            return false;
        if (v == "none")
            return true;
        if (v.StartsWith("url(", StringComparison.Ordinal))
        {
            paintServer = true;
            return true;
        }

        color = SymbolColor.FromLiteral(v);
        return true;
    }

    private sealed record InheritedStyle(
        SymbolColor? Fill,
        double FillOpacity,
        SymbolFillRule FillRule,
        SymbolColor? Stroke,
        double StrokeOpacity,
        double StrokeWidth,
        SymbolLineCap LineCap,
        SymbolLineJoin LineJoin,
        bool PaintServer)
    {
        public static InheritedStyle Initial { get; } = new(
            SymbolColor.DefaultFill, 1.0, SymbolFillRule.NonZero, null, 1.0, 1.0,
            SymbolLineCap.Butt, SymbolLineJoin.Miter, false);

        public SymbolPaint ToPaint() => new()
        {
            Fill = Fill,
            FillOpacity = FillOpacity,
            FillRule = FillRule,
            Stroke = Stroke,
            StrokeOpacity = StrokeOpacity,
            StrokeWidth = StrokeWidth,
            LineCap = LineCap,
            LineJoin = LineJoin,
        };
    }
}
