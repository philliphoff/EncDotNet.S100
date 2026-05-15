using System.Diagnostics;
using System.Globalization;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders an S-100 Part 9 drawing-instruction display list into a Mapsui
/// <see cref="ILayer"/> by resolving feature geometry from an
/// <see cref="IFeatureGeometryProvider"/>, projecting to EPSG:3857, and
/// applying styles derived from the instruction properties.
/// </summary>
/// <remarks>
/// This renderer is product-agnostic: it consumes the unified
/// <see cref="DrawingInstruction"/> model (produced by S-101 Lua, S-124/S-129/S-421
/// XSLT, or other portrayal pipelines) and a geometry provider that knows how to
/// look up feature geometry for the current product.
/// </remarks>
public sealed class MapsuiDisplayListRenderer
{
    /// <summary>
    /// Key used to store the originating S-100 feature reference on Mapsui features.
    /// Consumers can read <c>feature[FeatureRefKey]</c> to trace a rendered feature
    /// back to its source dataset record.
    /// </summary>
    public const string FeatureRefKey = "S100.FeatureRef";

    /// <summary>
    /// Size, in millimetres, of one S-100 portrayal "pixel" on the nominal
    /// display surface (S-100 Part 9 §3.10.4 — 1 pixel = 0.32 mm).  Used to
    /// convert spec-defined widths from millimetres to Mapsui screen pixels.
    /// </summary>
    private const double S100PixelSizeMm = 0.32;

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-101 Vector";

    /// <summary>
    /// Optional S-100 product identifier (e.g. <c>"S-101"</c>, <c>"S-131"</c>)
    /// used as the <c>s100.product</c> dimension on cache and render-frame
    /// metrics. Set by dataset processors so cache hit/miss counts can be
    /// attributed by product. When <see langword="null"/>, the counter is
    /// emitted without a product tag (preserving legacy behaviour for
    /// direct callers).
    /// </summary>
    public string? Product { get; set; }

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

    /// <summary>
    /// Optional function that returns a <see cref="LineStyle"/> definition by
    /// name. When set, line instructions that carry only a
    /// <c>lineStyleReference</c> (e.g. S-421 <c>RTEACTLEGLINE</c>) will render
    /// using the referenced colour, width, and dash pattern from the
    /// portrayal catalogue.
    /// </summary>
    public Func<string, LineStyle?>? LineStyleProvider { get; set; }

    /// <summary>
    /// Global scale factor applied to all point symbols (default 1.0).
    /// </summary>
    public double SymbolScale { get; set; } = 1.0;

    /// <summary>
    /// Global scale factor applied to all text labels (default 1.0).
    /// </summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>
    /// Optional shared cache for processed-SVG symbol entries and rasterised
    /// pattern tiles. When set, the renderer routes its symbol/pattern
    /// lookups through this cache so re-renders of the same dataset (e.g.
    /// after a palette toggle, time-step scrub, or mariner-setting change)
    /// reuse the SVG processing + pattern rasterization work. When unset
    /// (the default), a per-renderer cache is used, preserving legacy
    /// behaviour for ad-hoc / one-shot callers such as tests.
    /// </summary>
    public MapsuiRenderAssetCache? AssetCache { get; set; }

    // Per-renderer fallback used when AssetCache is null.
    private readonly MapsuiRenderAssetCache _localAssetCache = new();

    /// <summary>
    /// A cached SVG symbol: its Mapsui <c>svg-content://</c> source URI plus
    /// the pivot-to-bounds-centre offset recovered from the raw SVG before
    /// <see cref="SvgProcessor"/> stripped its layout elements.  The relative
    /// offset (in fractions of viewBox size) is what Mapsui's
    /// <c>RelativeOffset</c> consumes; the millimetre offset is retained in
    /// case a future code path needs an absolute-pixel translation.
    /// </summary>
    internal readonly record struct SymbolEntry(
        string? Source,
        double PivotOffsetXMm,
        double PivotOffsetYMm,
        double RelativeOffsetX,
        double RelativeOffsetY);

