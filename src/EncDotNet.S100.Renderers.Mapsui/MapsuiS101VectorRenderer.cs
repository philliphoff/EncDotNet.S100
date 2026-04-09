using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Svg.Skia;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders S-101 parsed drawing instructions into a Mapsui <see cref="ILayer"/>
/// by resolving feature geometry from the dataset, projecting to EPSG:3857,
/// and applying styles derived from the instruction properties.
/// </summary>
public sealed class MapsuiS101VectorRenderer
{
    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-101 Vector";

    /// <summary>
    /// The color palette to use for resolving S-100 color tokens.
    /// When set, overrides the built-in fallback colors.
    /// </summary>
    public ColorPalette? Palette { get; set; }

    /// <summary>
    /// Optional function that returns raw SVG content for a symbol name
    /// (e.g. "POSGEN03" → the contents of POSGEN03.svg).
    /// When set, point features will render using actual SVG symbols.
    /// </summary>
    public Func<string, string?>? SymbolProvider { get; set; }

    /// <summary>
    /// Optional function that returns an <see cref="AreaFill"/> definition by name.
    /// When set, non-colorFill area instructions will render using tiled SVG patterns.
    /// </summary>
    public Func<string, AreaFill?>? AreaFillProvider { get; set; }

    // Caches processed SVG data URIs keyed by symbol name.
    private readonly Dictionary<string, string?> _symbolDataUriCache = new(StringComparer.OrdinalIgnoreCase);

    // Caches rasterized pattern tile sources keyed by area fill name.
    private readonly Dictionary<string, (string Source, AreaFill Fill)?> _patternTileCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Renders a set of parsed drawing instructions for the given dataset
    /// into a Mapsui layer.
    /// </summary>
    public ILayer Render(
        IReadOnlyList<ParsedDrawingInstruction> instructions,
        S101Dataset dataset)
    {
        // 1. Resolve all feature geometries from the dataset
        var vectorSource = new S101VectorSource(dataset);
        var features = vectorSource.GetFeatures();
        var featureGeometry = new Dictionary<long, (GeometryType Type, IReadOnlyList<(double Lat, double Lon)> Coords)>();
        foreach (var f in features)
        {
            featureGeometry[f.Id] = (f.GeometryType, f.Coordinates);
        }

        // 2. Sort instructions by rendering order: areas first, then lines, then points/text
        //    Within same type, sort by DrawingPriority
        var sorted = instructions
            .OrderBy(i => i.DisplayPlane == "OverRadar" ? 1 : 0)
            .ThenBy(i => i.Type switch
            {
                InstructionType.AreaFill => 0,
                InstructionType.Line => 1,
                InstructionType.Point => 2,
                InstructionType.Text => 3,
                _ => 4,
            })
            .ThenBy(i => i.DrawingPriority)
            .ToList();

        // 3. Build color resolver from palette
        var resolveColor = BuildColorResolver(Palette);

        // 3a. Merge consecutive SAFCON point instructions into text labels
        var merged = MergeSafconLabels(sorted);

        // 4. Convert each instruction to a Mapsui feature
        var mapFeatures = new List<IFeature>();
        foreach (var instruction in merged)
        {
            if (!long.TryParse(instruction.FeatureRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var featureId))
                continue;

            if (!featureGeometry.TryGetValue(featureId, out var geom))
                continue;

            if (geom.Coords.Count == 0)
                continue;

            var mapFeature = CreateMapFeature(instruction, geom.Type, geom.Coords, resolveColor, this);
            if (mapFeature is not null)
                mapFeatures.Add(mapFeature);
        }

        return new MemoryLayer
        {
            Name = LayerName,
            Features = mapFeatures,
            Style = null,
        };
    }

