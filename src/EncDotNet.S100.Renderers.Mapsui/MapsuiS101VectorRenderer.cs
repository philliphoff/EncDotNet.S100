using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
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

            var mapFeature = CreateMapFeature(instruction, geom.Type, geom.Coords, resolveColor);
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
        Func<string?, MapsuiColor> resolveColor)
    {
        switch (instruction.Type)
        {
            case InstructionType.AreaFill:
                return CreateAreaFeature(instruction, geomType, coords, resolveColor);

            case InstructionType.Line:
                return CreateLineFeature(instruction, geomType, coords, resolveColor);

            case InstructionType.Point:
                return CreatePointFeature(instruction, coords, resolveColor);

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
        Func<string?, MapsuiColor> resolveColor)
    {
        if (coords.Count < 3)
            return null;

        // Non-colorFill area fills are pattern references (e.g. DIAMOND1, DQUALB01)
        // that require SVG symbol tiling. Skip them for now — the colorFill instructions
        // already provide the proper background colour for the area.
        if (!instruction.IsColorFill)
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

        var fillColor = resolveColor(instruction.SymbolRef);

        if (instruction.Transparency.HasValue)
        {
            int alpha = (int)(255 * (1.0 - instruction.Transparency.Value));
            fillColor = new Color(fillColor.R, fillColor.G, fillColor.B, alpha);
        }

        var style = new VectorStyle
        {
            Fill = new Brush { Color = fillColor },
            Outline = new Pen { Color = new Color(0, 0, 0, 40), Width = 0.5 },
        };

        var feature = new GeometryFeature(polygon);
        feature.Styles.Add(style);
        return feature;
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
        Func<string?, MapsuiColor> resolveColor)
    {
        if (coords.Count == 0)
            return null;

        // Use first coordinate as the point location
        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var symbolColor = ResolveSymbolColor(instruction.SymbolRef, resolveColor);
        var style = new SymbolStyle
        {
            SymbolScale = 0.15 * instruction.ScaleFactor,
            Fill = new Brush { Color = symbolColor },
            Line = null,
        };

        if (instruction.Rotation.HasValue)
            style.SymbolRotation = instruction.Rotation.Value;

        var feature = new PointFeature(mx, my);
        feature.Styles.Add(style);
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
