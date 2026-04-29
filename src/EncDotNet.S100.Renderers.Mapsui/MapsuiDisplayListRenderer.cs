using System.Globalization;
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

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-101 Vector";

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
    /// Global scale factor applied to all point symbols (default 1.0).
    /// </summary>
    public double SymbolScale { get; set; } = 1.0;

    /// <summary>
    /// Global scale factor applied to all text labels (default 1.0).
    /// </summary>
    public double TextScale { get; set; } = 1.0;

    // Caches processed SVG data URIs keyed by symbol name.
    private readonly Dictionary<string, string?> _symbolDataUriCache = new(StringComparer.OrdinalIgnoreCase);

    // Caches rasterized pattern tile PNG bytes keyed by area fill name.
    private readonly Dictionary<string, byte[]?> _patternTileCache = new(StringComparer.OrdinalIgnoreCase);

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
            if (geom is null || geom.Coordinates.Count == 0)
                continue;

            // Defer pattern fills for merging
            if (instruction is AreaInstruction { AreaFillReference: { } patternRef } areaPattern)
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

        return new MemoryLayer
        {
            Name = LayerName,
            Features = mapFeatures,
            Style = null,
        };
    }

    private static IFeature? CreateMapFeature(
        DrawingInstruction instruction,
        FeatureGeometry geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        var feature = instruction switch
        {
            AreaInstruction area => CreateAreaFeature(area, geometry, resolveColor, renderer),
            LineInstruction line => CreateLineFeature(line, geometry, resolveColor),
            PointInstruction point => CreatePointFeature(point, geometry, resolveColor, renderer),
            TextInstruction text => CreateTextFeature(text, geometry, resolveColor, renderer),
            _ => null,
        };

        if (feature is not null)
        {
            feature[FeatureRefKey] = instruction.FeatureReference;
        }

        return feature;
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
        FeatureGeometry geometry,
        Func<string?, MapsuiColor> resolveColor)
    {
        var coords = geometry.Coordinates;
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
        PointInstruction instruction,
        FeatureGeometry geometry,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiDisplayListRenderer renderer)
    {
        var coords = geometry.Coordinates;
        if (coords.Count == 0)
            return null;

        // Use first coordinate as the point location
        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var feature = new PointFeature(mx, my);

        // Try to render with an actual SVG symbol
        var symbolRef = instruction.SymbolReference;
        var svgSource = renderer.GetSymbolSource(symbolRef);
        if (svgSource is not null)
        {
            var svgScale = 0.6 * instruction.SymbolScale * renderer.SymbolScale;

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
            feature.Styles.Add(hitStyle);

            var style = new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
            };
            style.SymbolScale = svgScale;
            if (instruction.Rotation.HasValue)
                style.SymbolRotation = instruction.Rotation.Value;
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
        if (coords.Count == 0 || string.IsNullOrEmpty(instruction.Text))
            return null;

        // Determine position: if LinePlacementPosition is set and we have
        // enough coordinates to form a line, interpolate along the polyline.
        double lat, lon;
        if (instruction.LinePlacementPosition.HasValue && coords.Count >= 2)
        {
            (lat, lon) = InterpolateAlongPolyline(coords, instruction.LinePlacementPosition.Value);
        }
        else
        {
            (lat, lon) = coords[0];
        }

        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var textColor = resolveColor(instruction.FontColor);
        var style = new LabelStyle
        {
            Text = instruction.Text,
            ForeColor = textColor,
            Font = new Font { Size = instruction.FontSize * renderer.TextScale },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            BackColor = null,
        };

        var feature = new PointFeature(mx, my);
        feature.Styles.Add(style);
        return feature;
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
    /// Returns a Mapsui svg-content:// source string for the given symbol name,
    /// processing and caching the SVG on first access. Returns null if no
    /// SymbolProvider is set or the symbol is not found.
    /// </summary>
    private string? GetSymbolSource(string? symbolRef)
    {
        if (string.IsNullOrEmpty(symbolRef) || SymbolProvider is null)
            return null;

        if (_symbolDataUriCache.TryGetValue(symbolRef, out var cached))
            return cached;

        string? source = null;
        try
        {
            var svgContent = SymbolProvider(symbolRef);
            if (svgContent is not null)
            {
                var processed = SvgProcessor.Process(svgContent, Palette);
                source = "svg-content://" + processed;
            }
        }
        catch
        {
            // Symbol not found or malformed — fall back to dot
        }

        _symbolDataUriCache[symbolRef] = source;
        return source;
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

        if (_patternTileCache.TryGetValue(fillName, out var cached))
            return cached;

        byte[]? result = null;
        try
        {
            var areaFill = AreaFillProvider(fillName);
            if (areaFill?.PatternSymbol is not null)
            {
                var svgContent = SymbolProvider(areaFill.PatternSymbol);
                if (svgContent is not null)
                {
                    var processed = SvgProcessor.Process(svgContent, Palette);
                    result = SkiaSvgRasterizer.RasterizePatternTile(processed, areaFill);
                }
            }
        }
        catch
        {
            // Area fill or symbol not found — skip pattern
        }

        _patternTileCache[fillName] = result;
        return result;
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
        }

        return [.. result];
    }
}
