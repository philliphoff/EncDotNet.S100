using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Parses the semicolon-separated key:value drawing instruction strings
/// emitted by the S-101 Lua portrayal pipeline into the unified
/// <see cref="DrawingInstruction"/> hierarchy.
/// </summary>
/// <remarks>
/// The instruction string is a stateful sequence: property-setting commands
/// (ViewingGroup, DrawingPriority, DisplayPlane, font/style settings) establish
/// state that applies to subsequent rendering commands (PointInstruction,
/// LineInstruction, AreaFillReference, ColorFill, TextInstruction, etc.).
/// Geometry is NOT encoded in the string — the renderer resolves it from the
/// feature's spatial associations using the instruction's
/// <see cref="DrawingInstruction.FeatureReference"/>.
/// </remarks>
public static class DrawingInstructionParser
{
    /// <summary>
    /// Parses a single emitted instruction string into zero or more
    /// <see cref="DrawingInstruction"/> instances.
    /// </summary>
    public static List<DrawingInstruction> Parse(string featureRef, string instructionString)
    {
        if (string.IsNullOrEmpty(instructionString))
            return [];

        var results = new List<DrawingInstruction>();

        // Current state — accumulated as we scan through commands
        int viewingGroup = 0;
        int drawingPriority = 0;
        DisplayPlane displayPlane = DisplayPlane.UnderRadar;
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
        double? fontTransparency = null;
        string? fontBackgroundColor = null;
        double? fontBackgroundTransparency = null;
        double? linePlacementPosition = null;
        TextHorizontalAlignment textHAlign = TextHorizontalAlignment.Center;
        TextVerticalAlignment textVAlign = TextVerticalAlignment.Center;

        // Augmented-geometry state.  S-100 Part 9 §11.5 lets a Lua rule
        // override the per-instruction anchor with AugmentedPoint, or build
        // a synthetic line geometry from AugmentedRay/ArcByRadius/AugmentedPath.
        // We currently support only the GeographicCRS anchor override; a
        // following PointInstruction or TextInstruction is anchored at this
        // explicit lat/lon instead of the feature's first vertex.
        (double Latitude, double Longitude)? augmentedAnchor = null;

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
                    if (Enum.TryParse<DisplayPlane>(value, ignoreCase: true, out var parsedPlane))
                        displayPlane = parsedPlane;
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
                    {
                        // FontColor:token[,transparency]
                        var fcParts = value.Split(',');
                        fontColor = fcParts[0];
                        if (fcParts.Length > 1 &&
                            double.TryParse(fcParts[1], CultureInfo.InvariantCulture, out var fct))
                        {
                            fontTransparency = fct;
                        }
                        else
                        {
                            fontTransparency = null;
                        }
                    }
                    break;

                // ── Rendering commands (emit a parsed instruction) ──
                case "PointInstruction":
                    results.Add(new PointInstruction
                    {
                        FeatureReference = featureRef,
                        SymbolReference = value,
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        Plane = displayPlane,
                        Rotation = rotation,
                        SymbolScale = scaleFactor,
                        LocalOffsetX = localOffsetX,
                        LocalOffsetY = localOffsetY,
                        LinePlacementPosition = linePlacementPosition,
                        CoordinateOverride = augmentedAnchor,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    // Reset per-instruction state
                    rotation = null;
                    scaleFactor = 1.0;
                    localOffsetX = 0;
                    localOffsetY = 0;
                    augmentedAnchor = null;
                    break;

                case "LineInstruction":
                case "LineInstructionUnsuppressed":
                    results.Add(new LineInstruction
                    {
                        FeatureReference = featureRef,
                        LineStyleReference = string.IsNullOrEmpty(value) ? lineStyleRef : value,
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        Plane = displayPlane,
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
                    results.Add(new AreaInstruction
                    {
                        FeatureReference = featureRef,
                        FillColor = cfParts[0],
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        Plane = displayPlane,
                        Transparency = cfParts.Length > 1 &&
                            double.TryParse(cfParts[1], CultureInfo.InvariantCulture, out var tr) ? tr : null,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    break;

                case "AreaFillReference":
                    results.Add(new AreaInstruction
                    {
                        FeatureReference = featureRef,
                        AreaFillReference = value,
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        Plane = displayPlane,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    break;

                case "TextInstruction":
                    results.Add(new TextInstruction
                    {
                        FeatureReference = featureRef,
                        Text = DefDecode(value),
                        ViewingGroup = viewingGroup,
                        DrawingPriority = drawingPriority,
                        Plane = displayPlane,
                        FontSize = fontSize,
                        FontColor = fontColor,
                        FontTransparency = fontTransparency,
                        BackgroundColor = fontBackgroundColor,
                        BackgroundTransparency = fontBackgroundTransparency,
                        LinePlacementPosition = linePlacementPosition,
                        HorizontalAlignment = textHAlign,
                        VerticalAlignment = textVAlign,
                        OffsetXmm = localOffsetX != 0 ? localOffsetX : null,
                        OffsetYmm = localOffsetY != 0 ? localOffsetY : null,
                        CoordinateOverride = augmentedAnchor,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    // Reset per-instruction text state so the next label
                    // doesn't inherit alignment / offset from this one.
                    textHAlign = TextHorizontalAlignment.Center;
                    textVAlign = TextVerticalAlignment.Center;
                    localOffsetX = 0;
                    localOffsetY = 0;
                    augmentedAnchor = null;
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
                case "AreaPlacement":
                case "AreaCRS":
                case "Date":
                case "Time":
                case "DateTime":
                case "TimeValid":
                case "ClearTime":
                    // Recognised but not yet handled — skip silently
                    break;

                case "ClearGeometry":
                    augmentedAnchor = null;
                    break;

                case "AugmentedPoint":
                    {
                        // AugmentedPoint:CRS,x,y
                        // GeographicCRS: x = longitude, y = latitude
                        // (S-101 SOUNDG03 emits feature.X (longitude) then feature.Y (latitude)).
                        // LocalCRS / PortrayalCRS variants are not yet handled.
                        var apParts = value.Split(',');
                        if (apParts.Length >= 3 &&
                            string.Equals(apParts[0], "GeographicCRS", StringComparison.OrdinalIgnoreCase) &&
                            double.TryParse(apParts[1], CultureInfo.InvariantCulture, out var apLon) &&
                            double.TryParse(apParts[2], CultureInfo.InvariantCulture, out var apLat))
                        {
                            augmentedAnchor = (apLat, apLon);
                        }
                    }
                    break;

                case "AugmentedRay":
                case "AugmentedPath":
                case "ArcByRadius":
                    // Buffered synthetic line geometry: not yet supported.
                    // Affects sector lights and all-around-light circles.
                    WarnAugmentedLineGeometryOnce();
                    break;

                case "TextAlignHorizontal":
                    if (Enum.TryParse<TextHorizontalAlignment>(value, ignoreCase: true, out var parsedHAlign))
                        textHAlign = parsedHAlign;
                    break;

                case "TextAlignVertical":
                    if (Enum.TryParse<TextVerticalAlignment>(value, ignoreCase: true, out var parsedVAlign))
                        textVAlign = parsedVAlign;
                    break;

                case "FontBackgroundColor":
                    {
                        // FontBackgroundColor:[token[,transparency]]
                        // An empty value clears any inherited background.
                        if (string.IsNullOrEmpty(value))
                        {
                            fontBackgroundColor = null;
                            fontBackgroundTransparency = null;
                        }
                        else
                        {
                            var fbParts = value.Split(',');
                            fontBackgroundColor = string.IsNullOrEmpty(fbParts[0]) ? null : fbParts[0];
                            fontBackgroundTransparency = fbParts.Length > 1 &&
                                double.TryParse(fbParts[1], CultureInfo.InvariantCulture, out var fbt)
                                ? fbt
                                : null;
                        }
                    }
                    break;

                case "FontWeight":
                case "FontSlant":
                case "FontProportion":
                case "FontSerifs":
                case "FontUnderline":
                case "FontStrikethrough":
                case "FontUpperline":
                case "FontReference":
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

    private static int s_augmentedLineGeometryWarningCount;

    private static void WarnAugmentedLineGeometryOnce()
    {
        if (System.Threading.Interlocked.Increment(ref s_augmentedLineGeometryWarningCount) == 1)
        {
            Console.Error.WriteLine(
                "[S101] Augmented line geometry (AugmentedRay / ArcByRadius / AugmentedPath) " +
                "is not yet rendered — sector lights and all-around-light circles will be incomplete.");
        }
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
