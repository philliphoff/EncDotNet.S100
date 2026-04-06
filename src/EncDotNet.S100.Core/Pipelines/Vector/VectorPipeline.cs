namespace EncDotNet.S100.Pipelines.Vector;

public class VectorPipeline
{
    public Task<IVectorLayer> ProcessAsync(
        IVectorSource source,
        IVectorPortrayalCatalogue catalogue,
        NavigationContext? context = null
    )
    {
        var features = source.GetFeatures(context?.Viewport is { } vp
            ? new BoundingBox(vp.MinLatitude, vp.MinLongitude, vp.MaxLatitude, vp.MaxLongitude)
            : null);

        var drawingInstructions = new List<DrawingInstruction>();

        foreach (var feature in features)
        {
            // Resolve which portrayal rule applies to this feature
            string ruleName = catalogue.ResolveRule(feature.FeatureType, feature.Attributes);

            // Determine the viewing group; skip if the user has hidden it
            // TODO: Rule execution (XSLT/Lua) produces the actual drawing instructions.
            // For now, map geometry type to a placeholder instruction.
            var instruction = feature.GeometryType switch
            {
                GeometryType.Point => DrawingInstruction.ForSymbol(
                    feature, ruleName, DisplayPlane.OverRadar, viewingGroup: 21010),
                GeometryType.Curve => DrawingInstruction.ForLine(
                    feature, ruleName, DisplayPlane.OverRadar, viewingGroup: 21010),
                GeometryType.Surface => DrawingInstruction.ForArea(
                    feature, ruleName, DisplayPlane.UnderRadar, viewingGroup: 21010),
                _ => null,
            };

            if (instruction is not null
                && catalogue.ViewingGroups.IsVisible(instruction.ViewingGroup))
            {
                drawingInstructions.Add(instruction);
            }
        }

        // Sort by display plane, then drawing priority (back-to-front)
        drawingInstructions.Sort((a, b) =>
        {
            int cmp = a.Plane.CompareTo(b.Plane);
            return cmp != 0 ? cmp : a.DrawingPriority.CompareTo(b.DrawingPriority);
        });

        IVectorLayer layer = new DefaultVectorLayer
        {
            Metadata = source.Metadata,
            Instructions = drawingInstructions,
        };

        return Task.FromResult(layer);
    }
}

/// <summary>
/// A styled vector layer ready for rendering, carrying an ordered
/// sequence of drawing instructions produced by portrayal rule evaluation.
/// </summary>
public interface IVectorLayer
{
    VectorMetadata Metadata { get; }

    /// <summary>
    /// Drawing instructions in back-to-front render order.
    /// </summary>
    IReadOnlyList<DrawingInstruction> Instructions { get; }
}

/// <summary>
/// The display plane a feature is drawn on (S-52/S-100 portrayal model).
/// </summary>
public enum DisplayPlane
{
    UnderRadar = 0,
    OverRadar = 1,
}

/// <summary>
/// A single drawing instruction produced by portrayal rule evaluation.
/// Tells the renderer what to draw and how.
/// </summary>
public sealed class DrawingInstruction
{
    public required Feature Feature { get; init; }
    public required string RuleName { get; init; }
    public required DrawingType Type { get; init; }
    public required DisplayPlane Plane { get; init; }
    public required int ViewingGroup { get; init; }
    public int DrawingPriority { get; init; }

    // Symbol placement (for points)
    public string? SymbolName { get; init; }
    public double Rotation { get; init; }

    // Line portrayal (for curves)
    public string? LineStyleName { get; init; }

    // Area portrayal (for surfaces)
    public string? AreaFillName { get; init; }
    public string? AreaPatternName { get; init; }

    public static DrawingInstruction ForSymbol(
        Feature feature, string ruleName, DisplayPlane plane, int viewingGroup) =>
        new()
        {
            Feature = feature,
            RuleName = ruleName,
            Type = DrawingType.Symbol,
            Plane = plane,
            ViewingGroup = viewingGroup,
        };

    public static DrawingInstruction ForLine(
        Feature feature, string ruleName, DisplayPlane plane, int viewingGroup) =>
        new()
        {
            Feature = feature,
            RuleName = ruleName,
            Type = DrawingType.Line,
            Plane = plane,
            ViewingGroup = viewingGroup,
        };

    public static DrawingInstruction ForArea(
        Feature feature, string ruleName, DisplayPlane plane, int viewingGroup) =>
        new()
        {
            Feature = feature,
            RuleName = ruleName,
            Type = DrawingType.Area,
            Plane = plane,
            ViewingGroup = viewingGroup,
        };
}

public enum DrawingType
{
    Symbol,
    Line,
    Area,
    Text,
}

internal sealed class DefaultVectorLayer : IVectorLayer
{
    public required VectorMetadata Metadata { get; init; }
    public required IReadOnlyList<DrawingInstruction> Instructions { get; init; }
}