    /// <summary>
    /// Renders the supplied display list against the geometry provided by
    /// <paramref name="geometryProvider"/>, returning a Mapsui memory layer.
    /// </summary>
    /// <remarks>
    /// Drawing instructions whose feature reference cannot be resolved by
    /// the provider are silently skipped; this lets callers pre-process the
    /// list (e.g. merging S-101 SAFCON labels) without worrying about
    /// synthesised feature references.
    /// </remarks>
    public ILayer Render(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.render.frame");
        __activity?.SetTag("s100.render.target", "mapsui");
        __activity?.SetTag("s100.render.instructions.count", instructions.Count);
        var renderStart = Stopwatch.GetTimestamp();

        S100Diag.Telemetry.InstructionsProcessed.Add(instructions.Count);

        // Ensure the custom pattern fill renderer is registered before Mapsui
        // encounters any AnchoredPatternFillStyle instances.
        AnchoredPatternFillRenderer.Register();

        // 1. Sort instructions by rendering order: areas first, then lines, then points/text
        //    Within same type, sort by DrawingPriority
        var sorted = instructions
            .OrderBy(i => i.Plane == Pipelines.Vector.DisplayPlane.OverRadar ? 1 : 0)
            .ThenBy(i => i switch
            {
                AreaInstruction => 0,
                LineInstruction => 1,
                PointInstruction => 2,
                TextInstruction => 3,
                _ => 4,
            })
            .ThenBy(i => i.DrawingPriority)
            .ToList();

        // 2. Build color resolver from palette
        var resolveColor = BuildColorResolver(Palette);

        // 3. Convert each instruction to a Mapsui feature.
        //    Pattern fills are collected and merged per unique pattern so that
        //    overlapping polygons with the same globally-anchored pattern are
        //    drawn exactly once, preventing alpha accumulation artifacts.
        //    Merged patterns are inserted after all color fills to ensure no
        //    solid fill can cover a previously-drawn pattern.
        var mapFeatures = new List<IFeature>();
        var patternEntries = new List<(byte[] TilePng, int Priority, List<Polygon> Polygons)>();
        int lastColorFillIndex = -1;

        // Track which features produce pattern fills, so we can identify
        // "non-patterned" color fill areas (like land) that should occlude patterns.
        var featuresWithPatterns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instruction in sorted)
        {
            if (instruction is AreaInstruction { AreaFillReference: not null } pa)
                featuresWithPatterns.Add(pa.FeatureReference);
        }

        // Collect opaque color fill polygons from features that do NOT also
        // produce a pattern fill. These represent areas (such as land) where
        // patterns from other features should not be visible.
        var nonPatternedColorFillPolygons = new List<Polygon>();

        foreach (var instruction in sorted)
        {
            var geom = geometryProvider.GetGeometry(instruction.FeatureReference);

            // LineInstructions with CoordinatesOverride carry their own
            // synthetic geometry (from augmented rays/arcs) and don't need
            // the feature's natural geometry to have coordinates.
            bool hasAugmentedLine = instruction is LineInstruction { CoordinatesOverride: not null };
            if (!hasAugmentedLine && (geom is null || geom.Coordinates.Count == 0))
                continue;

            // Defer pattern fills for merging
            if (instruction is AreaInstruction { AreaFillReference: { } patternRef } areaPattern && geom is not null)
            {
                var tilePng = GetPatternTilePng(patternRef);
                if (tilePng is not null)
                {
                    var polygon = CreatePolygonFromGeometry(geom);
                    if (polygon is not null)
                    {
                        // Find existing entry with the same tile and priority, or create a new one
                        var existing = patternEntries.Find(e =>
                            ReferenceEquals(e.TilePng, tilePng) && e.Priority == areaPattern.DrawingPriority);
                        if (existing.TilePng is not null)
                        {
                            existing.Polygons.Add(polygon);
                        }
                        else
                        {
                            patternEntries.Add((tilePng, areaPattern.DrawingPriority, new List<Polygon> { polygon }));
                        }
                    }
                }
                continue;
            }

            // Track non-patterned color fills (e.g. land areas) for pattern clipping
            if (instruction is AreaInstruction { FillColor: not null } colorFill
                && geom is not null
                && !featuresWithPatterns.Contains(colorFill.FeatureReference))
            {
                var polygon = CreatePolygonFromGeometry(geom);
                if (polygon is not null)
                    nonPatternedColorFillPolygons.Add(polygon);
            }

            var mapFeature = CreateMapFeature(instruction, geom, resolveColor, this);
            if (mapFeature is not null)
            {
                mapFeatures.Add(mapFeature);

                // Track where color fills end so we can insert patterns after them
                if (instruction is AreaInstruction { FillColor: not null })
                    lastColorFillIndex = mapFeatures.Count;
            }
        }

        // Clip lower-priority pattern groups to exclude areas covered by
        // higher-priority patterns so that, e.g., DIAMOND1 (priority 9)
        // diamonds do not show through DQUAL (priority 12) pattern zones.
        // Also clips all patterns against non-patterned color fill areas
        // (e.g. land) so patterns don't bleed over land.
        patternEntries.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        var clippedPatterns = ClipPatternsByPriority(patternEntries, nonPatternedColorFillPolygons);