    private static IFeature? CreateMapFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
        switch (instruction.Type)
        {
            case InstructionType.AreaFill:
                return CreateAreaFeature(instruction, geomType, coords, resolveColor, renderer);

            case InstructionType.Line:
                return CreateLineFeature(instruction, geomType, coords, resolveColor);

            case InstructionType.Point:
                return CreatePointFeature(instruction, coords, resolveColor, renderer);

            case InstructionType.Text:
                return CreateTextFeature(instruction, coords, resolveColor);

            default:
                return null;
        }
    }

    private static IFeature? CreateAreaFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
        if (coords.Count < 3)
            return null;

        var projected = ProjectCoordinates(coords);

        // Close the ring if not already closed
        var ringCoords = new List<Coordinate>(projected);
        if (ringCoords.Count > 0 && !ringCoords[0].Equals2D(ringCoords[^1]))
            ringCoords.Add(new Coordinate(ringCoords[0].X, ringCoords[0].Y));

        if (ringCoords.Count < 4)
            return null;

        var ring = new LinearRing(ringCoords.ToArray());
        var polygon = new Polygon(ring);

        if (instruction.IsColorFill)
        {
            // Solid color fill
            var fillColor = resolveColor(instruction.SymbolRef);

            if (instruction.Transparency.HasValue)
            {
                int alpha = (int)(255 * (1.0 - instruction.Transparency.Value));
                fillColor = new MapsuiColor(fillColor.R, fillColor.G, fillColor.B, alpha);
            }

            var style = new VectorStyle
            {
                Fill = new Brush { Color = fillColor },
                Outline = new Pen { Color = new MapsuiColor(0, 0, 0, 40), Width = 0.5 },
            };

            var feature = new GeometryFeature(polygon);
            feature.Styles.Add(style);
            return feature;
        }
        else
        {
            // Pattern fill via AreaFillReference
            var patternResult = renderer.GetPatternTileSource(instruction.SymbolRef);
            if (patternResult is null)
                return null;

            var (tileSource, _) = patternResult.Value;

            var brush = new Brush
            {
                Image = new Image { Source = tileSource },
            };

            var style = new VectorStyle
            {
                Fill = brush,
                Outline = new Pen { Color = new MapsuiColor(0, 0, 0, 40), Width = 0.5 },
            };

            var feature = new GeometryFeature(polygon);
            feature.Styles.Add(style);
            return feature;
        }
    }

    private static IFeature? CreateLineFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor)
    {
        if (coords.Count < 2)
            return null;

        var projected = ProjectCoordinates(coords);
        var lineString = new LineString(projected.ToArray());

        var lineColor = resolveColor(instruction.LineColor);
        var pen = new Pen
        {
            Color = lineColor,
            Width = Math.Max(instruction.LineWidth, 0.5),
        };

        if (instruction.Dashes is { Count: > 0 })
        {
            pen.PenStyle = PenStyle.Dash;
        }

        var style = new VectorStyle
        {
            Line = pen,
            Fill = null,
            Outline = null,
        };

        var feature = new GeometryFeature(lineString);
        feature.Styles.Add(style);
        return feature;
    }

    private static IFeature? CreatePointFeature(
        ParsedDrawingInstruction instruction,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
        if (coords.Count == 0)
            return null;

        // Use first coordinate as the point location
        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var feature = new PointFeature(mx, my);

        // Try to render with an actual SVG symbol
        var symbolRef = instruction.SymbolRef;
        var svgSource = renderer.GetSymbolSource(symbolRef);
        if (svgSource is not null)
        {
            var style = new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
            };
            style.SymbolScale = 0.6 * instruction.ScaleFactor;
            if (instruction.Rotation.HasValue)
                style.SymbolRotation = instruction.Rotation.Value;
            feature.Styles.Add(style);
        }
        else
        {
            // Fallback: colored dot
            var symbolColor = ResolveSymbolColor(symbolRef, resolveColor);
            var style = new SymbolStyle
            {
                SymbolScale = 0.15 * instruction.ScaleFactor,
                Fill = new Brush { Color = symbolColor },
                Line = null,
            };
            if (instruction.Rotation.HasValue)
                style.SymbolRotation = instruction.Rotation.Value;
            feature.Styles.Add(style);
        }

        return feature;
    }

    private static IFeature? CreateTextFeature(
        ParsedDrawingInstruction instruction,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor)
    {
        if (coords.Count == 0 || string.IsNullOrEmpty(instruction.Text))
            return null;

        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var textColor = resolveColor(instruction.FontColor);
        var style = new LabelStyle
        {
            Text = instruction.Text,
            ForeColor = textColor,
            Font = new Font { Size = instruction.FontSize },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
        };

        var feature = new PointFeature(mx, my);
        feature.Styles.Add(style);
        return feature;
    }

    // ── Coordinate projection ──────────────────────────────────────────

    private static List<Coordinate> ProjectCoordinates(IReadOnlyList<(double Lat, double Lon)> coords)
    {
        var result = new List<Coordinate>(coords.Count);
        foreach (var (lat, lon) in coords)
        {
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            result.Add(new Coordinate(mx, my));
        }
        return result;
    }

    // ── SVG symbol processing ──────────────────────────────────────────

    /// <summary>
    /// Returns a Mapsui svg-content:// source string for the given symbol name,
    /// processing and caching the SVG on first access. Returns null if no
    /// SymbolProvider is set or the symbol is not found.
    /// </summary>
    private string? GetSymbolSource(string? symbolRef)
    {
        if (string.IsNullOrEmpty(symbolRef) || SymbolProvider is null)
            return null;

        if (_symbolDataUriCache.TryGetValue(symbolRef, out var cached))
            return cached;

        string? source = null;
        try
        {
            var svgContent = SymbolProvider(symbolRef);
            if (svgContent is not null)
            {
                var processed = ProcessSvg(svgContent, Palette);
                source = "svg-content://" + processed;
            }
        }
        catch
        {
            // Symbol not found or malformed — fall back to dot
        }

        _symbolDataUriCache[symbolRef] = source;
        return source;
    }

    // ── Pattern tile rasterization ─────────────────────────────────────

    // Pixels per mm used when rasterizing SVG pattern tiles.
    // S-100 defines pattern dimensions in mm for paper charts (~3.78 px/mm at 96 DPI).
    // For interactive display we use a lower density so patterns repeat more tightly
    // relative to the on-screen polygon size.
    private const double PixelsPerMm = 1.5;

    /// <summary>
    /// Returns a Mapsui base64-content:// source string for the given area fill name,
    /// rasterizing the pattern SVG to a tiled bitmap on first access.
    /// </summary>
    private (string Source, AreaFill Fill)? GetPatternTileSource(string? fillName)
    {
        if (string.IsNullOrEmpty(fillName) || AreaFillProvider is null || SymbolProvider is null)
            return null;

        if (_patternTileCache.TryGetValue(fillName, out var cached))
            return cached;

        (string Source, AreaFill Fill)? result = null;
        try
        {
            var areaFill = AreaFillProvider(fillName);
            if (areaFill?.PatternSymbol is not null)
            {
                var svgContent = SymbolProvider(areaFill.PatternSymbol);
                if (svgContent is not null)
                {
                    var processed = ProcessSvg(svgContent, Palette);
                    var tileSource = RasterizePatternTile(processed, areaFill);
                    if (tileSource is not null)
                    {
                        result = (tileSource, areaFill);
                    }
                }
            }
        }
        catch
        {
            // Area fill or symbol not found — skip pattern
        }

        _patternTileCache[fillName] = result;
        return result;
    }

    /// <summary>
    /// Rasterizes a processed SVG pattern into a repeating tile bitmap.
    /// Returns a base64-content:// source suitable for <see cref="Brush.Image"/>.
    /// </summary>
    private static string? RasterizePatternTile(string processedSvg, AreaFill areaFill)
    {
        // Parse SVG into an SkiaSharp picture via Svg.Skia
        using var svg = SKSvg.CreateFromSvg(processedSvg);
        if (svg is null) return null;

        var picture = svg.Picture;
        if (picture is null) return null;

        var svgBounds = picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return null;

        // Determine tile dimensions from the tiling vectors.
        // v1 defines horizontal repeat spacing; v2 defines vertical + optional horizontal offset.
        double tileWidthMm = Math.Abs(areaFill.V1X);
        double tileHeightMm = Math.Abs(areaFill.V2Y);
        if (tileWidthMm <= 0) tileWidthMm = svgBounds.Width;
        if (tileHeightMm <= 0) tileHeightMm = svgBounds.Height;

        bool hasOffset = Math.Abs(areaFill.V2X) > 0.01;

        // For parallelogram lattices (v2.x != 0), create a double-height tile
        // with the second row offset by v2.x, producing the correct brick-like pattern.
        double totalHeightMm = hasOffset ? tileHeightMm * 2 : tileHeightMm;

        int tileW = Math.Max(1, (int)Math.Round(tileWidthMm * PixelsPerMm));
        int tileH = Math.Max(1, (int)Math.Round(totalHeightMm * PixelsPerMm));

        // Cap tile size for sanity
        if (tileW > 512 || tileH > 512) return null;

        using var bitmap = new SKBitmap(tileW, tileH);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Scale the SVG to fit within one tile cell
        float svgW = svgBounds.Width;
        float svgH = svgBounds.Height;
        float cellW = (float)(tileWidthMm * PixelsPerMm);
        float cellH = (float)(tileHeightMm * PixelsPerMm);
        float scaleX = cellW / svgW;
        float scaleY = cellH / svgH;
        float scale = Math.Min(scaleX, scaleY);

        // Center the SVG in the cell
        float scaledW = svgW * scale;
        float scaledH = svgH * scale;
        float offsetX = (cellW - scaledW) / 2;
        float offsetY = (cellH - scaledH) / 2;

        // Draw the SVG at position (0,0) for the first row
        canvas.Save();
        canvas.Translate(offsetX - svgBounds.Left * scale, offsetY - svgBounds.Top * scale);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();

        // For parallelogram lattices, draw a second copy offset for the second row
        if (hasOffset)
        {
            float offset2X = (float)(areaFill.V2X * PixelsPerMm);
            canvas.Save();
            canvas.Translate(offset2X + offsetX - svgBounds.Left * scale, cellH + offsetY - svgBounds.Top * scale);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }

        canvas.Flush();

        // Encode to PNG and return as base64-content:// source
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var base64 = Convert.ToBase64String(data.ToArray());
        return "base64-content://" + base64;
    }

    /// <summary>
    /// Processes an S-100 SVG symbol by:
    /// 1. Removing layout elements (symbolBox, svgBox, pivotPoint)
    /// 2. Removing the xml-stylesheet PI
    /// 3. Resolving CSS class names (fTOKEN, sTOKEN, f0, sl) to inline style attributes
    /// </summary>
    private static string ProcessSvg(string svgContent, ColorPalette? palette)
    {
        var doc = XDocument.Parse(svgContent);
        XNamespace ns = "http://www.w3.org/2000/svg";

        var svg = doc.Root!;

        // Remove xml-stylesheet processing instructions (they reference external CSS
        // files that Mapsui's SVG rasterizer cannot resolve)
        foreach (var pi in doc.Nodes().OfType<XProcessingInstruction>().ToList())
            pi.Remove();

        // Remove elements with class containing "layout"
        var layoutElements = svg.Descendants()
            .Where(e => (e.Attribute("class")?.Value ?? "").Contains("layout"))
            .ToList();
        foreach (var el in layoutElements)
            el.Remove();

        // Process remaining elements: resolve CSS classes to inline styles
        foreach (var el in svg.Descendants().ToList())
        {
            var classAttr = el.Attribute("class");
            if (classAttr is null) continue;

            var classes = classAttr.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? fill = null;
            string? stroke = null;
            string? strokeLinecap = null;
            string? strokeLinejoin = null;

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
                else if (cls.StartsWith('f') && cls.Length > 1 && char.IsUpper(cls[1]))
                {
                    // Fill class: fTOKEN (e.g. fCHBLK → fill:#000000)
                    var token = cls[1..];
                    fill = ResolveTokenHex(token, palette);
                }
                else if (cls.StartsWith('s') && cls.Length > 1 && char.IsUpper(cls[1]))
                {
                    // Stroke class: sTOKEN (e.g. sCHBLK → stroke:#000000)
                    var token = cls[1..];
                    stroke = ResolveTokenHex(token, palette);
                }
            }

            // Build inline style, preserving existing attributes
            if (fill is not null && el.Attribute("fill") is null)
                el.SetAttributeValue("fill", fill);
            if (stroke is not null && el.Attribute("stroke") is null)
                el.SetAttributeValue("stroke", stroke);
            if (strokeLinecap is not null && el.Attribute("stroke-linecap") is null)
                el.SetAttributeValue("stroke-linecap", strokeLinecap);
            if (strokeLinejoin is not null && el.Attribute("stroke-linejoin") is null)
                el.SetAttributeValue("stroke-linejoin", strokeLinejoin);

            // Remove the class attribute since we've inlined the styles
            classAttr.Remove();
        }

        // Remove metadata (not needed for rendering)
        svg.Elements(ns + "metadata").Remove();
        svg.Elements(ns + "title").Remove();
        svg.Elements(ns + "desc").Remove();

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static string ResolveTokenHex(string token, ColorPalette? palette)
    {
        if (palette is not null)
        {
            var hex = palette.Resolve(token);
            if (hex != "#000000" || string.Equals(token, "CHBLK", StringComparison.OrdinalIgnoreCase))
                return hex;
        }

        // Fallback: return black
        return "#000000";
    }

    // ── SAFCON contour label merging ───────────────────────────────────
    //
    // The S-101 Lua portrayal emits depth contour labels as sequences of
    // PointInstruction:SAFCONxy symbols, where each symbol represents a
    // single positioned digit glyph in an SVG composition. Since we don't
    // yet render SVG symbols, we decode the SAFCON sequence back into a
    // depth text string and emit a single text instruction instead.
    //
    // SAFCON encoding (from SAFCON01.lua):
    //   Row 0: single/middle digit    Row 5: fractional (depth 10–30)
    //   Row 1: units of 2-digit       Row 6: fractional (depth <10)
    //   Row 2: tens of 2-digit        Row 7: last digit of 4/5-digit
    //   Row 3: first of 4-digit       Row 8: first of 3-digit
    //   Row 4: first of 5-digit       Row 9: third of 3-digit

    private static List<ParsedDrawingInstruction> MergeSafconLabels(List<ParsedDrawingInstruction> instructions)
    {
        var result = new List<ParsedDrawingInstruction>(instructions.Count);

        // Group consecutive SAFCON points by feature
        var i = 0;
        while (i < instructions.Count)
        {
            var instr = instructions[i];

            if (instr.Type == InstructionType.Point && IsSafconSymbol(instr.SymbolRef))
            {
                // Collect all consecutive SAFCON instructions for this feature
                var safcons = new List<ParsedDrawingInstruction> { instr };
                var j = i + 1;
                while (j < instructions.Count &&
                       instructions[j].Type == InstructionType.Point &&
                       instructions[j].FeatureRef == instr.FeatureRef &&
                       IsSafconSymbol(instructions[j].SymbolRef))
                {
                    safcons.Add(instructions[j]);
                    j++;
                }

                // Decode the SAFCON sequence into a depth string
                var depthText = DecodeSafconSequence(safcons);

                // Emit a synthetic text instruction
                result.Add(new ParsedDrawingInstruction
                {
                    FeatureRef = instr.FeatureRef,
                    Type = InstructionType.Text,
                    Text = depthText,
                    ViewingGroup = instr.ViewingGroup,
                    DrawingPriority = instr.DrawingPriority,
                    DisplayPlane = instr.DisplayPlane,
                    FontSize = 10,
                    FontColor = "SNDG2",
                    ScaleMinimum = instr.ScaleMinimum,
                    ScaleMaximum = instr.ScaleMaximum,
                });

                i = j;
            }
            else
            {
                result.Add(instr);
                i++;
            }
        }

        return result;
    }

    private static bool IsSafconSymbol(string? symbolRef)
    {
        return symbolRef is not null &&
               symbolRef.StartsWith("SAFCON", StringComparison.Ordinal) &&
               symbolRef.Length == 8;
    }

    private static string DecodeSafconSequence(List<ParsedDrawingInstruction> safcons)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var instr in safcons)
        {
            var name = instr.SymbolRef!;
            // SAFCONxy — x is row (position type), y is digit
            var row = name[6] - '0';
            var digit = name[7];

            if (row == 5 || row == 6)
            {
                // Fractional digit — prepend decimal point
                sb.Append('.');
            }

            sb.Append(digit);
        }

        return sb.ToString();
    }

    // ── S-100 Color resolution ─────────────────────────────────────────

    /// <summary>
    /// Builds a color resolver function from the given palette.
    /// Falls back to black for unknown tokens.
    /// </summary>
    private static Func<string?, MapsuiColor> BuildColorResolver(ColorPalette? palette)
    {
        return token =>
        {
            if (string.IsNullOrEmpty(token))
                return MapsuiColor.Black;

            if (palette is not null)
            {
                var hex = palette.Resolve(token);
                if (hex != "#000000" || string.Equals(token, "CHBLK", StringComparison.OrdinalIgnoreCase))
                {
                    return HexToColor(hex);
                }
            }

            return MapsuiColor.Black;
        };
    }

    private static MapsuiColor HexToColor(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#' &&
            int.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            int.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            int.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new MapsuiColor(r, g, b);
        }

        return MapsuiColor.Black;
    }

    private static MapsuiColor ResolveSymbolColor(string? symbolRef, Func<string?, MapsuiColor> resolveColor)
    {
        if (string.IsNullOrEmpty(symbolRef))
            return MapsuiColor.Black;

        // Symbol names like QUESMRK1, SAFCON03, BOYCAR01 etc.
        // Map by prefix/known names to approximate colours
        if (symbolRef.StartsWith("SAFCON", StringComparison.Ordinal))
            return resolveColor("SNDG1");     // Sounding symbols — use sounding colour
        if (symbolRef.StartsWith("BOYCAR", StringComparison.Ordinal) ||
            symbolRef.StartsWith("BOYLAT", StringComparison.Ordinal))
            return resolveColor("CHBLK");     // Buoy symbols — black
        if (symbolRef.StartsWith("BCNLAT", StringComparison.Ordinal))
            return resolveColor("CHBLK");     // Beacon symbols — black
        if (symbolRef == "QUESMRK1")
            return new MapsuiColor(200, 0, 200, 120); // Default/unknown — faint magenta
        if (symbolRef.StartsWith("LIGHTS", StringComparison.Ordinal))
            return resolveColor("LITYW");     // Lights — yellow

        return resolveColor("OUTLW");
    }
}
