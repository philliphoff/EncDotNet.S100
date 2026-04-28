using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using NtsPoint = NetTopologySuite.Geometries.Point;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders an S-421 Route Plan dataset and its XSLT-produced Part 9 display list
/// into a Mapsui <see cref="ILayer"/>. Resolves geometry from the dataset by
/// <c>gml:id</c>, projects to EPSG:3857 (Web Mercator), and applies styles
/// derived from the drawing instructions.
/// </summary>
/// <remarks>
/// S-421 portrayal output uses standard S-100 Part 9 instruction shapes
/// (<c>pointInstruction</c>, <c>lineInstruction</c>, <c>textInstruction</c>,
/// <c>areaInstruction</c>) with a <c>featureReference</c> child whose value
/// matches the dataset feature's <c>gml:id</c>.
/// </remarks>
public sealed class MapsuiS421VectorRenderer
{
    /// <summary>Default name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-421 Route";

    /// <summary>
    /// The color palette used to resolve S-100 colour tokens (e.g. <c>PLRTE</c>,
    /// <c>CHBLK</c>) and to recolour SVG symbols. When <c>null</c>, fallback
    /// colours are used.
    /// </summary>
    public ColorPalette? Palette { get; set; }

    /// <summary>
    /// Optional callback returning the SVG symbol for a given symbol reference
    /// (e.g. <c>"RTEWPT01"</c>). When present, point instructions render with
    /// the resolved symbol; otherwise a fallback shape is drawn.
    /// </summary>
    public Func<string, SvgSymbol?>? SymbolProvider { get; set; }

    /// <summary>Global scale factor applied to all point symbols (default 1.0).</summary>
    public double SymbolScale { get; set; } = 1.0;

    /// <summary>Global scale factor applied to all text labels (default 1.0).</summary>
    public double TextScale { get; set; } = 1.0;

    private static readonly MapsuiColor RouteFallbackColor = new(0xC0, 0x40, 0xC0); // magenta-ish (PLRTE)
    private static readonly MapsuiColor TextFallbackColor = new(0xAA, 0x44, 0xA8);

    /// <summary>
    /// Renders the given Part 9 display list against the supplied dataset,
    /// returning a Mapsui memory layer containing one feature per drawing instruction.
    /// </summary>
    /// <param name="displayList">
    /// XSLT-produced Part 9 display list (the document returned by
    /// <c>S421PortrayalCatalogue.GetCompiledRule(...).Transform(...)</c>).
    /// </param>
    /// <param name="dataset">The S-421 dataset whose features the instructions reference.</param>
    public ILayer Render(XDocument displayList, S421Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(displayList);
        ArgumentNullException.ThrowIfNull(dataset);

        var featureIndex = new Dictionary<string, S421Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dataset.Features)
        {
            if (!string.IsNullOrEmpty(f.Id))
                featureIndex[f.Id] = f;
        }

        var instructions = ParseDisplayList(displayList);

        // Render order: lines first (so symbols/text sit on top), then points,
        // then text. Within each bucket, sort by drawing priority.
        instructions.Sort(static (a, b) =>
        {
            int ao = OrderFor(a.Type);
            int bo = OrderFor(b.Type);
            return ao != bo ? ao.CompareTo(bo) : a.DrawingPriority.CompareTo(b.DrawingPriority);

            static int OrderFor(S421InstructionType t) => t switch
            {
                S421InstructionType.Area => 0,
                S421InstructionType.Line => 1,
                S421InstructionType.Point => 2,
                S421InstructionType.Text => 3,
                _ => 4,
            };
        });

        var mapFeatures = new List<IFeature>();
        foreach (var instr in instructions)
        {
            if (!featureIndex.TryGetValue(instr.FeatureReference, out var feature))
                continue;

            var mapFeature = instr.Type switch
            {
                S421InstructionType.Point => CreatePointFeature(instr, feature),
                S421InstructionType.Line => CreateLineFeature(instr, feature),
                S421InstructionType.Area => CreateAreaFeature(instr, feature),
                S421InstructionType.Text => CreateTextFeature(instr, feature),
                _ => null,
            };

            if (mapFeature is null)
                continue;

            mapFeature[MapsuiS101VectorRenderer.FeatureRefKey] = instr.FeatureReference;
            mapFeatures.Add(mapFeature);
        }

