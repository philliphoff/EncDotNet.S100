using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// A resolved point-symbol asset: the processed (class-resolved) SVG content
/// plus the S-100 Part 9 §11.5 pivot offset expressed as a fraction of symbol
/// size in screen-space orientation (+Y = down).
/// </summary>
/// <param name="ProcessedSvg">Processed SVG content (no <c>svg-content://</c> prefix).</param>
/// <param name="PivotRelativeX">Pivot-to-centre offset as a fraction of width (+X = pivot left of centre).</param>
/// <param name="PivotRelativeY">Pivot-to-centre offset as a fraction of height (+Y = pivot above centre, screen +Y down).</param>
public readonly record struct SymbolAsset(
    string ProcessedSvg,
    double PivotRelativeX,
    double PivotRelativeY);

/// <summary>
/// Lowers a backend-agnostic S-100 Part 9 display list (a list of
/// <see cref="DrawingInstruction"/>s plus an <see cref="IFeatureGeometryProvider"/>)
/// into a resolved <see cref="VectorScene"/>: instructions are sorted into
/// Part 9 draw order, colours/symbols/line-styles are resolved, sizes are
/// converted from millimetres to display pixels (<c>1 px = 0.32 mm</c>), and
/// geometry is projected to EPSG:3857. The result is consumed by both the
/// headless <c>SkiaDisplayListRenderer</c> and the Mapsui renderer so
/// the S-100 portrayal-correctness logic lives in exactly one place.
/// </summary>
/// <remarks>
/// <b>Scope.</b> Both solid-colour and tiled-symbol pattern area fills are
/// lowered into the IR. Pattern fills are emitted as
/// <see cref="PatternAreaPaintOp"/> only when a <see cref="PatternResolver"/>
/// is supplied (the headless Skia path); the Mapsui renderer leaves the
/// resolver unset and continues to drive its own pattern collection /
/// priority-clip / insert phase, so its output is unchanged.
/// </remarks>
public sealed class VectorSceneBuilder
{
    /// <summary>
    /// Size, in millimetres, of one S-100 portrayal pixel on the nominal display
    /// surface (S-100 Part 9 §3.10.4 — 1 px = 0.32 mm). Used to convert
    /// spec-defined millimetre sizes to display pixels.
    /// </summary>
    public const double S100PixelSizeMm = 0.32;

    /// <summary>Resolves an S-100 colour token to an <see cref="RgbaColor"/>. Required.</summary>
    public required Func<string?, RgbaColor> ResolveColor { get; init; }

    /// <summary>
    /// Resolves a symbol name to a processed-SVG asset, or null when the symbol
    /// cannot be resolved (the op then carries a fallback dot). Optional.
    /// </summary>
    public Func<string, SymbolAsset?>? SymbolResolver { get; init; }

    /// <summary>Resolves a line-style name to its catalogue definition. Optional.</summary>
    public Func<string, LineStyle?>? LineStyleProvider { get; init; }

    /// <summary>
    /// Resolves an area-fill name to a pre-rasterised tiled pattern (PNG
    /// bytes). When set, <c>AreaInstruction</c>s with an
    /// <c>AreaFillReference</c> are lowered to a
    /// <see cref="PatternAreaPaintOp"/>; when unset (the Mapsui default),
    /// pattern instructions are skipped so the Mapsui renderer's existing
    /// pattern phase remains authoritative. Returning <see langword="null"/>
    /// for a given name silently drops just that instruction.
    /// </summary>
    public Func<string, byte[]?>? PatternResolver { get; init; }

    /// <summary>Global scale factor applied to all point symbols (default 1.0).</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Global scale factor applied to all text labels (default 1.0).</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>
    /// Optional dataset-wide out-of-scale-band cap, expressed as an S-100
    /// Part 9 §11.1 scale denominator (the cell's most-permissive
    /// <c>DataCoverage.minimumDisplayScale</c>; S-101 FC §3.1.1). When set,
    /// every op's effective <see cref="PaintOp.ScaleMinimum"/> is clamped to
    /// this denominator, so features lacking their own SCAMIN — and those
    /// whose SCAMIN is more permissive than the cell's — are hidden once the
    /// display is zoomed out past the cell's compilation scale. This mirrors
    /// the cap the Mapsui feature path applies via per-feature
    /// <c>MaxVisible</c> (<c>MapsuiDatasetRenderer.ApplyOutOfScaleBandCap</c>),
    /// keeping the <see cref="VectorScene"/> IR consumed by the TiledScene
    /// render subsystem in agreement with the Mapsui subsystem.
    /// </summary>
    public double? OutOfBandMinDisplayScale { get; init; }

