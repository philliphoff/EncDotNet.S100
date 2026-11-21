using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Serializes a vector dataset's resolved S-100 Part 9 portrayal to a stable,
/// deterministic JSON document — the non-image output of <c>s100 render</c>
/// (<c>--format json</c>). The document is the drawing the renderer makes,
/// handed out as data so a renderer in another process (GIS symbology, vector
/// PDF or SVG) can draw it the same way.
/// </summary>
/// <remarks>
/// <para>
/// Each sub-layer of the portrayal is lowered by
/// <see cref="HeadlessVectorRenderer.BuildScene"/>, the lowering the image
/// path and the compositor draw from, and every <see cref="PaintOp"/> of the
/// resulting <see cref="VectorScene"/> becomes one entry of <c>ops</c>, in
/// draw order. So the JSON carries line suppression (one entry per edge left
/// to stroke), multi-part splitting, pattern priority clipping, resolved
/// colours, sizes in display pixels and the chosen anchors, exactly as drawn.
/// The fields an op does not keep (symbol, line style and font names, display
/// plane, viewing groups, drawing priority) come from the display-list
/// instruction it was built from (<see cref="PaintOp.Instruction"/>).
/// </para>
/// <para>
/// The document has no timing, encoder settings or viewport, so two runs over
/// the same dataset and <see cref="RenderContext"/> are byte-identical. The
/// shape is versioned by <see cref="SchemaVersion"/> and documented in
/// <c>docs/cli.md</c> (Display-list JSON).
/// </para>
/// </remarks>
internal static partial class DisplayListJsonWriter
{
    /// <summary>
    /// The version of the document shape, written as <c>schemaVersion</c>.
    /// Bumped whenever a field is removed or changes meaning; adding a field
    /// does not bump it.
    /// </summary>
    public const int SchemaVersion = 2;

    // ~1 cm at the equator: finer than any chart needs, and keeps the output stable.
    private const int CoordinateDecimals = 7;
    private const int SizeDecimals = 4;