        return new MemoryLayer
        {
            Name = LayerName,
            Features = mapFeatures,
            Style = null,
        };
    }

    // ── Feature creation ─────────────────────────────────────────

    private IFeature? CreatePointFeature(S421Instruction instr, S421Feature feature)
    {
        if (feature.Points.IsDefaultOrEmpty)
            return null;

        var (lat, lon) = feature.Points[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var mapFeature = new GeometryFeature(new NtsPoint(mx, my));

        if (instr.SymbolReference is not null && SymbolProvider is not null)
        {
            var symbol = SymbolProvider(instr.SymbolReference);
            if (symbol is not null)
            {
                var processed = SvgProcessor.Process(symbol.SvgContent, Palette);
                var scale = 0.6 * instr.SymbolScaleFactor * SymbolScale;

                // Hit-test backing rectangle so taps on transparent SVG areas still pick the feature.
                mapFeature.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Rectangle,
                    SymbolScale = scale * 1.2,
                    Fill = new Brush { Color = new MapsuiColor(0, 0, 0, 1) },
                    Line = null,
                    Outline = null,
                });

                mapFeature.Styles.Add(new ImageStyle
                {
                    Image = new Image { Source = "svg-content://" + processed, RasterizeSvg = true },
                    SymbolScale = scale,
                });
                return mapFeature;
            }
        }

        // Fallback marker
        mapFeature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(RouteFallbackColor),
            Outline = new Pen(MapsuiColor.Black, 1),
            SymbolScale = 0.5 * SymbolScale,
        });
        return mapFeature;
    }

    private IFeature? CreateLineFeature(S421Instruction instr, S421Feature feature)
    {
        var coords = GetLineCoordinates(feature);
        if (coords.Count < 2)
            return null;

        var ntsCoords = coords
            .Select(c =>
            {
                var (mx, my) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
                return new Coordinate(mx, my);
            })
            .ToArray();

        var mapFeature = new GeometryFeature(new LineString(ntsCoords));

        var color = instr.LineColor is not null
            ? ResolvePaletteColor(instr.LineColor, RouteFallbackColor)
            : RouteFallbackColor;
        var width = instr.LineWidth > 0 ? instr.LineWidth : 1.5;

        var pen = new Pen(color, width);
        if (instr.LineStyle.Equals("dash", StringComparison.OrdinalIgnoreCase))
        {
            pen.PenStyle = PenStyle.Dash;
        }
        else if (instr.LineStyle.Equals("dot", StringComparison.OrdinalIgnoreCase))
        {
            pen.PenStyle = PenStyle.Dot;
        }

        mapFeature.Styles.Add(new VectorStyle { Line = pen });
        return mapFeature;
    }

    private IFeature? CreateAreaFeature(S421Instruction instr, S421Feature feature)
    {
        if (feature.ExteriorRing.IsDefaultOrEmpty)
            return null;

        var ext = feature.ExteriorRing
            .Select(c =>
            {
                var (mx, my) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
                return new Coordinate(mx, my);
            })
            .ToArray();
        if (ext.Length < 4)
            return null;

        var holes = feature.InteriorRings
            .Select(ring => new LinearRing(ring
                .Select(c =>
                {
                    var (mx, my) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
                    return new Coordinate(mx, my);
                })
                .ToArray()))
            .ToArray();

        var polygon = holes.Length > 0 ? new Polygon(new LinearRing(ext), holes) : new Polygon(new LinearRing(ext));
        var mapFeature = new GeometryFeature(polygon);

        var fill = instr.LineColor is not null
            ? ResolvePaletteColor(instr.LineColor, RouteFallbackColor)
            : RouteFallbackColor;
        // Make area fill semi-transparent so underlying chart remains visible.
        var translucent = new MapsuiColor(fill.R, fill.G, fill.B, 80);

        mapFeature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(translucent),
            Outline = new Pen(fill, instr.LineWidth > 0 ? instr.LineWidth : 1.0),
        });
        return mapFeature;
    }

    private IFeature? CreateTextFeature(S421Instruction instr, S421Feature feature)
    {
        if (instr.TextContent is null)
            return null;

        // Anchor at the feature's first available coordinate.
        (double Lat, double Lon)? anchor = null;
        if (!feature.Points.IsDefaultOrEmpty)
            anchor = feature.Points[0];
        else if (!feature.Curves.IsDefaultOrEmpty && feature.Curves[0].Length > 0)
            anchor = feature.Curves[0][feature.Curves[0].Length / 2];
        else if (!feature.ExteriorRing.IsDefaultOrEmpty)
            anchor = feature.ExteriorRing[0];

        if (anchor is null)
            return null;

        var (mx, my) = SphericalMercator.FromLonLat(anchor.Value.Lon, anchor.Value.Lat);
        var mapFeature = new GeometryFeature(new NtsPoint(mx, my));

        mapFeature.Styles.Add(new LabelStyle
        {
            Text = instr.TextContent,
            ForeColor = TextFallbackColor,
            BackColor = new Brush(new MapsuiColor(255, 255, 255, 180)),
            Font = new Font { Size = 10 * TextScale },
            Offset = new Offset(8, -8),
        });
        return mapFeature;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private MapsuiColor ResolvePaletteColor(string token, MapsuiColor fallback)
    {
        if (Palette is null)
            return fallback;

        var hex = Palette.Resolve(token);
        if (hex is null)
            return fallback;

        var rgba = RgbaColor.FromHex(hex);
        return new MapsuiColor(rgba.R, rgba.G, rgba.B, rgba.A);
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> GetLineCoordinates(S421Feature feature)
    {
        if (!feature.Curves.IsDefaultOrEmpty)
        {
            var coords = new List<(double, double)>();
            foreach (var curve in feature.Curves)
                foreach (var c in curve)
                    coords.Add(c);
            return coords;
        }

        if (!feature.ExteriorRing.IsDefaultOrEmpty)
            return feature.ExteriorRing;

        return [];
    }

    // ── Display-list parser ──────────────────────────────────────

    private static List<S421Instruction> ParseDisplayList(XDocument doc)
    {
        var result = new List<S421Instruction>();
        var root = doc.Root;
        if (root is null) return result;

        foreach (var element in root.Elements())
        {
            var type = element.Name.LocalName switch
            {
                "pointInstruction" => S421InstructionType.Point,
                "lineInstruction" => S421InstructionType.Line,
                "areaInstruction" => S421InstructionType.Area,
                "textInstruction" => S421InstructionType.Text,
                _ => (S421InstructionType?)null,
            };
            if (type is null) continue;

            var featureRef = element.Element("featureReference")?.Value ?? "";
            var drawingPriority = ParseInt(element.Element("drawingPriority")?.Value);

            string? symbolRef = null;
            double scaleFactor = 1.0;
            var symbolEl = element.Element("symbol");
            if (symbolEl is not null)
            {
                symbolRef = symbolEl.Attribute("reference")?.Value;
                scaleFactor = ParseDouble(symbolEl.Element("scaleFactor")?.Value, 1.0);
            }

            string? lineStyleRef = null;
            string? lineColor = null;
            double lineWidth = 0;
            string lineStyle = "solid";

            var lineStyleRefEl = element.Element("lineStyleReference");
            if (lineStyleRefEl is not null)
                lineStyleRef = lineStyleRefEl.Attribute("reference")?.Value;

            var lineStyleEl = element.Element("lineStyle");
            if (lineStyleEl is not null)
            {
                var pen = lineStyleEl.Element("pen");
                if (pen is not null)
                {
                    lineColor = pen.Element("color")?.Value;
                    lineWidth = ParseDouble(pen.Attribute("width")?.Value);
                    var styleEl = pen.Element("style");
                    if (styleEl is not null) lineStyle = styleEl.Value;
                }
            }

            string? textContent = null;
            var textPoint = element.Element("textPoint");
            if (textPoint is not null)
                textContent = textPoint.Descendants("text").FirstOrDefault()?.Value?.Trim();

            result.Add(new S421Instruction
            {
                Type = type.Value,
                FeatureReference = featureRef,
                DrawingPriority = drawingPriority,
                SymbolReference = symbolRef,
                SymbolScaleFactor = scaleFactor,
                LineStyleReference = lineStyleRef,
                LineColor = lineColor,
                LineWidth = lineWidth,
                LineStyle = lineStyle,
                TextContent = textContent,
            });
        }

        return result;
    }

    private static int ParseInt(string? value, int fallback = 0) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out var r) ? r : fallback;

    private static double ParseDouble(string? value, double fallback = 0.0) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out var r) ? r : fallback;

    private enum S421InstructionType { Point, Line, Area, Text }

    private sealed class S421Instruction
    {
        public required S421InstructionType Type { get; init; }
        public required string FeatureReference { get; init; }
        public int DrawingPriority { get; init; }
        public string? SymbolReference { get; init; }
        public double SymbolScaleFactor { get; init; } = 1.0;
        public string? LineStyleReference { get; init; }
        public string? LineColor { get; init; }
        public double LineWidth { get; init; }
        public string LineStyle { get; init; } = "solid";
        public string? TextContent { get; init; }
    }
}