    /// <summary>
    /// Clamps a per-op <c>ScaleMinimum</c> (largest allowed denominator) to the
    /// dataset-wide <see cref="OutOfBandMinDisplayScale"/> cap when one is set.
    /// An op with no SCAMIN inherits the cap; an op with a more-permissive
    /// SCAMIN is tightened to it. Returns the value unchanged when no cap is set.
    /// </summary>
    private double? CapScaleMinimum(double? scaleMinimum)
    {
        if (OutOfBandMinDisplayScale is not double cap)
            return scaleMinimum;
        return scaleMinimum.HasValue ? Math.Min(scaleMinimum.Value, cap) : cap;
    }

    /// <summary>
    /// Convenience helper that produces a <see cref="SymbolAsset"/> from raw SVG
    /// content by recovering the pivot (<see cref="SvgPivotMetrics.TryParse"/>)
    /// and processing CSS classes (<see cref="SvgProcessor.Process"/>). Returns
    /// null when the content is null/empty or processing fails.
    /// </summary>
    public static SymbolAsset? ResolveSymbolAsset(string? svgContent, ColorPalette? palette)
    {
        if (string.IsNullOrEmpty(svgContent))
            return null;
        try
        {
            var pivot = SvgPivotMetrics.TryParse(svgContent);
            var processed = SvgProcessor.Process(svgContent, palette);
            return new SymbolAsset(
                processed,
                pivot?.RelativeOffset.X ?? 0.0,
                pivot?.RelativeOffset.Y ?? 0.0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Builds a resolved scene from the supplied display list and geometry.</summary>
    public VectorScene Build(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        var sorted = instructions
            .OrderBy(i => i.Plane == EncDotNet.S100.Pipelines.Vector.DisplayPlane.OverRadar ? 1 : 0)
            .ThenBy(i => i switch
            {
                // Pattern areas draw after solid areas but before lines/points/text,
                // mirroring the Mapsui renderer's "insert pattern fills after the
                // last solid colour fill" ordering.
                AreaInstruction { AreaFillReference: not null } => 1,
                AreaInstruction => 0,
                LineInstruction => 2,
                PointInstruction => 3,
                TextInstruction => 4,
                _ => 5,
            })
            .ThenBy(i => i.DrawingPriority)
            .ToList();

        var ops = new List<PaintOp>(sorted.Count);

        foreach (var instruction in sorted)
        {
            var geom = geometryProvider.GetGeometry(instruction.FeatureReference);

            bool hasAugmentedLine = instruction is LineInstruction { CoordinatesOverride: not null };
            if (!hasAugmentedLine && (geom is null || geom.Coordinates.Count == 0))
                continue;

            PaintOp? op = instruction switch
            {
                // Pattern fills are lowered only when a resolver is supplied (the
                // headless path); otherwise they are deferred to the Mapsui
                // pattern phase, which collects/clips them outside this IR.
                AreaInstruction { AreaFillReference: { } patternRef } areaPattern when geom is not null
                    => BuildPatternArea(areaPattern, patternRef, geom),
                AreaInstruction area when geom is not null => BuildArea(area, geom),
                LineInstruction line => BuildLine(line, geom),
                PointInstruction point when geom is not null => BuildPoint(point, geom),
                TextInstruction text when geom is not null => BuildText(text, geom),
                _ => null,
            };

            if (op is not null)
                ops.Add(op);
        }

        return new VectorScene(ops);
    }

    private PatternAreaPaintOp? BuildPatternArea(
        AreaInstruction instruction, string patternRef, FeatureGeometry geometry)
    {
        if (PatternResolver is null)
            return null;

        if (geometry.Coordinates.Count < 3)
            return null;

        var tile = PatternResolver(patternRef);
        if (tile is null || tile.Length == 0)
            return null;

        var holes = new List<IReadOnlyList<(double, double)>>(geometry.InteriorRings.Count);
        foreach (var ring in geometry.InteriorRings)
        {
            if (ring.Count >= 3)
                holes.Add(Project(ring));
        }

        return new PatternAreaPaintOp
        {
            FeatureReference = instruction.FeatureReference,
            ScaleMinimum = CapScaleMinimum(instruction.ScaleMinimum),
            ScaleMaximum = instruction.ScaleMaximum,
            PatternReference = patternRef,
            WorldShell = Project(geometry.Coordinates),
            WorldHoles = holes,
            TilePng = tile,
        };
    }

    private AreaPaintOp? BuildArea(AreaInstruction instruction, FeatureGeometry geometry)
    {
        if (instruction.FillColor is null)
            return null;

        if (geometry.Coordinates.Count < 3)
            return null;

        var fill = ResolveColor(instruction.FillColor);
        if (instruction.Transparency.HasValue)
        {
            byte alpha = (byte)(255 * (1.0 - instruction.Transparency.Value));
            fill = new RgbaColor(fill.R, fill.G, fill.B, alpha);
        }

        var holes = new List<IReadOnlyList<(double, double)>>(geometry.InteriorRings.Count);
        foreach (var ring in geometry.InteriorRings)
        {
            if (ring.Count >= 3)
                holes.Add(Project(ring));
        }

        return new AreaPaintOp
        {
            FeatureReference = instruction.FeatureReference,
            ScaleMinimum = CapScaleMinimum(instruction.ScaleMinimum),
            ScaleMaximum = instruction.ScaleMaximum,
            WorldShell = Project(geometry.Coordinates),
            WorldHoles = holes,
            Fill = fill,
            // Matches the legacy renderer's faint area outline.
            OutlineColor = new RgbaColor(0, 0, 0, 40),
            OutlineWidthPx = 0.5,
        };
    }

    private LinePaintOp? BuildLine(LineInstruction instruction, FeatureGeometry? geometry)
    {
        var coords = instruction.CoordinatesOverride ?? geometry?.Coordinates;
        if (coords is null || coords.Count < 2)
            return null;

        string? colorToken = instruction.LineColor;
        double width = instruction.LineWidth;
        bool dashed = instruction.Dashes is { Count: > 0 };

        if (colorToken is null && instruction.LineStyleReference is not null && LineStyleProvider is not null)
        {
            var externalStyle = LineStyleProvider(instruction.LineStyleReference);
            if (externalStyle is not null)
            {
                colorToken = externalStyle.Color;
                if (externalStyle.Width > 0)
                    width = externalStyle.Width;
                if (externalStyle.DashPattern is { Length: > 0 })
                    dashed = true;
            }
        }

        var widthPx = width > 0 ? (width / S100PixelSizeMm) : 0.0;
        widthPx = Math.Max(widthPx, 1.0);

        IReadOnlyList<float>? dashArray = null;
        bool defaultDash = false;
        if (dashed && instruction.Dashes is { Count: > 0 })
        {
            var onMm = instruction.DashOnLength > 0
                ? instruction.DashOnLength
                : instruction.Dashes[0].Length;
            var gapMm = instruction.Dashes[0].Length;
            var onPx = (float)(onMm / S100PixelSizeMm);
            var gapPx = (float)(gapMm / S100PixelSizeMm);
            dashArray = [Math.Max(onPx, 1f), Math.Max(gapPx, 1f)];
        }
        else if (dashed)
        {
            defaultDash = true;
        }

        return new LinePaintOp
        {
            FeatureReference = instruction.FeatureReference,
            ScaleMinimum = CapScaleMinimum(instruction.ScaleMinimum),
            ScaleMaximum = instruction.ScaleMaximum,
            World = Project(coords),
            Color = ResolveColor(colorToken),
            WidthPx = widthPx,
            DashArrayPx = dashArray,
            DefaultDash = defaultDash,
        };
    }

    private PointPaintOp BuildPoint(PointInstruction instruction, FeatureGeometry geometry)
    {
        double lat, lon;
        if (instruction.CoordinateOverride is { } anchor)
        {
            (lat, lon) = (anchor.Latitude, anchor.Longitude);
        }
        else
        {
            (lat, lon) = geometry.Coordinates[0];
        }

        var symOffsetXpx = instruction.LocalOffsetX / S100PixelSizeMm;
        var symOffsetYpx = instruction.LocalOffsetY / S100PixelSizeMm;

        SymbolAsset? asset = null;
        if (!string.IsNullOrEmpty(instruction.SymbolReference) && SymbolResolver is not null)
            asset = SymbolResolver(instruction.SymbolReference);

        ResolvedSymbol? symbol = asset is { } a
            ? new ResolvedSymbol(
                a.ProcessedSvg,
                0.6 * instruction.SymbolScale * SymbolScale,
                a.PivotRelativeX,
                a.PivotRelativeY)
            : null;

        return new PointPaintOp
        {
            FeatureReference = instruction.FeatureReference,
            ScaleMinimum = CapScaleMinimum(instruction.ScaleMinimum),
            ScaleMaximum = instruction.ScaleMaximum,
            World = WebMercator.FromLonLat(lon, lat),
            Symbol = symbol,
            FallbackColor = ColorResolver.ResolveSymbolColor(instruction.SymbolReference, ResolveColor),
            FallbackScale = 0.15 * instruction.SymbolScale * SymbolScale,
            Rotation = instruction.Rotation,
            OffsetXpx = symOffsetXpx,
            OffsetYpx = symOffsetYpx,
        };
    }

    private TextPaintOp? BuildText(TextInstruction instruction, FeatureGeometry geometry)
    {
        var coords = geometry.Coordinates;
        if (string.IsNullOrEmpty(instruction.Text))
            return null;

        double lat, lon;
        if (instruction.CoordinateOverride is { } anchor)
        {
            (lat, lon) = (anchor.Latitude, anchor.Longitude);
        }
        else if (coords.Count == 0)
        {
            return null;
        }
        else if (instruction.LinePlacementPosition.HasValue && coords.Count >= 2
            && geometry.Type == GeometryType.Curve)
        {
            (lat, lon) = InterpolateAlongPolyline(coords, instruction.LinePlacementPosition.Value);
        }
        else if (geometry.Type == GeometryType.Surface && coords.Count >= 3)
        {
            (lat, lon) = ComputeRingCentroid(coords);
        }
        else if (geometry.Type == GeometryType.Curve && coords.Count >= 2)
        {
            (lat, lon) = coords[coords.Count / 2];
        }
        else
        {
            (lat, lon) = coords[0];
        }

        var foreColor = ColorResolver.ApplyTransparency(
            ResolveColor(instruction.FontColor), instruction.FontTransparency);

        RgbaColor? backColor = null;
        if (!string.IsNullOrEmpty(instruction.BackgroundColor))
        {
            var bgBase = ResolveColor(instruction.BackgroundColor);
            backColor = ColorResolver.ApplyTransparency(bgBase, instruction.BackgroundTransparency ?? 0.5);
        }

        return new TextPaintOp
        {
            FeatureReference = instruction.FeatureReference,
            ScaleMinimum = CapScaleMinimum(instruction.ScaleMinimum),
            ScaleMaximum = instruction.ScaleMaximum,
            World = WebMercator.FromLonLat(lon, lat),
            Text = instruction.Text,
            FontSizePx = instruction.FontSize * TextScale,
            ForeColor = foreColor,
            BackColor = backColor,
            HorizontalAlignment = instruction.HorizontalAlignment,
            VerticalAlignment = instruction.VerticalAlignment,
            OffsetXpx = (instruction.OffsetX ?? 0) / S100PixelSizeMm,
            OffsetYpx = (instruction.OffsetY ?? 0) / S100PixelSizeMm,
        };
    }

    private static IReadOnlyList<(double X, double Y)> Project(
        IReadOnlyList<GeoPosition> coords)
    {
        var result = new (double, double)[coords.Count];
        for (int i = 0; i < coords.Count; i++)
            result[i] = WebMercator.FromLonLat(coords[i].Longitude, coords[i].Latitude);
        return result;
    }

    private static GeoPosition InterpolateAlongPolyline(
        IReadOnlyList<GeoPosition> coords, double fraction)
    {
        if (coords.Count < 2)
            return coords[0];

        fraction = Math.Clamp(fraction, 0.0, 1.0);

        double totalLength = 0;
        for (int i = 1; i < coords.Count; i++)
        {
            double dLat = coords[i].Latitude - coords[i - 1].Latitude;
            double dLon = coords[i].Longitude - coords[i - 1].Longitude;
            totalLength += Math.Sqrt(dLat * dLat + dLon * dLon);
        }

        if (totalLength <= 0)
            return coords[0];

        double targetLength = totalLength * fraction;
        double accumulated = 0;

        for (int i = 1; i < coords.Count; i++)
        {
            double dLat = coords[i].Latitude - coords[i - 1].Latitude;
            double dLon = coords[i].Longitude - coords[i - 1].Longitude;
            double segmentLength = Math.Sqrt(dLat * dLat + dLon * dLon);

            if (accumulated + segmentLength >= targetLength)
            {
                double t = segmentLength > 0 ? (targetLength - accumulated) / segmentLength : 0;
                return new GeoPosition(
                    coords[i - 1].Latitude + t * dLat,
                    coords[i - 1].Longitude + t * dLon);
            }

            accumulated += segmentLength;
        }

        return coords[^1];
    }

    private static GeoPosition ComputeRingCentroid(
        IReadOnlyList<GeoPosition> ring)
    {
        int count = ring.Count;
        if (count >= 2 && ring[0] == ring[count - 1])
            count--;
        double sumLat = 0, sumLon = 0;
        for (int i = 0; i < count; i++)
        {
            sumLat += ring[i].Latitude;
            sumLon += ring[i].Longitude;
        }
        return new GeoPosition(sumLat / count, sumLon / count);
    }
}