    private static readonly DisplayListJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    });

    /// <summary>
    /// Serializes the portrayal carried by <paramref name="result"/> to an
    /// indented JSON string.
    /// </summary>
    /// <param name="result">The built vector portrayal (sub-layers + geometry + catalogue resolvers).</param>
    /// <param name="datasetName">The source dataset file name, echoed into the document.</param>
    /// <param name="palette">The active palette token (e.g. <c>day</c>).</param>
    /// <param name="hiddenCategories">Instruction categories left out, as the image path leaves them out.</param>
    /// <returns>
    /// The serialized JSON document, terminated with a single trailing <c>\n</c>.
    /// A fixed newline (rather than <see cref="Environment.NewLine"/>) keeps the
    /// output byte-identical across operating systems for stable snapshot/diffing.
    /// </returns>
    public static string Serialize(
        VectorPortrayalResult result,
        string datasetName,
        string palette,
        DrawingInstructionCategory hiddenCategories = DrawingInstructionCategory.None)
    {
        ArgumentNullException.ThrowIfNull(result);

        var subLayers = new List<SubLayerDto>(result.SubLayers.Count);
        var ops = new List<OpDto>();
        int instructionCount = 0;
        for (int index = 0; index < result.SubLayers.Count; index++)
        {
            var subLayer = result.SubLayers[index];
            instructionCount += subLayer.Instructions.Count;
            subLayers.Add(new SubLayerDto
            {
                Index = index,
                Key = subLayer.LayerKey,
                Name = subLayer.LayerName,
                Plane = subLayer.Plane.ToString(),
                InstructionCount = subLayer.Instructions.Count,
            });

            var scene = HeadlessVectorRenderer.BuildScene(
                subLayer.Instructions,
                result.GeometryProvider,
                result.Palette,
                result.SymbolProvider,
                result.LineStyleProvider,
                result.SymbolScale,
                result.TextScale,
                result.AreaFillProvider,
                hiddenCategories);

            foreach (var op in scene.Ops)
            {
                if (ToDto(op, subLayer.Instructions) is { } dto)
                {
                    dto.Order = ops.Count;
                    dto.SubLayer = index;
                    ops.Add(dto);
                }
            }
        }

        var document = new DisplayListDto
        {
            SchemaVersion = SchemaVersion,
            Dataset = datasetName,
            Product = result.Product,
            Spec = result.Spec.ToString(),
            Palette = palette,
            SymbolScale = result.SymbolScale,
            TextScale = result.TextScale,
            Crs = "EPSG:4326",
            SizeProfile = new SizeProfileDto
            {
                SvgPixelsPerMillimetre = Size(SymbolSizeProfile.SvgPixelsPerMillimetre),
                PointSymbolScale = SymbolSizeProfile.Screen.PointSymbolScale,
                MillimetresPerPixel = SymbolSizeProfile.Screen.MillimetresPerPixel,
                PatternPixelsPerMillimetre = SymbolSizeProfile.Screen.PatternPixelsPerMillimetre,
                PixelsPerFontPoint = SymbolSizeProfile.Screen.PixelsPerFontPoint,
            },
            SubLayerCount = result.SubLayers.Count,
            InstructionCount = instructionCount,
            OpCount = ops.Count,
            CategoryCounts = new CategoryCountsDto
            {
                Areas = ops.Count(o => o.Kind == "area"),
                Patterns = ops.Count(o => o.Kind == "pattern"),
                Lines = ops.Count(o => o.Kind == "line"),
                Points = ops.Count(o => o.Kind == "point"),
                Text = ops.Count(o => o.Kind == "text"),
            },
            SubLayers = subLayers,
            Ops = ops,
        };

        return JsonSerializer.Serialize(document, Json.DisplayListDto) + "\n";
    }

    private static OpDto? ToDto(PaintOp op, IReadOnlyList<DrawingInstruction> subLayerInstructions)
    {
        var instruction = op.Instruction ?? SourceOf(op, subLayerInstructions);

        OpDto? dto = op switch
        {
            AreaPaintOp area => new OpDto
            {
                Kind = "area",
                Rings = Rings(area.WorldShell, area.WorldHoles),
                Fill = Hex(area.Fill),
                FillToken = (instruction as AreaInstruction)?.FillColor,
                Transparency = (instruction as AreaInstruction)?.Transparency is > 0 and var t ? t : null,
                Outline = area.OutlineWidthPx > 0
                    ? new OutlineDto { Color = Hex(area.OutlineColor), WidthPx = Size(area.OutlineWidthPx) }
                    : null,
            },

            // A pattern op's rings are the visible region after S-100 Part 9
            // priority clipping (PatternPriorityClipper): where a higher-priority
            // pattern or an opaque fill covers it, it is already cut away.
            // Issue #1510 (N3) adds which pattern wins where; its fields go here.
            PatternAreaPaintOp pattern => new OpDto
            {
                Kind = "pattern",
                Rings = Rings(pattern.WorldShell, pattern.WorldHoles),
                AreaFill = pattern.PatternReference,
            },

            LinePaintOp line => new OpDto
            {
                Kind = "line",
                Coordinates = Polyline(line.World),
                LineStyle = (instruction as LineInstruction)?.LineStyleReference,
                Stroke = line.Stroke,
                Color = Hex(line.Color),
                ColorToken = (instruction as LineInstruction)?.LineColor,
                WidthPx = Size(line.WidthPx),
                DashArrayPx = line.DashArrayPx?.Select(d => Size(d)).ToList(),
                DashPhasePx = line.DashPhasePx != 0 ? Size(line.DashPhasePx) : null,
                DefaultDash = line.DefaultDash ? true : null,
                SymbolIntervalPx = line.Symbols.Count > 0 ? Size(line.SymbolIntervalPx) : null,
                LineSymbols = line.Symbols.Count > 0
                    ? line.Symbols.Select(s => new LineSymbolDto
                    {
                        Symbol = s.Reference,
                        PositionPx = Size(s.PositionPx),
                        Rotation = s.RotationDegrees,
                        Scale = Size(s.Symbol.Scale),
                        Pivot = Pivot(s.Symbol),
                    }).ToList()
                    : null,
            },

            PointPaintOp point => new OpDto
            {
                Kind = "point",
                Anchor = LonLat(point.World),
                Placement = Placement(point.Placement),
                Symbol = (instruction as PointInstruction)?.SymbolReference,
                Scale = point.Symbol is { } resolved ? Size(resolved.Scale) : null,
                Pivot = point.Symbol is { } withPivot ? Pivot(withPivot) : null,
                FallbackColor = point.Symbol is null ? Hex(point.FallbackColor) : null,
                Rotation = point.Rotation,
                RotationCrs = point.Rotation is null ? null : point.RotationCrs.ToString(),
                OffsetPx = Offset(point.OffsetXpx, point.OffsetYpx),
            },

            TextPaintOp text => new OpDto
            {
                Kind = "text",
                Anchor = LonLat(text.World),
                Placement = Placement(text.Placement),
                Text = text.Text,
                Font = (instruction as TextInstruction)?.FontReference,
                FontSizePx = Size(text.FontSizePx),
                FontWeight = text.FontWeight.ToString(),
                FontProportion = text.FontProportion.ToString(),
                FontSlant = text.FontSlant.ToString(),
                Color = Hex(text.ForeColor),
                ColorToken = (instruction as TextInstruction)?.FontColor,
                Background = text.BackColor is { } back ? Hex(back) : null,
                Halo = text.HaloColor is { } halo
                    ? new OutlineDto { Color = Hex(halo), WidthPx = Size(text.HaloWidthPx) }
                    : null,
                HorizontalAlignment = text.HorizontalAlignment.ToString(),
                VerticalAlignment = text.VerticalAlignment.ToString(),
                OffsetPx = Offset(text.OffsetXpx, text.OffsetYpx),
                Rotation = (instruction as TextInstruction)?.Rotation,
            },

            _ => null,
        };

        if (dto is null)
            return null;

        dto.Feature = op.FeatureReference;
        dto.ScaleMinimum = op.ScaleMinimum;
        dto.ScaleMaximum = op.ScaleMaximum;
        if (instruction is not null)
        {
            dto.Plane = instruction.Plane.ToString();
            dto.ViewingGroups = [instruction.ViewingGroup, .. instruction.AdditionalViewingGroups];
            dto.DrawingPriority = instruction.DrawingPriority;
        }

        return dto;
    }

    /// <summary>
    /// The instruction an op without a back-reference came from: a pattern op
    /// rebuilt by priority clipping keeps the feature and pattern of the first
    /// op of its group, so the matching area instruction gives its plane,
    /// viewing groups and priority.
    /// </summary>
    private static DrawingInstruction? SourceOf(PaintOp op, IReadOnlyList<DrawingInstruction> instructions)
    {
        if (op is not PatternAreaPaintOp pattern)
            return null;

        foreach (var instruction in instructions)
        {
            if (instruction is AreaInstruction { AreaFillReference: { } fill } area
                && string.Equals(area.FeatureReference, pattern.FeatureReference, StringComparison.Ordinal)
                && string.Equals(fill, pattern.PatternReference, StringComparison.OrdinalIgnoreCase))
            {
                return area;
            }
        }

        return null;
    }

    private static double[] LonLat((double X, double Y) world)
    {
        var (longitude, latitude) = WebMercator.ToLonLat(world.X, world.Y, clampLatitude: false);
        return [Math.Round(longitude, CoordinateDecimals), Math.Round(latitude, CoordinateDecimals)];
    }

    private static List<double[]> Polyline(IReadOnlyList<(double X, double Y)> world)
    {
        var coordinates = new List<double[]>(world.Count);
        foreach (var vertex in world)
            coordinates.Add(LonLat(vertex));
        return coordinates;
    }

    private static List<List<double[]>> Rings(
        IReadOnlyList<(double X, double Y)> shell,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes)
    {
        var rings = new List<List<double[]>>(1 + holes.Count) { Polyline(shell) };
        foreach (var hole in holes)
            rings.Add(Polyline(hole));
        return rings;
    }

    private static PlacementDto Placement(AnchorPlacement placement) => new()
    {
        Kind = placement.Kind.ToString(),
        Part = placement.Part,
        LinePosition = placement.Kind == AnchorPlacementKind.Line ? placement.LinePosition : null,
        Segment = placement.Kind is AnchorPlacementKind.Line or AnchorPlacementKind.CurveMiddleVertex
            ? placement.Segment
            : null,
        SegmentPosition = placement.Kind == AnchorPlacementKind.Line ? placement.SegmentPosition : null,
        VisibleParts = placement.VisibleParts ? true : null,
    };

    private static double[] Pivot(ResolvedSymbol symbol) =>
        [Math.Round(symbol.PivotRelativeX, SizeDecimals), Math.Round(symbol.PivotRelativeY, SizeDecimals)];

    private static double[]? Offset(double x, double y) =>
        x == 0 && y == 0 ? null : [Size(x), Size(y)];

    private static double Size(double value) => Math.Round(value, SizeDecimals);

    private static string Hex(RgbaColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private sealed class DisplayListDto
    {
        public int SchemaVersion { get; init; }
        public required string Dataset { get; init; }
        public required string Product { get; init; }
        public required string Spec { get; init; }
        public required string Palette { get; init; }
        public double SymbolScale { get; init; }
        public double TextScale { get; init; }
        public required string Crs { get; init; }
        public required SizeProfileDto SizeProfile { get; init; }
        public int SubLayerCount { get; init; }
        public int InstructionCount { get; init; }
        public int OpCount { get; init; }
        public required CategoryCountsDto CategoryCounts { get; init; }
        public required IReadOnlyList<SubLayerDto> SubLayers { get; init; }
        public required IReadOnlyList<OpDto> Ops { get; init; }
    }

    private sealed class SizeProfileDto
    {
        public double SvgPixelsPerMillimetre { get; init; }
        public double PointSymbolScale { get; init; }
        public double MillimetresPerPixel { get; init; }
        public double PatternPixelsPerMillimetre { get; init; }
        public double PixelsPerFontPoint { get; init; }
    }

    private sealed class CategoryCountsDto
    {
        public int Areas { get; init; }
        public int Patterns { get; init; }
        public int Lines { get; init; }
        public int Points { get; init; }
        public int Text { get; init; }
    }

    private sealed class SubLayerDto
    {
        public int Index { get; init; }
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Plane { get; init; }
        public int InstructionCount { get; init; }
    }

    private sealed class PlacementDto
    {
        public required string Kind { get; init; }
        public int Part { get; init; }
        public double? LinePosition { get; init; }
        public int? Segment { get; init; }
        public double? SegmentPosition { get; init; }
        public bool? VisibleParts { get; init; }
    }

    private sealed class OutlineDto
    {
        public required string Color { get; init; }
        public double WidthPx { get; init; }
    }

    private sealed class LineSymbolDto
    {
        public string? Symbol { get; init; }
        public double PositionPx { get; init; }
        public double Rotation { get; init; }
        public double Scale { get; init; }
        public required double[] Pivot { get; init; }
    }

    private sealed class OpDto
    {
        public required string Kind { get; init; }
        public int Order { get; set; }
        public int SubLayer { get; set; }
        public string? Feature { get; set; }
        public string? Plane { get; set; }
        public IReadOnlyList<int>? ViewingGroups { get; set; }
        public int? DrawingPriority { get; set; }
        public double? ScaleMinimum { get; set; }
        public double? ScaleMaximum { get; set; }

        // Geometry: one of these, by kind.
        public double[]? Anchor { get; init; }
        public IReadOnlyList<double[]>? Coordinates { get; init; }
        public IReadOnlyList<List<double[]>>? Rings { get; init; }
        public PlacementDto? Placement { get; init; }

        // Area / pattern
        public string? Fill { get; init; }
        public string? FillToken { get; init; }
        public double? Transparency { get; init; }
        public OutlineDto? Outline { get; init; }
        public string? AreaFill { get; init; }

        // Line
        public string? LineStyle { get; init; }
        public bool? Stroke { get; init; }
        public double? WidthPx { get; init; }
        public IReadOnlyList<double>? DashArrayPx { get; init; }
        public double? DashPhasePx { get; init; }
        public bool? DefaultDash { get; init; }
        public double? SymbolIntervalPx { get; init; }
        public IReadOnlyList<LineSymbolDto>? LineSymbols { get; init; }

        // Point
        public string? Symbol { get; init; }
        public double? Scale { get; init; }
        public double[]? Pivot { get; init; }
        public string? FallbackColor { get; init; }
        public double? Rotation { get; init; }
        public string? RotationCrs { get; init; }

        // Point and text
        public double[]? OffsetPx { get; init; }

        // Text
        public string? Text { get; init; }
        public string? Font { get; init; }
        public double? FontSizePx { get; init; }
        public string? FontWeight { get; init; }
        public string? FontProportion { get; init; }
        public string? FontSlant { get; init; }
        public string? Background { get; init; }
        public OutlineDto? Halo { get; init; }
        public string? HorizontalAlignment { get; init; }
        public string? VerticalAlignment { get; init; }

        // Line and text
        public string? Color { get; init; }
        public string? ColorToken { get; init; }
    }

    [JsonSerializable(typeof(DisplayListDto))]
    private sealed partial class DisplayListJsonContext : JsonSerializerContext;
}
