using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Diagnostic tests for S-101 pattern fill rendering.
/// These tests require external S-101 test datasets and are skipped in CI.
/// Output PNGs are written to /tmp/s101_diag/ for visual inspection.
/// </summary>
public class S101PatternFillDiagnosticTests
{
    private const string DatasetDir =
        "/Users/phoff/Source/iho-ohi/S-101-Test-Datasets/S-101_Test_DataSets/exports/2.0/IHO_V12/S100_ROOT/S-101/DATASET_FILES";

    /// <summary>
    /// Dataset 3 contains overlapping quality of data zones (DQUAL patterns)
    /// that exhibit the crosshatch issue.
    /// </summary>
    private const string TestDataset = "101AA00DS0003.000";

    private const string OutputDir = "/tmp/s101_diag";

    private static bool DatasetExists =>
        File.Exists(Path.Combine(DatasetDir, TestDataset));

    /// <summary>
    /// Dumps all area fill instructions for the test dataset, showing which
    /// features overlap and what patterns they reference. This helps diagnose
    /// whether the crosshatch comes from instruction-level overlap.
    /// </summary>
    [SkippableFact]
    public void DumpAreaFillInstructions()
    {
        Skip.IfNot(DatasetExists, $"Test dataset not found at {DatasetDir}/{TestDataset}");
        Directory.CreateDirectory(OutputDir);

        var (parsed, _, _) = RunLuaPipeline();

        // Filter to area fill instructions only
        var areaFills = parsed.OfType<AreaInstruction>().ToList();

        var lines = new List<string>
        {
            $"Dataset: {TestDataset}",
            $"Total parsed instructions: {parsed.Count}",
            $"Area fill instructions: {areaFills.Count}",
            $"  ColorFills: {areaFills.Count(a => a.FillColor is not null)}",
            $"  PatternFills: {areaFills.Count(a => a.AreaFillReference is not null)}",
            "",
            "=== Area Fill Instructions (sorted by DrawingPriority) ===",
        };

        foreach (var af in areaFills.OrderBy(a => a.DrawingPriority).ThenBy(a => a.FillColor is not null ? 0 : 1))
        {
            lines.Add($"  Feature={af.FeatureReference} Priority={af.DrawingPriority} " +
                       $"Type={(af.FillColor is not null ? "ColorFill" : "Pattern")} " +
                       $"SymbolRef={af.FillColor ?? af.AreaFillReference} " +
                       $"Transparency={af.Transparency?.ToString(CultureInfo.InvariantCulture) ?? "none"} " +
                       $"Plane={af.Plane}");
        }

        // Group by unique feature to show multi-instruction features
        var byFeature = areaFills.GroupBy(a => a.FeatureReference).Where(g => g.Count() > 1);
        lines.Add("");
        lines.Add("=== Features with multiple area fills ===");
        foreach (var group in byFeature)
        {
            lines.Add($"  Feature {group.Key}:");
            foreach (var af in group)
            {
                lines.Add($"    {(af.FillColor is not null ? "ColorFill" : "Pattern")}:{af.FillColor ?? af.AreaFillReference} pri={af.DrawingPriority}");
            }
        }

        // Group by pattern name to show overlap
        var patternGroups = areaFills.Where(a => a.AreaFillReference is not null)
            .GroupBy(a => a.AreaFillReference)
            .OrderByDescending(g => g.Count());
        lines.Add("");
        lines.Add("=== Pattern names and feature counts ===");
        foreach (var g in patternGroups)
        {
            lines.Add($"  {g.Key}: {g.Count()} features ({string.Join(", ", g.Select(a => a.FeatureReference))})");
        }

        var report = string.Join(Environment.NewLine, lines);
        File.WriteAllText(Path.Combine(OutputDir, "area_fill_report.txt"), report);

        // Also write to test output
        Assert.True(areaFills.Count > 0, report);
    }

