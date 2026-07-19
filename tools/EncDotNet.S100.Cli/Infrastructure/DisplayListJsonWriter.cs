using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Serializes a vector dataset's post-portrayal S-100 Part 9 display list — the
/// ordered list of <see cref="DrawingInstruction"/> produced by a
/// <see cref="IVectorPortrayalSource"/> — to a stable, deterministic JSON
/// document. This is the non-image output format of <c>s100 render</c>
/// (<c>--format json</c>): it captures <em>what</em> the portrayal pipeline
/// decided to draw (symbol / line-style / area-fill references, colours,
/// display planes, priorities, text) rather than the rasterised pixels, so a
/// portrayal change can be diagnosed and snapshot-tested in text without a
/// viewer or image diff.
/// </summary>
/// <remarks>
/// The document is pure portrayal output: it contains no timing, no encoder
/// settings, and no viewport-dependent pixel coordinates, so two runs over the
/// same dataset and <see cref="RenderContext"/> produce byte-identical JSON.
/// Geometry is summarised (type, vertex count, and a representative anchor in
/// latitude/longitude) rather than dumped in full, keeping the output compact
/// and diffable.
/// </remarks>
internal static class DisplayListJsonWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Serializes the display list carried by <paramref name="result"/> to an
    /// indented JSON string.
    /// </summary>
    /// <param name="result">The built vector portrayal (sub-layers + geometry).</param>
    /// <param name="datasetName">The source dataset file name, echoed into the document.</param>
    /// <param name="palette">The active palette token (e.g. <c>day</c>).</param>
    /// <returns>
    /// The serialized JSON document, terminated with a single trailing <c>\n</c>.
    /// A fixed newline (rather than <see cref="Environment.NewLine"/>) keeps the
    /// output byte-identical across operating systems for stable snapshot/diffing.
    /// </returns>
    public static string Serialize(VectorPortrayalResult result, string datasetName, string palette)
    {
        ArgumentNullException.ThrowIfNull(result);

        var instructions = new List<InstructionDto>();
        for (int subLayer = 0; subLayer < result.SubLayers.Count; subLayer++)
        {
            foreach (var instruction in result.SubLayers[subLayer].Instructions)
                instructions.Add(ToDto(instruction, subLayer, result.GeometryProvider));
        }

        var document = new DisplayListDto
        {
            Dataset = datasetName,
            Product = result.Product,
            Spec = result.Spec.ToString(),
            Palette = palette,
            SymbolScale = result.SymbolScale,
            TextScale = result.TextScale,
            SubLayerCount = result.SubLayers.Count,
            InstructionCount = instructions.Count,
            CategoryCounts = new CategoryCountsDto
            {
                Areas = instructions.Count(i => i.Kind == "area"),
                Lines = instructions.Count(i => i.Kind == "line"),
                Points = instructions.Count(i => i.Kind == "point"),
                Text = instructions.Count(i => i.Kind == "text"),
            },
            Instructions = instructions,
        };

        return JsonSerializer.Serialize(document, Options) + "\n";
    }

    private static InstructionDto ToDto(
        DrawingInstruction instruction, int subLayer, IFeatureGeometryProvider geometry)
    {
        var dto = instruction switch
        {
            PointInstruction p => new InstructionDto
            {
                Kind = "point",
                Symbol = p.SymbolReference,
                SymbolScale = p.SymbolScale,
                Rotation = p.Rotation,
                LocalOffsetX = NonZero(p.LocalOffsetX),
                LocalOffsetY = NonZero(p.LocalOffsetY),
                LinePlacementPosition = p.LinePlacementPosition,
            },
            LineInstruction l => new InstructionDto
            {
                Kind = "line",
                LineStyle = l.LineStyleReference,
                LineWidth = NonZero(l.LineWidth),
                LineColor = l.LineColor,
                Dashes = l.Dashes?.Select(d => new[] { d.Offset, d.Length }).ToList(),
            },
            AreaInstruction a => new InstructionDto
            {
                Kind = "area",
                AreaFill = a.AreaFillReference,
                FillColor = a.FillColor,
                Transparency = a.Transparency,
                OutlineStyle = a.OutlineStyleReference,
            },
            TextInstruction t => new InstructionDto
            {
                Kind = "text",
                Text = t.Text,
                Font = t.FontReference,
                FontSize = t.FontSize,
                FontColor = t.FontColor,
                BackgroundColor = t.BackgroundColor,
                Rotation = t.Rotation,
                HorizontalAlignment = t.HorizontalAlignment.ToString(),
                VerticalAlignment = t.VerticalAlignment.ToString(),
            },
            _ => new InstructionDto { Kind = "unknown" },
        };

        dto.Feature = instruction.FeatureReference;
        dto.SubLayer = subLayer;
        dto.Plane = instruction.Plane.ToString();
        dto.ViewingGroup = instruction.ViewingGroup;
        dto.DrawingPriority = instruction.DrawingPriority;
        dto.ScaleMinimum = instruction.ScaleMinimum;
        dto.ScaleMaximum = instruction.ScaleMaximum;
        dto.Geometry = instruction is LineInstruction { CoordinatesOverride: { } overrideCoordinates }
            ? SummariseCoordinates(GeometryType.Curve, overrideCoordinates)
            : SummariseGeometry(geometry.GetGeometry(instruction.FeatureReference));
        return dto;
    }

    private static GeometrySummaryDto? SummariseGeometry(FeatureGeometry? geometry)
    {
        if (geometry is null)
            return null;

        return SummariseCoordinates(geometry.Type, geometry.Coordinates);
    }

    private static GeometrySummaryDto? SummariseCoordinates(
        GeometryType type, IReadOnlyList<GeoPosition> coordinates)
    {
        if (coordinates.Count == 0)
            return null;

        GeoPosition anchor = coordinates[0];
        return new GeometrySummaryDto
        {
            Type = type.ToString(),
            VertexCount = coordinates.Count,
            Anchor = [Math.Round(anchor.Latitude, 6), Math.Round(anchor.Longitude, 6)],
        };
    }

    private static double? NonZero(double value) => value == 0 ? null : value;

    private sealed class DisplayListDto
    {
        public required string Dataset { get; init; }
        public required string Product { get; init; }
        public required string Spec { get; init; }
        public required string Palette { get; init; }
        public double SymbolScale { get; init; }
        public double TextScale { get; init; }
        public int SubLayerCount { get; init; }
        public int InstructionCount { get; init; }
        public required CategoryCountsDto CategoryCounts { get; init; }
        public required IReadOnlyList<InstructionDto> Instructions { get; init; }
    }

    private sealed class CategoryCountsDto
    {
        public int Areas { get; init; }
        public int Lines { get; init; }
        public int Points { get; init; }
        public int Text { get; init; }
    }

    private sealed class GeometrySummaryDto
    {
        public required string Type { get; init; }
        public int VertexCount { get; init; }
        public required IReadOnlyList<double> Anchor { get; init; }
    }

    private sealed class InstructionDto
    {
        public required string Kind { get; init; }
        public string? Feature { get; set; }
        public int SubLayer { get; set; }
        public string? Plane { get; set; }
        public int ViewingGroup { get; set; }
        public int DrawingPriority { get; set; }
        public double? ScaleMinimum { get; set; }
        public double? ScaleMaximum { get; set; }

        // Point
        public string? Symbol { get; init; }
        public double? SymbolScale { get; init; }
        public double? Rotation { get; init; }
        public double? LocalOffsetX { get; init; }
        public double? LocalOffsetY { get; init; }
        public double? LinePlacementPosition { get; init; }

        // Line
        public string? LineStyle { get; init; }
        public double? LineWidth { get; init; }
        public string? LineColor { get; init; }
        public IReadOnlyList<double[]>? Dashes { get; init; }

        // Area
        public string? AreaFill { get; init; }
        public string? FillColor { get; init; }
        public double? Transparency { get; init; }
        public string? OutlineStyle { get; init; }

        // Text
        public string? Text { get; init; }
        public string? Font { get; init; }
        public double? FontSize { get; init; }
        public string? FontColor { get; init; }
        public string? BackgroundColor { get; init; }
        public string? HorizontalAlignment { get; init; }
        public string? VerticalAlignment { get; init; }

        public GeometrySummaryDto? Geometry { get; set; }
    }
}