        // Insert merged pattern fill features after all color fills but before
        // lines/points/text. This ensures no solid fill can occlude a pattern.
        int insertAt = lastColorFillIndex >= 0 ? lastColorFillIndex : 0;
        foreach (var (tilePng, geometry) in clippedPatterns)
        {
            var feature = new GeometryFeature(geometry);
            feature.Styles.Add(new AnchoredPatternFillStyle { TilePng = tilePng });
            mapFeatures.Insert(insertAt, feature);
            insertAt++;
        }

        S100Diag.Telemetry.StylesApplied.Add(mapFeatures.Sum(f => f.Styles.Count));
        S100Diag.Telemetry.FrameDuration.Record(
            (Stopwatch.GetTimestamp() - renderStart) * 1000.0 / Stopwatch.Frequency);

        return new MemoryLayer
        {
            Name = LayerName,
            Features = mapFeatures,
            Style = null,
        };
    }

    private static IFeature? CreateMapFeature(
        DrawingInstruction instruction,
        FeatureGeometry? geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        var feature = instruction switch
        {
            AreaInstruction area when geometry is not null => CreateAreaFeature(area, geometry, resolveColor, renderer),
            LineInstruction line => CreateLineFeature(line, geometry, resolveColor, renderer),
            PointInstruction point when geometry is not null => CreatePointFeature(point, geometry, resolveColor, renderer),
            TextInstruction text when geometry is not null => CreateTextFeature(text, geometry, resolveColor, renderer),
            _ => null,
        };

        if (feature is not null)
        {
            feature[FeatureRefKey] = instruction.FeatureReference;
            ApplyScaleVisibility(feature, instruction);
        }

        return feature;
    }

    /// <summary>
    /// S-100 Part 9 scale denominator → Mapsui resolution (m/px in EPSG:3857)
    /// at 96 DPI: 1 px = 0.28 mm = 0.00028 m on the nominal display surface,
    /// so resolution ≈ scaleDenominator × 0.00028.
    /// </summary>
    private const double DenomToResolutionMetres = 0.00028;

    /// <summary>
    /// Maps the S-100 Part 9 §11.1 <see cref="DrawingInstruction.ScaleMinimum"/> /
    /// <see cref="DrawingInstruction.ScaleMaximum"/> denominators on each
    /// rendered style.  Per the field documentation in
    /// <c>DrawingInstruction</c>, <c>ScaleMinimum</c> is the most zoomed-out
    /// limit (largest allowed denominator) and maps to Mapsui's
    /// <c>MaxVisible</c>; <c>ScaleMaximum</c> is the most zoomed-in limit
    /// (smallest allowed denominator) and maps to <c>MinVisible</c>.
    /// </summary>
    private static void ApplyScaleVisibility(IFeature feature, DrawingInstruction instruction)
    {
        if (!instruction.ScaleMinimum.HasValue && !instruction.ScaleMaximum.HasValue)
            return;

        double? maxRes = instruction.ScaleMinimum.HasValue
            ? instruction.ScaleMinimum.Value * DenomToResolutionMetres
            : (double?)null;
        double? minRes = instruction.ScaleMaximum.HasValue
            ? instruction.ScaleMaximum.Value * DenomToResolutionMetres
            : (double?)null;

        foreach (var style in feature.Styles)
        {
            if (style is null) continue;
            if (maxRes.HasValue) style.MaxVisible = maxRes.Value;
            if (minRes.HasValue) style.MinVisible = minRes.Value;
        }
    }

    private static IFeature? CreateAreaFeature(
        AreaInstruction instruction,
        FeatureGeometry geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        var polygon = CreatePolygonFromGeometry(geometry);
        if (polygon is null)
            return null;

        if (instruction.FillColor is not null)
        {
            // Solid color fill
            var fillColor = resolveColor(instruction.FillColor);

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

        // Pattern fill via AreaFillReference, using a custom style so the
        // pattern is anchored to the geometry and moves during panning.
        var tilePng = renderer.GetPatternTilePng(instruction.AreaFillReference);
        if (tilePng is null)
            return null;

        var patternStyle = new AnchoredPatternFillStyle
        {
            TilePng = tilePng,
        };

        var patternFeature = new GeometryFeature(polygon);
        patternFeature.Styles.Add(patternStyle);
        return patternFeature;
    }

    private static IFeature? CreateLineFeature(
        LineInstruction instruction,
        FeatureGeometry? geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        // Prefer augmented (synthetic) coordinates from AugmentedRay/ArcByRadius
        // over the feature's natural geometry.
        var coords = instruction.CoordinatesOverride ?? geometry?.Coordinates;
        if (coords is null || coords.Count < 2)
            return null;

        var projected = ProjectCoordinates(coords);
        var lineString = new LineString(projected.ToArray());

        // Resolve color, width, and dash pattern.  Inline lineStyle wins; if
        // only a lineStyleReference is supplied, look up the external style
        // through the LineStyleProvider (e.g. S-421 RTEACTLEGLINE).
        string? colorToken = instruction.LineColor;
        double width = instruction.LineWidth;
        bool dashed = instruction.Dashes is { Count: > 0 };

        if (colorToken is null && instruction.LineStyleReference is not null && renderer.LineStyleProvider is not null)
        {
            var externalStyle = renderer.LineStyleProvider(instruction.LineStyleReference);
            if (externalStyle is not null)
            {
                colorToken = externalStyle.Color;
                if (externalStyle.Width > 0)
                    width = externalStyle.Width;
                if (externalStyle.DashPattern is { Length: > 0 })
                    dashed = true;
            }
        }

        // S-100 Part 9 specifies pen widths in millimetres on the nominal
        // display surface, where 1 portrayal pixel = 0.32 mm.  Mapsui Pen.Width
        // is in screen pixels, so convert mm → px before assigning.
        var widthPx = width > 0 ? (width / S100PixelSizeMm) : 0.0;
        var pen = new Pen
        {
            Color = resolveColor(colorToken),
            Width = Math.Max(widthPx, 1.0),
        };
        if (dashed && instruction.Dashes is { Count: > 0 })
        {
            // Build a SkiaSharp-compatible [on, off] dash array from the S-100
            // dash specification.  The Dash command gives (offset, gapMm) pairs
            // and the LineStyle second parameter gives the dash "on" length.
            // NOTE: SkiaSharp applies the dash pattern in screen space, which
            // causes a subtle "marquee" shift when the viewport pans.  This is
            // an inherent limitation of the SkiaSharp/Mapsui rendering pipeline;
            // a proper fix would require a custom Mapsui renderer that anchors
            // the dash phase to the geometry's world coordinates.
            var onMm = instruction.DashOnLengthMm > 0
                ? instruction.DashOnLengthMm
                : instruction.Dashes[0].Length;   // fallback: use gap length
            var gapMm = instruction.Dashes[0].Length;
            var onPx = (float)(onMm / S100PixelSizeMm);
            var gapPx = (float)(gapMm / S100PixelSizeMm);
            pen.DashArray = [Math.Max(onPx, 1f), Math.Max(gapPx, 1f)];
            pen.PenStyle = PenStyle.UserDefined;
        }
        else if (dashed)
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
        PointInstruction instruction,
        FeatureGeometry geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        var coords = geometry.Coordinates;

        // S-100 Part 9 §11.5 AugmentedPoint (GeographicCRS) lets a rule
        // override the per-instruction anchor — e.g. SOUNDG03 places each
        // sounding of a MultiPoint feature at its own coordinate.
        double lat, lon;
        if (instruction.CoordinateOverride is { } anchor)
        {
            (lat, lon) = (anchor.Latitude, anchor.Longitude);
        }
        else
        {
            if (coords.Count == 0)
                return null;
            (lat, lon) = coords[0];
        }
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var feature = new PointFeature(mx, my);

        // Translate the S-100 §11.3 LocalOffset (millimetres on the nominal
        // display surface) to screen pixels using the standard 1 px = 0.32 mm
        // convention.  Both ImageStyle and SymbolStyle expose Offset via the
        // shared VectorStyle base.
        var symOffsetXpx = instruction.LocalOffsetX / S100PixelSizeMm;
        var symOffsetYpx = instruction.LocalOffsetY / S100PixelSizeMm;
        var hasSymbolOffset = symOffsetXpx != 0 || symOffsetYpx != 0;

        // Try to render with an actual SVG symbol
        var symbolRef = instruction.SymbolReference;
        var entry = renderer.GetSymbolEntry(symbolRef);
        var svgSource = entry.Source;
        if (svgSource is not null)
        {
            var svgScale = 0.6 * instruction.SymbolScale * renderer.SymbolScale;

            // Recover S-100 Part 9 §11.5 pivot placement.  Mapsui's ImageStyle
            // centres the SVG bounding box on the anchor (pivot semantics are
            // ignored), so composite symbols built from off-centre glyph
            // tiles — most visibly multi-digit soundings — collapse on top of
            // each other.  Mapsui's RelativeOffset is expressed as a fraction
            // of the symbol size and matches the (vbCenter - pivot)/vbSize
            // ratio computed from the SVG, so it stays correct regardless of
            // SymbolScale or any mm→px convention used by the SVG rasteriser.
            // Mapsui's RelativeOffset uses +Y = up (map frame); SVG/screen use
            // +Y = down, so the Y component is negated here.
            var pivotRelX = entry.RelativeOffsetX;
            var pivotRelY = -entry.RelativeOffsetY;
            var hasPivotRelative = pivotRelX != 0 || pivotRelY != 0;

            // Add a nearly-invisible rectangle as a hit-test area so that
            // tapping on a transparent portion of the SVG still picks this
            // feature.  The rectangle is slightly larger than the SVG to
            // provide a comfortable tap target.
            var hitStyle = new SymbolStyle
            {
                SymbolType = SymbolType.Rectangle,
                SymbolScale = svgScale * 1.2,
                Fill = new Brush { Color = new MapsuiColor(0, 0, 0, 1) },
                Line = null,
                Outline = null,
            };
            if (instruction.Rotation.HasValue)
                hitStyle.SymbolRotation = instruction.Rotation.Value;
            if (hasSymbolOffset)
                hitStyle.Offset = new Offset(symOffsetXpx, symOffsetYpx);
            if (hasPivotRelative)
                hitStyle.RelativeOffset = new RelativeOffset(pivotRelX, pivotRelY);
            feature.Styles.Add(hitStyle);

            var style = new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
            };
            style.SymbolScale = svgScale;
            if (instruction.Rotation.HasValue)
                style.SymbolRotation = instruction.Rotation.Value;
            if (hasSymbolOffset)
                style.Offset = new Offset(symOffsetXpx, symOffsetYpx);
            if (hasPivotRelative)
                style.RelativeOffset = new RelativeOffset(pivotRelX, pivotRelY);
            feature.Styles.Add(style);
        }
        else
        {
            // Fallback: colored dot
            var symbolColor = ResolveSymbolColor(symbolRef, resolveColor);
            var style = new SymbolStyle
            {
                SymbolScale = 0.15 * instruction.SymbolScale * renderer.SymbolScale,
                Fill = new Brush { Color = symbolColor },
                Line = null,
            };
            if (instruction.Rotation.HasValue)
                style.SymbolRotation = instruction.Rotation.Value;
            if (hasSymbolOffset)
                style.Offset = new Offset(symOffsetXpx, symOffsetYpx);
            feature.Styles.Add(style);
        }

        return feature;
    }

    private static IFeature? CreateTextFeature(
        TextInstruction instruction,
        FeatureGeometry geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        var coords = geometry.Coordinates;
        if (string.IsNullOrEmpty(instruction.Text))
            return null;

        // S-100 Part 9 §11.5 AugmentedPoint (GeographicCRS) anchor override
        // takes precedence over any feature-derived anchor.
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

        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        // Apply the optional S-100 §11.4 transparency attribute (0 = opaque,
        // 1 = fully transparent) on top of the resolved palette colour.
        var textColor = ApplyTransparency(resolveColor(instruction.FontColor), instruction.FontTransparency);

        // Convert mm offsets (S-100 §11.4 nominal display surface) to screen
        // pixels using the standard 1 px = 0.32 mm convention.
        var offsetXpx = (instruction.OffsetXmm ?? 0) / S100PixelSizeMm;
        var offsetYpx = (instruction.OffsetYmm ?? 0) / S100PixelSizeMm;

        Brush? backBrush = null;
        if (!string.IsNullOrEmpty(instruction.BackgroundColor))
        {
            var bgBase = resolveColor(instruction.BackgroundColor);
            // Default background transparency: when the spec leaves it
            // unspecified, fall back to the same translucency convention as
            // legacy renderers (~50%).
            var bgColor = ApplyTransparency(bgBase, instruction.BackgroundTransparency ?? 0.5);
            backBrush = new Brush { Color = bgColor };
        }

        var style = new LabelStyle
        {
            Text = instruction.Text,
            ForeColor = textColor,
            Font = new Font { Size = instruction.FontSize * renderer.TextScale },
            HorizontalAlignment = MapHAlign(instruction.HorizontalAlignment),
            VerticalAlignment = MapVAlign(instruction.VerticalAlignment),
            Offset = new Offset(offsetXpx, offsetYpx),
            BackColor = backBrush,
        };

        var feature = new PointFeature(mx, my);
        feature.Styles.Add(style);
        return feature;
    }

    /// <summary>
    /// Returns <paramref name="color"/> with its alpha attenuated by
    /// <paramref name="transparency"/> (0 = unchanged opaque, 1 = fully
    /// transparent).  When <paramref name="transparency"/> is null the input
    /// colour is returned unchanged.
    /// </summary>
    private static MapsuiColor ApplyTransparency(MapsuiColor color, double? transparency)
    {
        if (!transparency.HasValue)
            return color;
        var t = Math.Clamp(transparency.Value, 0.0, 1.0);
        var a = (int)Math.Round(color.A * (1.0 - t));
        return new MapsuiColor(color.R, color.G, color.B, a);
    }

    private static LabelStyle.HorizontalAlignmentEnum MapHAlign(TextHorizontalAlignment a) => a switch
    {
        TextHorizontalAlignment.Start => LabelStyle.HorizontalAlignmentEnum.Left,
        TextHorizontalAlignment.End => LabelStyle.HorizontalAlignmentEnum.Right,
        _ => LabelStyle.HorizontalAlignmentEnum.Center,
    };

    private static LabelStyle.VerticalAlignmentEnum MapVAlign(TextVerticalAlignment a) => a switch
    {
        TextVerticalAlignment.Top => LabelStyle.VerticalAlignmentEnum.Top,
        TextVerticalAlignment.Bottom => LabelStyle.VerticalAlignmentEnum.Bottom,
        _ => LabelStyle.VerticalAlignmentEnum.Center,
    };

    /// <summary>
    /// Centroid of a closed exterior ring (lat/lon, simple unweighted average
    /// of the unique vertices).  Sufficient for label placement on the
    /// near-equirectangular ring sizes typical of S-100 surface features.
    /// </summary>
    private static (double Lat, double Lon) ComputeRingCentroid(
        IReadOnlyList<(double Lat, double Lon)> ring)
    {
        // Drop the closing duplicate vertex if present.
        int count = ring.Count;
        if (count >= 2 && ring[0] == ring[count - 1])
            count--;
        double sumLat = 0, sumLon = 0;
        for (int i = 0; i < count; i++)
        {
            sumLat += ring[i].Lat;
            sumLon += ring[i].Lon;
        }
        return (sumLat / count, sumLon / count);
    }



    // ── Coordinate projection ──────────────────────────────────────────

    /// <summary>
    /// Interpolates a position at a relative distance (0.0–1.0) along a polyline.
    /// </summary>
    private static (double Lat, double Lon) InterpolateAlongPolyline(
        IReadOnlyList<(double Lat, double Lon)> coords, double fraction)
    {
        if (coords.Count < 2)
            return coords[0];

        fraction = Math.Clamp(fraction, 0.0, 1.0);

        // Compute total length of the polyline (in degrees — approximate but fine for interpolation)
        double totalLength = 0;
        for (int i = 1; i < coords.Count; i++)
        {
            double dLat = coords[i].Lat - coords[i - 1].Lat;
            double dLon = coords[i].Lon - coords[i - 1].Lon;
            totalLength += Math.Sqrt(dLat * dLat + dLon * dLon);
        }

        if (totalLength <= 0)
            return coords[0];

        double targetLength = totalLength * fraction;
        double accumulated = 0;

        for (int i = 1; i < coords.Count; i++)
        {
            double dLat = coords[i].Lat - coords[i - 1].Lat;
            double dLon = coords[i].Lon - coords[i - 1].Lon;
            double segmentLength = Math.Sqrt(dLat * dLat + dLon * dLon);

            if (accumulated + segmentLength >= targetLength)
            {
                double t = segmentLength > 0 ? (targetLength - accumulated) / segmentLength : 0;
                return (
                    coords[i - 1].Lat + t * dLat,
                    coords[i - 1].Lon + t * dLon);
            }

            accumulated += segmentLength;
        }

        return coords[^1];
    }

    private static Polygon? CreatePolygonFromGeometry(FeatureGeometry geometry)
    {
        var shell = BuildLinearRing(geometry.Coordinates);
        if (shell is null)
            return null;

        if (geometry.InteriorRings.Count == 0)
            return new Polygon(shell);

        var holes = new List<LinearRing>(geometry.InteriorRings.Count);
        foreach (var hole in geometry.InteriorRings)
        {
            var ring = BuildLinearRing(hole);
            if (ring is not null)
                holes.Add(ring);
        }

        return holes.Count == 0
            ? new Polygon(shell)
            : new Polygon(shell, holes.ToArray());
    }

    private static LinearRing? BuildLinearRing(IReadOnlyList<(double Latitude, double Longitude)> coords)
    {
        if (coords.Count < 3)
            return null;

        var projected = ProjectCoordinates(coords);

        // Close the ring if not already closed
        if (projected.Count > 0 && !projected[0].Equals2D(projected[^1]))
            projected.Add(new Coordinate(projected[0].X, projected[0].Y));

        if (projected.Count < 4)
            return null;

        return new LinearRing(projected.ToArray());
    }

    private static List<Coordinate> ProjectCoordinates(IReadOnlyList<(double Latitude, double Longitude)> coords)
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
    /// Returns a cached <see cref="SymbolEntry"/> for the given symbol name,
    /// processing and caching the raw SVG on first access.  The entry's
    /// <c>Source</c> is <c>null</c> when no <see cref="SymbolProvider"/> is
    /// configured or the symbol cannot be resolved.
    /// </summary>
    private SymbolEntry GetSymbolEntry(string? symbolRef)
    {
        if (string.IsNullOrEmpty(symbolRef) || SymbolProvider is null)
            return default;

        var resolveStart = Stopwatch.GetTimestamp();
        var cache = AssetCache ?? _localAssetCache;

        var entry = cache.GetOrAddSymbol(Palette, symbolRef, out var wasCached, ProduceSymbolEntry);

        // Tag cache + resolve metrics with the active product (when known)
        // so dashboards can attribute hit/miss counts to S-101 vs. S-131
        // vs. an unconfigured caller.
        var productTag = new KeyValuePair<string, object?>(TelemetryTags.Product, Product);

        if (wasCached)
        {
            S100Diag.Telemetry.SymbolCacheHit.Add(1, productTag);
            S100Diag.Telemetry.SymbolResolveDuration.Record(
                (Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / Stopwatch.Frequency,
                productTag,
                new KeyValuePair<string, object?>(TelemetryTags.SymbolResult, "hit"));
            return entry;
        }

        S100Diag.Telemetry.SymbolCacheMiss.Add(1, productTag);
        S100Diag.Telemetry.SymbolResolveDuration.Record(
            (Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / Stopwatch.Frequency,
            productTag,
            new KeyValuePair<string, object?>(TelemetryTags.SymbolResult, entry.Source is null ? "fallback" : "miss"));
        return entry;
    }

    private SymbolEntry ProduceSymbolEntry(string symbolRef)
    {
        try
        {
            var svgContent = SymbolProvider!(symbolRef);
            if (svgContent is not null)
            {
                // Recover S-100 Part 9 §11.5 pivot placement from the *raw*
                // SVG before SvgProcessor strips the pivotPoint layout
                // element.  Without this, Mapsui centres the SVG bbox on the
                // anchor and composite symbols (e.g. multi-digit soundings)
                // collapse onto the same point.
                var pivot = SvgPivotMetrics.TryParse(svgContent);
                var processed = SvgProcessor.Process(svgContent, Palette);
                return new SymbolEntry(
                    "svg-content://" + processed,
                    pivot?.PivotToBoundsCenterMm.X ?? 0.0,
                    pivot?.PivotToBoundsCenterMm.Y ?? 0.0,
                    pivot?.RelativeOffset.X ?? 0.0,
                    pivot?.RelativeOffset.Y ?? 0.0);
            }
        }
        catch
        {
            // Symbol not found or malformed — fall back to dot
        }

        return default;
    }

    // ── Pattern tile rasterization ─────────────────────────────────────

    /// <summary>
    /// Returns rasterized pattern tile PNG bytes for the given area fill name,
    /// processing and caching on first access.
    /// </summary>
    private byte[]? GetPatternTilePng(string? fillName)
    {
        if (string.IsNullOrEmpty(fillName) || AreaFillProvider is null || SymbolProvider is null)
            return null;

        var cache = AssetCache ?? _localAssetCache;
        var tile = cache.GetOrAddPatternTile(Palette, fillName, out var wasCached, ProducePatternTile);

        // Pattern cache counters (PR-CACHE-7 in the asset-caching audit) so
        // every reuse of a rasterised pattern tile is visible alongside the
        // symbol cache counters.
        var productTag = new KeyValuePair<string, object?>(TelemetryTags.Product, Product);
        if (wasCached)
        {
            S100Diag.Telemetry.PatternCacheHit.Add(1, productTag);
        }
        else
        {
            S100Diag.Telemetry.PatternCacheMiss.Add(1, productTag);
        }
        return tile;
    }

    private byte[]? ProducePatternTile(string fillName)
    {
        try
        {
            var areaFill = AreaFillProvider!(fillName);
            if (areaFill?.PatternSymbol is not null)
            {
                var svgContent = SymbolProvider!(areaFill.PatternSymbol);
                if (svgContent is not null)
                {
                    var processed = SvgProcessor.Process(svgContent, Palette);
                    return SkiaSvgRasterizer.RasterizePatternTile(processed, areaFill);
                }
            }
        }
        catch
        {
            // Area fill or symbol not found — skip pattern
        }

        return null;
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

            if (palette is not null && palette.TryResolve(token, out var hex))
                return HexToColor(hex);

            // S-100 Part 9 instructions may also carry inline hex literals
            // (e.g. the S-421 RouteActionPoint XSL emits <foreground>AA44A8</foreground>).
            // Treat any 6- or 8-digit hex string (with or without a leading '#')
            // as a literal colour before giving up.
            if (TryParseHexLiteral(token, out var literal))
                return literal;

            return MapsuiColor.Black;
        };
    }

    private static MapsuiColor HexToColor(string hex)
    {
        if (TryParseHexLiteral(hex, out var color))
            return color;

        return MapsuiColor.Black;
    }

    /// <summary>
    /// Parses a literal hex colour in the formats <c>RRGGBB</c>,
    /// <c>#RRGGBB</c>, <c>RRGGBBAA</c>, or <c>#RRGGBBAA</c>.  Returns false
    /// for anything else so the caller can fall back to a palette token
    /// lookup.
    /// </summary>
    private static bool TryParseHexLiteral(string value, out MapsuiColor color)
    {
        color = MapsuiColor.Black;
        if (string.IsNullOrEmpty(value)) return false;

        var span = value.AsSpan();
        if (span[0] == '#') span = span[1..];
        if (span.Length != 6 && span.Length != 8) return false;

        if (!int.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        int a = 255;
        if (span.Length == 8 &&
            !int.TryParse(span.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
        {
            return false;
        }

        color = new MapsuiColor(r, g, b, a);
        return true;
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

    /// <summary>
    /// Clips pattern groups so that:
    /// 1. Lower-priority patterns are clipped by higher-priority pattern areas
    ///    (e.g. DIAMOND1 at priority 9 is clipped by DQUAL at priority 12).
    /// 2. All patterns are clipped to exclude non-patterned color fill areas
    ///    (e.g. land areas) where patterns should not be visible.
    /// </summary>
    /// <remarks>
    /// Entries must be sorted by ascending priority before calling.
    /// Returns (tilePng, clippedGeometry) pairs in ascending priority order.
    /// </remarks>
    private static List<(byte[] TilePng, Geometry Geometry)> ClipPatternsByPriority(
        List<(byte[] TilePng, int Priority, List<Polygon> Polygons)> entries,
        List<Polygon> nonPatternedColorFills)
    {
        if (entries.Count == 0)
            return [];

        // Build a union of non-patterned color fill areas (e.g. land) that
        // should occlude all pattern fills.
        Geometry? excludeAreas = null;
        if (nonPatternedColorFills.Count > 0)
        {
            try
            {
                Geometry nonPatterned = nonPatternedColorFills.Count == 1
                    ? nonPatternedColorFills[0]
                    : new MultiPolygon(nonPatternedColorFills.ToArray());
                excludeAreas = nonPatterned.Union();
            }
            catch (TopologyException)
            {
                // If union fails, skip land clipping
            }
        }

        // Build merged geometry for each entry
        var merged = entries.Select(e =>
        {
            Geometry g = e.Polygons.Count == 1
                ? e.Polygons[0]
                : new MultiPolygon(e.Polygons.ToArray());
            return (e.TilePng, e.Priority, Geometry: g);
        }).ToList();

        // Walk from highest priority down, accumulating a union of
        // higher-priority areas that will clip lower-priority patterns.
        Geometry? higherPriorityAreas = null;
        var result = new (byte[] TilePng, Geometry Geometry)[merged.Count];

        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var (tile, _, geometry) = merged[i];

            // Start with the original geometry, then subtract exclusion areas
            var clipped = geometry;

            // Subtract higher-priority pattern areas
            if (higherPriorityAreas is not null)
            {
                try
                {
                    clipped = clipped.Difference(higherPriorityAreas);
                }
                catch (TopologyException)
                {
                    // Fall back to unclipped geometry
                }
                catch (ArgumentException)
                {
                    // NTS Difference rejects GeometryCollection arguments;
                    // accumulated unions can degenerate to that shape.
                    // Fall back to unclipped geometry.
                }
            }

            // Subtract non-patterned color fill areas (e.g. land)
            if (excludeAreas is not null)
            {
                try
                {
                    clipped = clipped.Difference(excludeAreas);
                }
                catch (TopologyException)
                {
                    // Fall back to current clipped geometry
                }
                catch (ArgumentException)
                {
                    // GeometryCollection rejected by NTS Difference; keep current geometry.
                }
            }

            result[i] = (tile, clipped);

            // Add this entry's original (unclipped) area to the higher-priority union
            try
            {
                higherPriorityAreas = higherPriorityAreas?.Union(geometry) ?? geometry;
            }
            catch (TopologyException)
            {
                // If union fails, keep the existing accumulated area
            }
            catch (ArgumentException)
            {
                // NTS Union rejects GeometryCollection LHS;
                // keep existing accumulated area.
            }
        }

        return [.. result];
    }
}