    /// <summary>
    /// Rasterizes each unique pattern tile to a PNG file for visual inspection.
    /// This directly tests the tile bitmap without any Mapsui involvement.
    /// </summary>
    [SkippableFact]
    public void DumpPatternTiles()
    {
        Skip.IfNot(DatasetExists, $"Test dataset not found at {DatasetDir}/{TestDataset}");
        Directory.CreateDirectory(OutputDir);

        var (parsed, palette, catalogue) = RunLuaPipeline();

        var patternFills = parsed
            .OfType<AreaInstruction>()
            .Where(p => p.AreaFillReference is not null)
            .Select(p => p.AreaFillReference!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dimReport = new List<string> { "=== Pattern Tile Dimensions ===" };

        foreach (var fillName in patternFills)
        {
            try
            {
                var areaFill = catalogue.GetAreaFill(fillName);
                if (areaFill.PatternSymbol is null) continue;

                var svgContent = catalogue.GetSymbol(areaFill.PatternSymbol).SvgContent;
                var processed = SvgProcessor.Process(svgContent, palette);

                // Dump SVG and area fill details
                File.WriteAllText(Path.Combine(OutputDir, $"svg_raw_{fillName}.svg"), svgContent);
                File.WriteAllText(Path.Combine(OutputDir, $"svg_processed_{fillName}.svg"), processed);
                dimReport.Add($"\n--- {fillName} ---");
                dimReport.Add($"  PatternSymbol: {areaFill.PatternSymbol}");
                dimReport.Add($"  V1=({areaFill.V1X}, {areaFill.V1Y}) V2=({areaFill.V2X}, {areaFill.V2Y})");

                // Check SVG bounds
                using var svgObj = Svg.Skia.SKSvg.CreateFromSvg(processed);
                var pic = svgObj?.Picture;
                if (pic is not null)
                {
                    var bounds = pic.CullRect;
                    dimReport.Add($"  SVG CullRect: ({bounds.Left}, {bounds.Top}, {bounds.Right}, {bounds.Bottom}) = {bounds.Width}x{bounds.Height}");
                }

                double tileWidthMm = Math.Abs(areaFill.V1X);
                double tileHeightMm = Math.Abs(areaFill.V2Y);
                bool hasOffset = Math.Abs(areaFill.V2X) > 0.01;
                double totalHeightMm = hasOffset ? tileHeightMm * 2 : tileHeightMm;
                const double ppm = 1.5;
                int tileW = Math.Max(1, (int)Math.Round(tileWidthMm * ppm));
                int tileH = Math.Max(1, (int)Math.Round(totalHeightMm * ppm));
                dimReport.Add($"  tileWidthMm={tileWidthMm} tileHeightMm={tileHeightMm} hasOffset={hasOffset}");
                dimReport.Add($"  Tile pixels: {tileW}x{tileH} (at {ppm} px/mm)");

                var png = SkiaSvgRasterizer.RasterizePatternTile(processed, areaFill);
                if (png is not null)
                {
                    using var tileBmpCheck = SKBitmap.Decode(png);
                    dimReport.Add($"  Actual PNG dimensions: {tileBmpCheck.Width}x{tileBmpCheck.Height}");
                    // Count non-transparent pixels
                    int nonTransparent = 0;
                    for (int y = 0; y < tileBmpCheck.Height; y++)
                    for (int x = 0; x < tileBmpCheck.Width; x++)
                    {
                        if (tileBmpCheck.GetPixel(x, y).Alpha > 0) nonTransparent++;
                    }
                    dimReport.Add($"  Non-transparent pixels: {nonTransparent}/{tileBmpCheck.Width * tileBmpCheck.Height}");

                    File.WriteAllBytes(Path.Combine(OutputDir, $"tile_{fillName}.png"), png);

                    // Also create a 3x3 tiled version for visual tiling inspection
                    using var tileImage = SKBitmap.Decode(png);
                    int tw = tileImage.Width, th = tileImage.Height;
                    using var tiledBitmap = new SKBitmap(tw * 3, th * 3);
                    using var canvas = new SKCanvas(tiledBitmap);
                    canvas.Clear(SKColors.LightBlue); // Simulate the blue background

                    for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        canvas.DrawBitmap(tileImage, col * tw, row * th);
                    }

                    canvas.Flush();
                    using var tiledImage = SKImage.FromBitmap(tiledBitmap);
                    using var tiledData = tiledImage.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(OutputDir, $"tiled3x3_{fillName}.png"),
                        tiledData.ToArray());
                }
            }
            catch (Exception ex)
            {
                dimReport.Add($"  ERROR: {ex.Message}");
                File.WriteAllText(Path.Combine(OutputDir, $"tile_{fillName}_error.txt"), ex.ToString());
            }
        }

