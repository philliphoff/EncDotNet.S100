using System.Globalization;
using EncDotNet.S100.Datasets.S101;
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
/// Renders S-101 parsed drawing instructions into a Mapsui <see cref="ILayer"/>
/// by resolving feature geometry from the dataset, projecting to EPSG:3857,
/// and applying styles derived from the instruction properties.
/// </summary>
public sealed class MapsuiS101VectorRenderer
{
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
    /// Renders a set of parsed drawing instructions for the given dataset
    /// into a Mapsui layer.
    /// </summary>
    public ILayer Render(
        IReadOnlyList<ParsedDrawingInstruction> instructions,
        S101Dataset dataset)
    {
        // Ensure the custom pattern fill renderer is registered before Mapsui
        // encounters any AnchoredPatternFillStyle instances.
        AnchoredPatternFillRenderer.Register();

        // 1. Resolve all feature geometries from the dataset
        var vectorSource = new S101VectorSource(dataset);
        var features = vectorSource.GetFeatures();
        var featureGeometry = new Dictionary<long, (GeometryType Type, IReadOnlyList<(double Lat, double Lon)> Coords)>();
        foreach (var f in features)
        {
            featureGeometry[f.Id] = (f.GeometryType, f.Coordinates);
        }

        // 2. Sort instructions by rendering order: areas first, then lines, then points/text
        //    Within same type, sort by DrawingPriority
        var sorted = instructions
            .OrderBy(i => i.DisplayPlane == "OverRadar" ? 1 : 0)
            .ThenBy(i => i.Type switch
            {
                InstructionType.AreaFill => 0,
                InstructionType.Line => 1,
                InstructionType.Point => 2,
                InstructionType.Text => 3,
                _ => 4,
            })
            .ThenBy(i => i.DrawingPriority)
            .ToList();

        // 3. Build color resolver from palette
        var resolveColor = BuildColorResolver(Palette);

        // 3a. Merge consecutive SAFCON point instructions into text labels
        var merged = MergeSafconLabels(sorted);

        // 4. Convert each instruction to a Mapsui feature.
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
        foreach (var instruction in merged)
        {
            if (instruction.Type == InstructionType.AreaFill && !instruction.IsColorFill)
                featuresWithPatterns.Add(instruction.FeatureRef);
        }

        // Collect opaque color fill polygons from features that do NOT also
        // produce a pattern fill. These represent areas (such as land) where
        // patterns from other features should not be visible.
        var nonPatternedColorFillPolygons = new List<Polygon>();

        foreach (var instruction in merged)
        {
            if (!long.TryParse(instruction.FeatureRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var featureId))
                continue;

            if (!featureGeometry.TryGetValue(featureId, out var geom))
                continue;

            if (geom.Coords.Count == 0)
                continue;

            // Defer pattern fills for merging
            if (instruction.Type == InstructionType.AreaFill && !instruction.IsColorFill)
            {
                var tilePng = GetPatternTilePng(instruction.SymbolRef);
                if (tilePng is not null)
                {
                    var polygon = CreatePolygonFromCoords(geom.Coords);
                    if (polygon is not null)
                    {
                        // Find existing entry with the same tile and priority, or create a new one
                        var existing = patternEntries.Find(e =>
                            ReferenceEquals(e.TilePng, tilePng) && e.Priority == instruction.DrawingPriority);
                        if (existing.TilePng is not null)
                        {
                            existing.Polygons.Add(polygon);
                        }
                        else
                        {
                            patternEntries.Add((tilePng, instruction.DrawingPriority, new List<Polygon> { polygon }));
                        }
                    }
                }
                continue;
            }

            // Track non-patterned color fills (e.g. land areas) for pattern clipping
            if (instruction.Type == InstructionType.AreaFill && instruction.IsColorFill
                && !featuresWithPatterns.Contains(instruction.FeatureRef))
            {
                var polygon = CreatePolygonFromCoords(geom.Coords);
                if (polygon is not null)
                    nonPatternedColorFillPolygons.Add(polygon);
            }

            var mapFeature = CreateMapFeature(instruction, geom.Type, geom.Coords, resolveColor, this);
            if (mapFeature is not null)
            {
                mapFeatures.Add(mapFeature);

                // Track where color fills end so we can insert patterns after them
                if (instruction.Type == InstructionType.AreaFill && instruction.IsColorFill)
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
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
        switch (instruction.Type)
        {
            case InstructionType.AreaFill:
                return CreateAreaFeature(instruction, geomType, coords, resolveColor, renderer);

            case InstructionType.Line:
                return CreateLineFeature(instruction, geomType, coords, resolveColor);

            case InstructionType.Point:
                return CreatePointFeature(instruction, coords, resolveColor, renderer);

            case InstructionType.Text:
                return CreateTextFeature(instruction, coords, resolveColor, renderer);

            default:
                return null;
        }
    }

    private static IFeature? CreateAreaFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
        var polygon = CreatePolygonFromCoords(coords);
        if (polygon is null)
            return null;

        if (instruction.IsColorFill)
        {
            // Solid color fill
            var fillColor = resolveColor(instruction.SymbolRef);

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
        else
        {
            // Pattern fill via AreaFillReference, using a custom style so the
            // pattern is anchored to the geometry and moves during panning.
            var tilePng = renderer.GetPatternTilePng(instruction.SymbolRef);
            if (tilePng is null)
                return null;

            var style = new AnchoredPatternFillStyle
            {
                TilePng = tilePng,
            };

            var feature = new GeometryFeature(polygon);
            feature.Styles.Add(style);
            return feature;
        }
    }

    private static IFeature? CreateLineFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor)
    {
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
        ParsedDrawingInstruction instruction,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
        if (coords.Count == 0)
            return null;

        // Use first coordinate as the point location
        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var feature = new PointFeature(mx, my);

        // Try to render with an actual SVG symbol
        var symbolRef = instruction.SymbolRef;
        var svgSource = renderer.GetSymbolSource(symbolRef);
        if (svgSource is not null)
        {
            var style = new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
            };
            style.SymbolScale = 0.6 * instruction.ScaleFactor * renderer.SymbolScale;
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
                SymbolScale = 0.15 * instruction.ScaleFactor * renderer.SymbolScale,
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
        ParsedDrawingInstruction instruction,
        IReadOnlyList<(double Lat, double Lon)> coords,
        Func<string?, MapsuiColor> resolveColor,
        MapsuiS101VectorRenderer renderer)
    {
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

    private static Polygon? CreatePolygonFromCoords(IReadOnlyList<(double Lat, double Lon)> coords)
    {
        if (coords.Count < 3)
            return null;

        var projected = ProjectCoordinates(coords);

        // Close the ring if not already closed
        var ringCoords = new List<Coordinate>(projected);
        if (ringCoords.Count > 0 && !ringCoords[0].Equals2D(ringCoords[^1]))
            ringCoords.Add(new Coordinate(ringCoords[0].X, ringCoords[0].Y));

        if (ringCoords.Count < 4)
            return null;

        var ring = new LinearRing(ringCoords.ToArray());
        return new Polygon(ring);
    }

    private static List<Coordinate> ProjectCoordinates(IReadOnlyList<(double Lat, double Lon)> coords)
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

    // ── SAFCON contour label merging ───────────────────────────────────
    //
    // The S-101 Lua portrayal emits depth contour labels as sequences of
    // PointInstruction:SAFCONxy symbols, where each symbol represents a
    // single positioned digit glyph in an SVG composition. Since we don't
    // yet render SVG symbols, we decode the SAFCON sequence back into a
    // depth text string and emit a single text instruction instead.
    //
    // SAFCON encoding (from SAFCON01.lua):
    //   Row 0: single/middle digit    Row 5: fractional (depth 10–30)
    //   Row 1: units of 2-digit       Row 6: fractional (depth <10)
    //   Row 2: tens of 2-digit        Row 7: last digit of 4/5-digit
    //   Row 3: first of 4-digit       Row 8: first of 3-digit
    //   Row 4: first of 5-digit       Row 9: third of 3-digit

    private static List<ParsedDrawingInstruction> MergeSafconLabels(List<ParsedDrawingInstruction> instructions)
    {
        var result = new List<ParsedDrawingInstruction>(instructions.Count);

        // Group consecutive SAFCON points by feature
        var i = 0;
        while (i < instructions.Count)
        {
            var instr = instructions[i];

            if (instr.Type == InstructionType.Point && IsSafconSymbol(instr.SymbolRef))
            {
                // Collect all consecutive SAFCON instructions for this feature
                var safcons = new List<ParsedDrawingInstruction> { instr };
                var j = i + 1;
                while (j < instructions.Count &&
                       instructions[j].Type == InstructionType.Point &&
                       instructions[j].FeatureRef == instr.FeatureRef &&
                       IsSafconSymbol(instructions[j].SymbolRef))
                {
                    safcons.Add(instructions[j]);
                    j++;
                }

                // Decode the SAFCON sequence into a depth string
                var depthText = DecodeSafconSequence(safcons);

                // Emit a synthetic text instruction preserving line placement
                result.Add(new ParsedDrawingInstruction
                {
                    FeatureRef = instr.FeatureRef,
                    Type = InstructionType.Text,
                    Text = depthText,
                    ViewingGroup = instr.ViewingGroup,
                    DrawingPriority = instr.DrawingPriority,
                    DisplayPlane = instr.DisplayPlane,
                    FontSize = 10,
                    FontColor = "DEPCN",
                    LinePlacementPosition = instr.LinePlacementPosition,
                    ScaleMinimum = instr.ScaleMinimum,
                    ScaleMaximum = instr.ScaleMaximum,
                });

                i = j;
            }
            else
            {
                result.Add(instr);
                i++;
            }
        }

        return result;
    }

    private static bool IsSafconSymbol(string? symbolRef)
    {
        return symbolRef is not null &&
               symbolRef.StartsWith("SAFCON", StringComparison.Ordinal) &&
               symbolRef.Length == 8;
    }

    private static string DecodeSafconSequence(List<ParsedDrawingInstruction> safcons)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var instr in safcons)
        {
            var name = instr.SymbolRef!;
            // SAFCONxy — x is row (position type), y is digit
            var row = name[6] - '0';
            var digit = name[7];

            if (row == 5 || row == 6)
            {
                // Fractional digit — prepend decimal point
                sb.Append('.');
            }

            sb.Append(digit);
        }

        return sb.ToString();
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
