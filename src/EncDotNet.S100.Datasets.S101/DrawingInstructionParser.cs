using System.Globalization;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Parses the semicolon-separated key:value drawing instruction strings
/// emitted by the S-101 Lua portrayal pipeline into structured
/// <see cref="ParsedDrawingInstruction"/> objects.
/// </summary>
/// <remarks>
/// The instruction string is a stateful sequence: property-setting commands
/// (ViewingGroup, DrawingPriority, DisplayPlane, font/style settings) establish
/// state that applies to subsequent rendering commands (PointInstruction,
/// LineInstruction, AreaFillReference, ColorFill, TextInstruction, etc.).
/// Geometry is NOT encoded in the string — the host resolves it from the
/// feature's spatial associations.
/// </remarks>
public static class DrawingInstructionParser
{
    /// <summary>
    /// Parses a single emitted instruction string into zero or more
    /// <see cref="ParsedDrawingInstruction"/> instances.
    /// </summary>
    public static List<ParsedDrawingInstruction> Parse(string featureRef, string instructionString)
    {
        if (string.IsNullOrEmpty(instructionString))
            return [];

        var results = new List<ParsedDrawingInstruction>();

        // Current state — accumulated as we scan through commands
        int viewingGroup = 0;
        int drawingPriority = 0;
        string displayPlane = "UnderRadar";
        double? scaleMinimum = null;
        double? scaleMaximum = null;

        // Point/line style state
        string? lineStyleRef = null;
        double lineWidth = 0.32;
        string lineColor = "CSTLN";
        var dashes = new List<(double Offset, double Length)>();
        double? rotation = null;
        string? rotationCrs = null;
        double scaleFactor = 1.0;
        double localOffsetX = 0;
        double localOffsetY = 0;

        // Text style state
        double fontSize = 10;
        string fontColor = "CHBLK";
        double? linePlacementPosition = null;

        var segments = instructionString.Split(';');

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var colonIdx = segment.IndexOf(':');
            string key, value;
            if (colonIdx >= 0)
            {
                key = segment[..colonIdx];
                value = segment[(colonIdx + 1)..];
            }
            else
            {
                key = segment;
                value = "";
            }

            switch (key)
            {
                // ── State-setting commands ──
                case "ViewingGroup":
                    // May be "vg1,vg2" — take the first
                    var vgParts = value.Split(',');
                    if (vgParts.Length > 0 && int.TryParse(vgParts[0], CultureInfo.InvariantCulture, out var vg))
                        viewingGroup = vg;
                    break;

                case "DrawingPriority":
                    if (int.TryParse(value, CultureInfo.InvariantCulture, out var dp))
                        drawingPriority = dp;
                    break;

                case "DisplayPlane":
                    displayPlane = value;
                    break;

                case "ScaleMinimum":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var smin))
                        scaleMinimum = smin;
                    break;