        File.WriteAllText(Path.Combine(OutputDir, "tile_dimensions.txt"),
            string.Join(Environment.NewLine, dimReport));

        Assert.True(patternFills.Count > 0, "No pattern fills found in dataset");
    }

    /// <summary>
    /// Renders a full bitmap of the dataset using pure SkiaSharp (no Mapsui),
    /// drawing each area fill instruction on a single canvas. This isolates
    /// whether the crosshatch is a tile/instruction issue vs a Mapsui issue.
    /// </summary>
    [SkippableFact]
    public void RenderAreaFillsToSkiaBitmap()
    {
        Skip.IfNot(DatasetExists, $"Test dataset not found at {DatasetDir}/{TestDataset}");
        Directory.CreateDirectory(OutputDir);

        var (parsed, palette, catalogue) = RunLuaPipeline();

        // Get all features with geometry
        var dataset = S101Dataset.Open(Path.Combine(DatasetDir, TestDataset));
        var vectorSource = new S101VectorSource(dataset);
        var features = vectorSource.GetFeatures();
        var featureGeom = features.ToDictionary(
            f => f.Id.ToString(CultureInfo.InvariantCulture),
            f => f.Coordinates);

        // Compute bounding box
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        foreach (var coords in featureGeom.Values)
        {
            foreach (var (Latitude, Longitude) in coords)
            {
                minLat = Math.Min(minLat, Latitude);
                maxLat = Math.Max(maxLat, Latitude);
                minLon = Math.Min(minLon, Longitude);
                maxLon = Math.Max(maxLon, Longitude);
            }
        }

        // Create bitmap
        int width = 1200, height = 800;
        double latRange = maxLat - minLat;
        double lonRange = maxLon - minLon;
        if (latRange <= 0 || lonRange <= 0) return;

        // Add 10% padding
        minLat -= latRange * 0.1;
        maxLat += latRange * 0.1;
        minLon -= lonRange * 0.1;
        maxLon += lonRange * 0.1;
        latRange = maxLat - minLat;
        lonRange = maxLon - minLon;

        float ScaleX(double lon) => (float)((lon - minLon) / lonRange * width);
        float ScaleY(double lat) => (float)((maxLat - lat) / latRange * height);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x87, 0xCE, 0xEB)); // Light blue like the viewer

        // Sort area fills like the renderer does
        var areaFills = parsed
            .OfType<AreaInstruction>()
            .OrderBy(p => p.Plane == Pipelines.Vector.DisplayPlane.OverRadar ? 1 : 0)
            .ThenBy(p => p.DrawingPriority)
            .ThenBy(p => p.FillColor is not null ? 0 : 1) // Color fills BEFORE patterns
            .ToList();

        // Cache pattern tiles
        var tileCache = new Dictionary<string, SKBitmap?>(StringComparer.OrdinalIgnoreCase);

        int colorFillCount = 0, patternFillCount = 0;

        // --- Phase 1: Draw all color fills ---
        foreach (var af in areaFills.Where(a => a.FillColor is not null))
        {
            if (!featureGeom.TryGetValue(af.FeatureReference, out var coords) || coords.Count < 3)
                continue;

            colorFillCount++;
            using var path = new SKPath();
            path.MoveTo(ScaleX(coords[0].Longitude), ScaleY(coords[0].Latitude));
            for (int i = 1; i < coords.Count; i++)
                path.LineTo(ScaleX(coords[i].Longitude), ScaleY(coords[i].Latitude));
            path.Close();

            SKColor fillColor = SKColors.LightBlue;
            if (af.FillColor is not null && palette.TryResolve(af.FillColor, out var hex))
            {
                fillColor = SKColor.Parse(hex);
            }
            if (af.Transparency.HasValue)
            {
                byte alpha = (byte)(255 * (1.0 - af.Transparency.Value));
                fillColor = fillColor.WithAlpha(alpha);
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = fillColor,
            };
            canvas.DrawPath(path, paint);
        }

        // --- Phase 2: Group pattern fills by (symbolRef, priority) ---
        var patternGroups = new List<(string SymbolRef, int Priority, SKPath CombinedPath)>();
        foreach (var group in areaFills.Where(a => a.AreaFillReference is not null)
            .GroupBy(a => (a.AreaFillReference!, a.DrawingPriority))
            .OrderBy(g => g.Key.DrawingPriority))
        {
            var combinedPath = new SKPath();
            foreach (var af in group)
            {
                if (!featureGeom.TryGetValue(af.FeatureReference, out var coords) || coords.Count < 3)
                    continue;

                var polyPath = new SKPath();
                polyPath.MoveTo(ScaleX(coords[0].Longitude), ScaleY(coords[0].Latitude));
                for (int i = 1; i < coords.Count; i++)
                    polyPath.LineTo(ScaleX(coords[i].Longitude), ScaleY(coords[i].Latitude));
                polyPath.Close();
                combinedPath.AddPath(polyPath);
                polyPath.Dispose();
            }
            patternGroups.Add((group.Key.Item1, group.Key.DrawingPriority, combinedPath));
        }

        // --- Phase 3: Clip lower-priority patterns by higher-priority areas ---
        // Walk from highest priority down, building a union of higher-priority areas
        SKPath? higherPriorityAreas = null;
        var clippedPaths = new SKPath[patternGroups.Count];
        for (int i = patternGroups.Count - 1; i >= 0; i--)
        {
            var (_, _, groupPath) = patternGroups[i];
            if (higherPriorityAreas is not null)
            {
                var clipped = groupPath.Op(higherPriorityAreas, SKPathOp.Difference);
                clippedPaths[i] = clipped ?? groupPath;
            }
            else
            {
                clippedPaths[i] = groupPath;
            }

            // Union this group's area into the higher-priority accumulator
            if (higherPriorityAreas is null)
            {
                higherPriorityAreas = new SKPath(groupPath);
            }
            else
            {
                var union = higherPriorityAreas.Op(groupPath, SKPathOp.Union);
                if (union is not null)
                {
                    higherPriorityAreas.Dispose();
                    higherPriorityAreas = union;
                }
            }
        }

        // --- Phase 4: Draw clipped pattern fills ---
        for (int i = 0; i < patternGroups.Count; i++)
        {
            var (symbolRef, _, _) = patternGroups[i];
            var path = clippedPaths[i];
            if (path.IsEmpty) continue;

            patternFillCount++;
            if (!tileCache.TryGetValue(symbolRef, out var tileBitmap))
            {
                try
                {
                    var areaFill = catalogue.GetAreaFill(symbolRef);
                    if (areaFill.PatternSymbol is not null)
                    {
                        var svgContent = catalogue.GetSymbol(areaFill.PatternSymbol).SvgContent;
                        var processed = SvgProcessor.Process(svgContent, palette);
                        var png = SkiaSvgRasterizer.RasterizePatternTile(processed, areaFill);
                        if (png is not null)
                            tileBitmap = SKBitmap.Decode(png);
                    }
                }
                catch { }
                tileCache[symbolRef] = tileBitmap;
            }

            if (tileBitmap is not null)
            {
                using var tileImage = SKImage.FromBitmap(tileBitmap);
                using var shader = tileImage.ToShader(
                    SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = shader,
                };
                canvas.Save();
                canvas.ClipPath(path);
                canvas.DrawRect(path.Bounds, paint);
                canvas.Restore();
            }
        }

        // Dispose pattern paths
        foreach (var pg in patternGroups) pg.CombinedPath.Dispose();
        foreach (var cp in clippedPaths) { if (!patternGroups.Any(pg => ReferenceEquals(pg.CombinedPath, cp))) cp?.Dispose(); }
        higherPriorityAreas?.Dispose();

        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(OutputDir, "skia_area_fills.png"), data.ToArray());

        // Dispose tile cache
        foreach (var tb in tileCache.Values)
            tb?.Dispose();

        File.WriteAllText(Path.Combine(OutputDir, "skia_render_summary.txt"),
            $"ColorFills: {colorFillCount}, PatternFills: {patternFillCount}, " +
            $"Unique patterns: {tileCache.Count}");

        Assert.True(colorFillCount + patternFillCount > 0);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private (List<DrawingInstruction> Parsed, ColorPalette Palette, S101PortrayalCatalogue Catalogue) RunLuaPipeline()
    {
        var datasetPath = Path.Combine(DatasetDir, TestDataset);
        var dataset = S101Dataset.Open(datasetPath);

        // Load the bundled S-101 feature catalogue
        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("S-101 feature catalogue not found.");
        var fc = FeatureCatalogueReader.Read(fcStream);

        // Create portrayal catalogue from bundled assets
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = PortrayalCatalogueProvider.OpenAsync(pcSource).GetAwaiter().GetResult();
        var luaEngine = new MoonSharpLuaEngine();
        var catalogue = new S101PortrayalCatalogue(provider, luaEngine);
        catalogue.SwitchPalette(PaletteType.Day);
        var palette = catalogue.ActivePalette;

        // Run Lua portrayal
        var portrayal = new S101LuaRuleExecutor(luaEngine, dataset, provider, fc);
        var emitted = portrayal.ExecuteRaw(MarinerSettings.Default);

        // Dump raw emit strings for key features
        var rawLines = new List<string> { "=== Raw Lua Emit Strings ===" };
        var interestingFeatures = new HashSet<string> { "2", "3", "8", "56", "58" };
        foreach (var e in emitted)
        {
            if (interestingFeatures.Contains(e.FeatureRef))
            {
                rawLines.Add($"\nFeature {e.FeatureRef}:");
                rawLines.Add(e.InstructionString);
            }
        }

        // Count emitted instructions per feature ref
        var emitCounts = emitted.GroupBy(e => e.FeatureRef)
            .Where(g => g.Count() > 1)
            .Select(g => $"  Feature {g.Key}: {g.Count()} emits, identical={g.Select(x => x.InstructionString).Distinct().Count() == 1}")
            .ToList();
        rawLines.Add($"\n=== Total emits: {emitted.Count} ===");
        rawLines.Add($"=== Features with multiple emits: {emitCounts.Count} ===");
        rawLines.AddRange(emitCounts);

        // Also check feature record duplicates
        var dataset2 = S101Dataset.Open(Path.Combine(DatasetDir, TestDataset));
        var doc = dataset2.Document;
        var docFeatureIds = doc.Features.Select(f => f.RecordId).ToList();
        var docDupIds = docFeatureIds.GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => $"  RecordId {g.Key}: {g.Count()} FRID records")
            .ToList();
        rawLines.Add($"\n=== Raw doc.Features count: {doc.Features.Length} ===");
        rawLines.Add($"=== Unique RecordIds: {docFeatureIds.Distinct().Count()} ===");
        rawLines.Add($"=== Duplicate RecordIds: {docDupIds.Count} ===");
        rawLines.AddRange(docDupIds);

        var vectorSource2 = new S101VectorSource(dataset2);
        var allFeatures = vectorSource2.GetFeatures();
        var featureIdCounts = allFeatures.GroupBy(f => f.Id)
            .Where(g => g.Count() > 1)
            .Select(g => $"  Feature ID {g.Key}: {g.Count()} records")
            .ToList();
        rawLines.Add($"\n=== Duplicate feature IDs from VectorSource: {featureIdCounts.Count} ===");
        rawLines.AddRange(featureIdCounts);

        File.WriteAllText(Path.Combine(OutputDir, "raw_instructions.txt"),
            string.Join(Environment.NewLine, rawLines));

        // Parse
        var parsed = new List<DrawingInstruction>();
        foreach (var e in emitted)
        {
            parsed.AddRange(DrawingInstructionParser.Parse(e.FeatureRef, e.InstructionString));
        }

        return (parsed, palette, catalogue);
    }
}
