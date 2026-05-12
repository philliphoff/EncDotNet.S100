using System.Globalization;
using EncDotNet.S100.Geodesy;
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
    /// <param name="featureRef">Feature reference identifier.</param>
    /// <param name="instructionString">Semicolon-separated instruction string from Lua.</param>
    /// <param name="featureAnchor">
    /// Optional (latitude, longitude) anchor of the feature's primary point
    /// geometry. Required for tessellating <c>AugmentedRay</c> /
    /// <c>ArcByRadius</c> / <c>AugmentedPath</c> augmented line geometry.
    /// When <see langword="null"/>, augmented line geometry is silently skipped.
    /// </param>
    public static List<DrawingInstruction> Parse(
        string featureRef,
        string instructionString,
        (double Latitude, double Longitude)? featureAnchor = null)
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
        // AugmentedPoint is a simple GeographicCRS anchor override for
        // PointInstruction / TextInstruction.
        // AugmentedRay / ArcByRadius buffer segments that AugmentedPath
        // resolves into a tessellated coordinate list attached to the next
        // LineInstruction via CoordinatesOverride.
        (double Latitude, double Longitude)? augmentedAnchor = null;

        // Buffered augmented line segments awaiting AugmentedPath resolution.
        var augmentedLineSegments = new List<AugmentedSegment>();

        // Resolved augmented line coordinates ready to attach to the next
        // LineInstruction.  Set by the AugmentedPath command.
        IReadOnlyList<(double Latitude, double Longitude)>? augmentedLineCoords = null;

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
                    // If there are pending augmented segments that weren't
                    // explicitly resolved by AugmentedPath (e.g. a bare
                    // AugmentedRay → LineInstruction), resolve them now.
                    if (augmentedLineCoords is null && augmentedLineSegments.Count > 0)
                    {
                        augmentedLineCoords = ResolveAugmentedPath(
                            augmentedLineSegments, augmentedAnchor ?? featureAnchor);
                        augmentedLineSegments.Clear();
                    }

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
                        CoordinatesOverride = augmentedLineCoords,
                        ScaleMinimum = scaleMinimum,
                        ScaleMaximum = scaleMaximum,
                    });
                    dashes.Clear();
                    lineStyleRef = null;
                    // NOTE: augmentedLineCoords is NOT cleared here — the Lua
                    // pattern for sector arcs emits multiple LineInstructions
                    // (outline + colour) against the same resolved geometry.
                    // ClearGeometry resets it.
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
                    augmentedLineSegments.Clear();
                    augmentedLineCoords = null;
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
                    {
                        // AugmentedRay:GeographicCRS,bearing,endCrs,length
                        // S-100 Part 9A §11.5: a ray from the feature's anchor
                        // at a true bearing for a given length.
                        var arParts = value.Split(',');
                        if (arParts.Length >= 4 &&
                            double.TryParse(arParts[1], CultureInfo.InvariantCulture, out var arBearing) &&
                            double.TryParse(arParts[3], CultureInfo.InvariantCulture, out var arLength))
                        {
                            string crs = arParts[2]; // end-point CRS
                            augmentedLineSegments.Add(new AugmentedSegment.Ray(arBearing, arLength, crs));
                        }
                    }
                    break;

                case "ArcByRadius":
                    {
                        // ArcByRadius:xOffset,yOffset,radius,startBearing,sweep
                        // S-100 Part 9A §11.5: a circular arc around the feature
                        // anchor. Offsets are typically 0,0.
                        var abParts = value.Split(',');
                        if (abParts.Length >= 5 &&
                            double.TryParse(abParts[0], CultureInfo.InvariantCulture, out var abXOff) &&
                            double.TryParse(abParts[1], CultureInfo.InvariantCulture, out var abYOff) &&
                            double.TryParse(abParts[2], CultureInfo.InvariantCulture, out var abRadius) &&
                            double.TryParse(abParts[3], CultureInfo.InvariantCulture, out var abStart) &&
                            double.TryParse(abParts[4], CultureInfo.InvariantCulture, out var abSweep))
                        {
                            augmentedLineSegments.Add(
                                new AugmentedSegment.Arc(abXOff, abYOff, abRadius, abStart, abSweep));
                        }
                    }
                    break;

                case "AugmentedPath":
                    {
                        // AugmentedPath:crs1,crs2,...
                        // Resolves buffered AugmentedRay/ArcByRadius segments
                        // into a tessellated coordinate list using the declared CRS
                        // sequence.  The feature's anchor point is the origin.
                        if (augmentedLineSegments.Count > 0)
                        {
                            augmentedLineCoords = ResolveAugmentedPath(
                                augmentedLineSegments, augmentedAnchor ?? featureAnchor);
                            augmentedLineSegments.Clear();
                        }
                    }
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

    /// <summary>
    /// Nominal S-100 display pixel size in millimetres (S-100 Part 9 §11.3).
    /// </summary>
    private const double S100PixelSizeMm = 0.32;

    /// <summary>
    /// Reference scale denominator used to convert LocalCRS mm radii to
    /// approximate geographic distances for tessellation. 1:25000 is a
    /// typical ECDIS display scale for harbour approaches where sector
    /// lights are most relevant.
    /// </summary>
    private const double ReferenceScaleDenominator = 25_000.0;

    /// <summary>
    /// Resolves buffered augmented line segments into a tessellated coordinate
    /// list. The feature's anchor point (from <paramref name="anchor"/>) is
    /// the origin for all geometry.
    /// </summary>
    private static IReadOnlyList<(double Latitude, double Longitude)>? ResolveAugmentedPath(
        List<AugmentedSegment> segments,
        (double Latitude, double Longitude)? anchor)
    {
        if (segments.Count == 0)
            return null;

        var allPoints = new List<(double Latitude, double Longitude)>();

        foreach (var segment in segments)
        {
            IReadOnlyList<(double Latitude, double Longitude)> tessellated = segment switch
            {
                AugmentedSegment.Ray ray => TessellateRaySegment(ray, anchor),
                AugmentedSegment.Arc arc => TessellateArcSegment(arc, anchor),
                _ => [],
            };

            // Append points, skipping the first if it duplicates the previous
            // end-point (segments share junction vertices).
            for (int i = 0; i < tessellated.Count; i++)
            {
                if (i == 0 && allPoints.Count > 0 &&
                    Math.Abs(allPoints[^1].Latitude - tessellated[i].Latitude) < 1e-10 &&
                    Math.Abs(allPoints[^1].Longitude - tessellated[i].Longitude) < 1e-10)
                {
                    continue;
                }

                allPoints.Add(tessellated[i]);
            }
        }

        return allPoints.Count >= 2 ? allPoints : null;
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> TessellateRaySegment(
        AugmentedSegment.Ray ray,
        (double Latitude, double Longitude)? anchor)
    {
        if (anchor is not { } origin)
            return [];

        double distanceMetres = ray.LengthMetres;
        if (string.Equals(ray.EndCrs, "LocalCRS", StringComparison.OrdinalIgnoreCase))
        {
            // LocalCRS length is in mm on the nominal display surface.
            // Convert to approximate metres using the reference scale.
            distanceMetres = ray.LengthMetres * S100PixelSizeMm / 1000.0 * ReferenceScaleDenominator;
        }

        if (distanceMetres <= 0)
            return [];

        return GeodesicHelper.TessellateRay(
            origin.Latitude, origin.Longitude, ray.BearingDeg, distanceMetres);
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> TessellateArcSegment(
        AugmentedSegment.Arc arc,
        (double Latitude, double Longitude)? anchor)
    {
        if (anchor is not { } origin)
            return [];

        // The arc radius is in LocalCRS units (mm on nominal display).
        // Convert to approximate metres.
        double radiusMetres = arc.Radius * S100PixelSizeMm / 1000.0 * ReferenceScaleDenominator;

        if (radiusMetres <= 0)
            return [];

        return GeodesicHelper.TessellateArc(
            origin.Latitude, origin.Longitude, radiusMetres,
            arc.StartBearingDeg, arc.SweepDeg);
    }
}

/// <summary>
/// Discriminated union of augmented line geometry segments buffered by
/// <see cref="DrawingInstructionParser"/> before <c>AugmentedPath</c>
/// resolution.
/// </summary>
internal abstract record AugmentedSegment
{
    private AugmentedSegment() { }

    /// <summary>
    /// A ray from the feature anchor at a true bearing for a given length
    /// (S-100 Part 9A §11.5 <c>AugmentedRay</c>).
    /// </summary>
    internal sealed record Ray(double BearingDeg, double LengthMetres, string EndCrs) : AugmentedSegment;

    /// <summary>
    /// A circular arc around the feature anchor (S-100 Part 9A §11.5
    /// <c>ArcByRadius</c>).
    /// </summary>
    internal sealed record Arc(
        double XOffset, double YOffset, double Radius,
        double StartBearingDeg, double SweepDeg) : AugmentedSegment;
}
