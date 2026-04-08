namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Base class for all drawing instructions produced by portrayal rule evaluation.
/// Subclasses carry the resolved resources needed by the renderer.
/// </summary>
public abstract class DrawingInstruction
{
    /// <summary>ID of the feature this instruction was generated for.</summary>
    public required string FeatureId { get; init; }

    /// <summary>Whether the feature renders under or over the radar overlay.</summary>
    public required DisplayPlane Plane { get; init; }

    /// <summary>Viewing group controlling visibility of this instruction.</summary>
    public required int ViewingGroup { get; init; }

    /// <summary>
    /// Drawing priority within the display plane (ascending, back-to-front).
    /// Within the same priority, the S-100 Part 9 type order applies:
    /// areas → lines → points → text.
    /// </summary>
    public int DrawingPriority { get; init; }

    /// <summary>
    /// Returns a numeric type order for S-100 Part 9 intra-priority sorting:
    /// areas (0) → lines (1) → points (2) → text (3).
    /// </summary>
    internal abstract int TypeSortOrder { get; }
}

/// <summary>
/// A point symbol placement instruction.
/// </summary>
public sealed class PointInstruction : DrawingInstruction
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required SvgSymbol Symbol { get; init; }
    public double Rotation { get; init; }
    public double Scale { get; init; } = 1.0;

    internal override int TypeSortOrder => 2;
}

/// <summary>
/// A line rendering instruction.
/// </summary>
public sealed class LineInstruction : DrawingInstruction
{
    public required IReadOnlyList<(double Latitude, double Longitude)> Geometry { get; init; }
    public required LineStyle LineStyle { get; init; }

    internal override int TypeSortOrder => 1;
}

/// <summary>
/// An area fill instruction (with optional outline).
/// </summary>
public sealed class AreaInstruction : DrawingInstruction
{
    /// <summary>
    /// Outer ring followed by any inner rings (holes).
    /// Each ring is an ordered list of coordinates.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> Rings { get; init; }
    public required AreaFill AreaFill { get; init; }
    public LineStyle? OutlineStyle { get; init; }

    internal override int TypeSortOrder => 0;
}

/// <summary>
/// A text label rendering instruction.
/// </summary>
public sealed class TextInstruction : DrawingInstruction
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required string Text { get; init; }
    public string? FontRef { get; init; }
    public double FontSize { get; init; } = 10.0;
    public string Color { get; init; } = "#000000";
    public double Rotation { get; init; }

    internal override int TypeSortOrder => 3;
}