                case "ScaleMaximum":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var smax))
                        scaleMaximum = smax;
                    break;

                case "Rotation":
                    var rotParts = value.Split(',');
                    if (rotParts.Length >= 2)
                    {
                        rotationCrs = rotParts[0];
                        if (double.TryParse(rotParts[1], CultureInfo.InvariantCulture, out var angle))
                            rotation = angle;
                    }
                    break;

                case "ScaleFactor":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var sf))
                        scaleFactor = sf;
                    break;

                case "LocalOffset":
                    var loParts = value.Split(',');
                    if (loParts.Length >= 2)
                    {
                        double.TryParse(loParts[0], CultureInfo.InvariantCulture, out localOffsetX);
                        double.TryParse(loParts[1], CultureInfo.InvariantCulture, out localOffsetY);
                    }
                    break;

                // ── Line/dash style state ──
                case "Dash":
                    var dashParts = value.Split(',');
                    if (dashParts.Length >= 2 &&
                        double.TryParse(dashParts[0], CultureInfo.InvariantCulture, out var dOff) &&
                        double.TryParse(dashParts[1], CultureInfo.InvariantCulture, out var dLen))
                    {
                        dashes.Add((dOff, dLen));
                    }
                    break;

                case "LineStyle":
                    var lsParts = value.Split(',');
                    if (lsParts.Length >= 4)
                    {
                        lineStyleRef = lsParts[0];
                        if (double.TryParse(lsParts[2], CultureInfo.InvariantCulture, out var lw))
                            lineWidth = lw;
                        lineColor = lsParts[3];
                    }
                    else if (lsParts.Length >= 1)
                    {
                        lineStyleRef = lsParts[0];
                    }
                    break;

                // ── Text style state ──
                case "FontSize":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var fs))
                        fontSize = fs;
                    break;

                case "FontColor":
                    fontColor = value;
                    break;

                // ── Rendering commands (emit a parsed instruction) ──
                case "PointInstruction":
                    results.Add(new ParsedDrawingInstruction
                    {
                        FeatureRef = featureRef,
                        Type = InstructionType.Point,
                        SymbolRef = value,
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        DisplayPlane = displayPlane,
                        Rotation = rotation,
                        ScaleFactor = scaleFactor,
                        LocalOffsetX = localOffsetX,
                        LocalOffsetY = localOffsetY,
                        LinePlacementPosition = linePlacementPosition,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    // Reset per-instruction state
                    rotation = null;
                    scaleFactor = 1.0;
                    localOffsetX = 0;
                    localOffsetY = 0;
                    break;

                case "LineInstruction":
                case "LineInstructionUnsuppressed":
                    results.Add(new ParsedDrawingInstruction
                    {
                        FeatureRef = featureRef,
                        Type = InstructionType.Line,
                        SymbolRef = value,
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        DisplayPlane = displayPlane,
                        LineWidth = lineWidth,
                        LineColor = lineColor,
                        Dashes = dashes.Count > 0 ? new List<(double, double)>(dashes) : null,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    dashes.Clear();
                    lineStyleRef = null;
                    break;

                case "ColorFill":
                    var cfParts = value.Split(',');
                    results.Add(new ParsedDrawingInstruction
                    {
                        FeatureRef = featureRef,
                        Type = InstructionType.AreaFill,
                        SymbolRef = cfParts[0],
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        DisplayPlane = displayPlane,
                        IsColorFill = true,
                        Transparency = cfParts.Length > 1 &&
                            double.TryParse(cfParts[1], CultureInfo.InvariantCulture, out var tr) ? tr : null,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    break;

                case "AreaFillReference":
                    results.Add(new ParsedDrawingInstruction
                    {
                        FeatureRef = featureRef,
                        Type = InstructionType.AreaFill,
                        SymbolRef = value,
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        DisplayPlane = displayPlane,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    break;

                case "TextInstruction":
                    results.Add(new ParsedDrawingInstruction
                    {
                        FeatureRef = featureRef,
                        Type = InstructionType.Text,
                        Text = DefDecode(value),
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        DisplayPlane = displayPlane,
                        FontSize = fontSize,
                        FontColor = fontColor,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    break;

                case "NullInstruction":
                    // Intentional no-op
                    break;

                case "LinePlacement":
                    // LinePlacement:Relative,0.5[,,true]
                    var lpParts = value.Split(',');
                    if (lpParts.Length >= 2 &&
                        double.TryParse(lpParts[1], CultureInfo.InvariantCulture, out var lpPos))
                    {
                        linePlacementPosition = lpPos;
                    }
                    break;

                case "AlertReference":
                case "Hover":
                case "SpatialReference":
                case "ClearGeometry":
                case "AugmentedPoint":
                case "AugmentedRay":
                case "AugmentedPath":
                case "ArcByRadius":
                case "AreaPlacement":
                case "AreaCRS":
                case "Date":
                case "Time":
                case "DateTime":
                case "TimeValid":
                case "ClearTime":
                case "FontWeight":
                case "FontSlant":
                case "FontProportion":
                case "FontSerifs":
                case "FontUnderline":
                case "FontStrikethrough":
                case "FontUpperline":
                case "FontReference":
                case "FontBackgroundColor":
                case "TextAlignHorizontal":
                case "TextAlignVertical":
                case "TextVerticalOffset":
                    // Recognised but not yet handled — skip silently
                    break;

                default:
                    // Unknown command — ignore
                    break;
            }
        }

        return results;
    }

    /// <summary>
    /// Decodes DEF-encoded text: &amp;a → &amp;, &amp;s → ;, &amp;c → :, &amp;m → ,
    /// </summary>
    private static string DefDecode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded) || !encoded.Contains('&'))
            return encoded;

        return encoded
            .Replace("&m", ",")
            .Replace("&c", ":")
            .Replace("&s", ";")
            .Replace("&a", "&");
    }
}

/// <summary>
/// The type of rendering instruction.
/// </summary>
public enum InstructionType
{
    Point,
    Line,
    AreaFill,
    Text,
}

/// <summary>
/// A parsed drawing instruction extracted from the Lua portrayal emit string.
/// Contains the instruction type, symbol reference, and accumulated display properties.
/// </summary>
public sealed class ParsedDrawingInstruction
{
    public required string FeatureRef { get; init; }
    public required InstructionType Type { get; init; }
    public required int ViewingGroup { get; init; }
    public required int DrawingPriority { get; init; }
    public required string DisplayPlane { get; init; }

    // Symbol/style reference — meaning depends on Type
    public string? SymbolRef { get; init; }

    // Text content (for TextInstruction)
    public string? Text { get; init; }

    // Point properties
    public double? Rotation { get; init; }
    public double ScaleFactor { get; init; } = 1.0;
    public double LocalOffsetX { get; init; }
    public double LocalOffsetY { get; init; }

    // Line properties
    public double LineWidth { get; init; }
    public string? LineColor { get; init; }
    public List<(double Offset, double Length)>? Dashes { get; init; }

    // Area fill properties
    public bool IsColorFill { get; init; }
    public double? Transparency { get; init; }

    // Text properties
    public double FontSize { get; init; } = 10;
    public string FontColor { get; init; } = "CHBLK";

    // Line placement: when set, the symbol/text should be placed at this
    // relative position (0.0–1.0) along the feature's curve geometry.
    public double? LinePlacementPosition { get; init; }

    // Scale visibility
    public double? ScaleMinimum { get; init; }
    public double? ScaleMaximum { get; init; }
}
