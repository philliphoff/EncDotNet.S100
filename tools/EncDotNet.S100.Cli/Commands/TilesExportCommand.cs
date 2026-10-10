using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tiles export</c> renders one or more S-100 datasets as an XYZ Web-Mercator
/// raster tile pyramid, written as a directory of <c>{z}/{x}/{y}</c> images, a
/// PMTiles archive or an MBTiles database, for use in web maps such as MapLibre, Leaflet and
/// OpenLayers.
/// </summary>
/// <remarks>
/// The datasets are opened and portrayed once; every tile is then painted from
/// the same prepared composite (<see cref="HeadlessTileRenderer"/>), with each
/// zoom level drawing only what is visible at its scale. Coverage products
/// (S-102, S-104, S-111) are portrayed again for each zoom level so their
/// sampling and arrow density suit its resolution. The input grammar matches
/// <c>s100 render</c>: one dataset, several <c>--layer</c> datasets, or an
/// exchange set.
/// </remarks>
internal sealed class TilesExportCommand : Command<TilesExportCommand.Settings>
{
    /// <summary>Tile count above which <c>--yes</c> (or an interactive confirmation) is required.</summary>
    internal const long ConfirmationThreshold = 100_000;

    /// <summary>The deepest zoom a default (derived) zoom range reaches.</summary>
    private const int DefaultZoomCeiling = 18;

    /// <summary>The widest coverage portrayal viewport per axis, in pixels.</summary>
    private const int MaxCoverageViewportPixels = 1 << 20;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("The dataset to tile, or an exchange set (a directory containing a CATALOG.XML, a CATALOG.XML, or an exchange-set .zip). Omit when using --layer or --exchange-set.")]
        public string? Input { get; init; }

        [CommandOption("--layer <PATH>")]
        [Description("Add a dataset as a composite layer (repeatable). The S-98 authority orders layers by display plane, so --layer order is only a within-plane tiebreak.")]
        public string[] Layers { get; init; } = [];

        [CommandOption("--exchange-set|--from <PATH>")]
        [Description("Tile every discoverable dataset in an exchange set: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one. Applies no S-101 updates; data-protected and unsupported datasets are skipped with a warning. Mutually exclusive with --layer.")]
        public string? ExchangeSet { get; init; }

        [CommandOption("--only <SPECS>")]
        [Description("Exchange-set form only: restrict tiling to a comma-separated list of product specifications (e.g. --only S101,S102).")]
        public string? Only { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output path: a directory for --container xyz, a .pmtiles file for --container pmtiles, or a .mbtiles file for --container mbtiles. An existing archive file is replaced. Required.")]
        public string? Output { get; init; }

        [CommandOption("--container <KIND>")]
        [Description("Tile container: xyz (a {z}/{x}/{y}.<ext> directory plus a tiles.json TileJSON file), pmtiles (one PMTiles v3 archive, static-hostable) or mbtiles (one MBTiles 1.3 SQLite database, for tile servers). Default: from the output extension (.pmtiles or .mbtiles), otherwise xyz.")]
        public string? Container { get; init; }

        [CommandOption("--min-zoom <ZOOM>")]
        [Description("Lowest zoom level to write (0-24). Default: derived from the datasets' minimum display scale, or from their extent.")]
        public int? MinZoom { get; init; }

        [CommandOption("--max-zoom <ZOOM>")]
        [Description("Highest zoom level to write (0-24). Default: one level past the datasets' finest compilation scale, or six levels past the minimum zoom, capped at 18.")]
        public int? MaxZoom { get; init; }

        [CommandOption("--bbox <BBOX>")]
        [Description("Limit the tiles to a WGS-84 bounding box 'minLon,minLat,maxLon,maxLat' (e.g. --bbox -1.5,50.0,-1.0,50.5). Default: the union extent of the datasets.")]
        public string? BoundingBox { get; init; }

        [CommandOption("--tile-size <PIXELS>")]
        [Description("Tile image size: 256 (default) or 512. 512 renders the same 256-pixel grid at twice the pixel density (\"@2x\" tiles for high-DPI screens); use tileSize 256 for the source in the web map.")]
        [DefaultValue(256)]
        public int TileSize { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Tile encoding: png (default), jpeg (jpg), or webp. jpeg has no transparency, so set --background.")]
        [DefaultValue("png")]
        public string Format { get; init; } = "png";

        [CommandOption("--quality <QUALITY>")]
        [Description("Encoder quality (1-100) for jpeg and webp. Ignored for png. Default 90.")]
        [DefaultValue(90)]
        public int Quality { get; init; }

        [CommandOption("--palette <PALETTE>")]
        [Description("Colour palette: day, dusk, or night (default day).")]
        [DefaultValue("day")]
        public string Palette { get; init; } = "day";

        [CommandOption("--symbol-scale <FACTOR>")]
        [Description("Symbol scale factor (default 1.0).")]
        [DefaultValue(1.0)]
        public double SymbolScale { get; init; }

        [CommandOption("--text-scale <FACTOR>")]
        [Description("Text scale factor (default 1.0).")]
        [DefaultValue(1.0)]
        public double TextScale { get; init; }

        [CommandOption("--time-step <INDEX>")]
        [Description("Zero-based time-step index for time-series datasets (S-104/S-111). Default 0. One tile set holds one time step.")]
        [DefaultValue(0)]
        public int TimeStep { get; init; }

        [CommandOption("--background <COLOR>")]
        [Description("Tile background as a hex string (e.g. #FFFFFF or #80FFFFFF). Default transparent, so the tiles overlay a basemap.")]
        public string? Background { get; init; }

        [CommandOption("--no-text")]
        [Description("Suppress text/label drawing instructions. Equivalent to --hide text.")]
        [DefaultValue(false)]
        public bool NoText { get; init; }

        [CommandOption("--hide <CATEGORIES>")]
        [Description("Comma-separated list of drawing-instruction categories to suppress: text, points, lines, areas. Combines additively with --no-text.")]
        public string? Hide { get; init; }

        [CommandOption("--basemap <MODE>")]
        [Description("Draw a basemap beneath the chart data: none (default) or offline (the bundled Natural Earth 1:10m land layer, public domain).")]
        [DefaultValue("none")]
        public string Basemap { get; init; } = "none";

        [CommandOption("--display-mode <MODE>")]
        [Description("Select the S-411 sea-ice portrayal display mode (S-411 only): ice-concentration (default), ice-sod or ice-navigational (PROVISIONAL preview, not a POLARIS/RIO computation).")]
        public string? DisplayMode { get; init; }

        [CommandOption("--skip-empty")]
        [Description("Do not write tiles on which nothing was drawn (every pixel is the background). Web maps show a missing tile as empty.")]
        [DefaultValue(false)]
        public bool SkipEmpty { get; init; }

        [CommandOption("--metatile <TILES>")]
        [Description("Render blocks of N x N tiles at once and cut them apart (1-16, default 4). Larger blocks are faster for dense data and use more memory.")]
        [DefaultValue(4)]
        public int Metatile { get; init; }

        [CommandOption("--parallel <WORKERS>")]
        [Description("Number of blocks rendered at once. Default: the processor count.")]
        public int? Parallel { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Write the tile set even when it holds more than 100000 tiles, without asking.")]
        [DefaultValue(false)]
        public bool Yes { get; init; }

        [CommandOption("--no-updates")]
        [Description("Do not apply sibling S-101 sequential updates (.001, .002, …) found alongside an .000 base cell given positionally or with --layer. The exchange-set form never applies updates.")]
        [DefaultValue(false)]
        public bool NoUpdates { get; init; }

        [CommandOption("--debug")]
        [Description("Show full stack traces on error, and surface host/Lua portrayal diagnostics on stderr.")]
        public bool Debug { get; init; }

        public bool IsComposite => Layers.Length > 0;

        public bool IsExplicitExchangeSet => !string.IsNullOrWhiteSpace(ExchangeSet);

        public bool IsExchangeSet =>
            IsExplicitExchangeSet
            || (!IsComposite && !string.IsNullOrWhiteSpace(Input) && ExchangeSetInput.LooksLikeExchangeSet(Input));

        public string? ExchangeSetSource => IsExplicitExchangeSet ? ExchangeSet : Input;

        public override ValidationResult Validate()
        {
            if (IsComposite && IsExplicitExchangeSet)
                return ValidationResult.Error("--layer and --exchange-set/--from cannot be combined. Use one or the other.");

            if ((IsComposite || IsExplicitExchangeSet) && !string.IsNullOrWhiteSpace(Input))
                return ValidationResult.Error("With --layer or --exchange-set, do not also pass a positional dataset.");

            if (IsExchangeSet)
            {
                if (!ExchangeSetInput.LooksLikeExchangeSet(ExchangeSetSource!))
                    return ValidationResult.Error(
                        $"Not an S-100 exchange set (expected a directory with CATALOG.XML, a CATALOG.XML, or an exchange-set .zip): {ExchangeSetSource}");
            }
            else if (IsComposite)
            {
                foreach (var layer in Layers)
                {
                    if (string.IsNullOrWhiteSpace(layer))
                        return ValidationResult.Error("A --layer value cannot be empty.");
                    if (!File.Exists(layer))
                        return ValidationResult.Error($"Layer file not found: {layer}");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Input))
                    return ValidationResult.Error("A dataset or exchange-set path is required (or use --layer / --exchange-set).");
                if (!File.Exists(Input))
                    return ValidationResult.Error($"Dataset file not found: {Input}");
            }

            if (Only is not null && !IsExchangeSet)
                return ValidationResult.Error("--only applies only to the exchange-set form.");
            if (Only is not null && !RenderCommand.TryParseOnlySpecs(Only, out _))
                return ValidationResult.Error("--only must be a comma-separated list of product specifications (e.g. S101,S102).");

            if (string.IsNullOrWhiteSpace(Output))
                return ValidationResult.Error("An output path is required (-o|--output).");
            if (!TryResolveContainer(Container, Output, out _))
                return ValidationResult.Error($"Unknown --container '{Container}'. Use xyz, pmtiles or mbtiles.");
            var parent = Path.GetDirectoryName(Path.GetFullPath(Output));
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                return ValidationResult.Error($"Output directory does not exist: {parent}");

            if (MinZoom is < 0 or > XyzTileGrid.MaxZoom || MaxZoom is < 0 or > XyzTileGrid.MaxZoom)
                return ValidationResult.Error($"--min-zoom and --max-zoom must be between 0 and {XyzTileGrid.MaxZoom}.");
            if (MinZoom is { } min && MaxZoom is { } max && min > max)
                return ValidationResult.Error("--min-zoom must not exceed --max-zoom.");

            if (BoundingBox is not null)
            {
                if (!CompositeViewportBuilder.TryParseDoubles(BoundingBox, 4, out var bb))
                    return ValidationResult.Error("--bbox must be four numbers: minLon,minLat,maxLon,maxLat.");
                if (bb[0] >= bb[2] || bb[1] >= bb[3])
                    return ValidationResult.Error("--bbox requires minLon < maxLon and minLat < maxLat.");
            }

            if (TileSize is not (256 or 512))
                return ValidationResult.Error("--tile-size must be 256 or 512.");
            if (!TryParseImageFormat(Format, out _))
                return ValidationResult.Error($"Unknown --format '{Format}'. Use png, jpeg, or webp.");
            if (Quality is < 1 or > 100)
                return ValidationResult.Error("--quality must be between 1 and 100.");
            if (!RenderCommand.TryParsePalette(Palette, out _))
                return ValidationResult.Error($"Unknown palette '{Palette}'. Use day, dusk, or night.");
            if (TimeStep < 0)
                return ValidationResult.Error("--time-step must be zero or greater.");
            if (Background is not null && !RenderCommand.TryParseHexColor(Background, out _))
                return ValidationResult.Error($"Invalid --background colour '{Background}'.");
            if (Hide is not null && !RenderCommand.TryParseHideCategories(Hide, out _, out var badToken))
                return ValidationResult.Error(
                    $"Invalid --hide value '{badToken}'. Use a comma-separated list of: text, points, lines, areas.");
            if (!RenderCommand.TryParseBasemap(Basemap, out _))
                return ValidationResult.Error($"Invalid --basemap value '{Basemap}'. Use none or offline.");
            if (DisplayMode is not null && !RenderCommand.TryParseDisplayMode(DisplayMode, out _))
                return ValidationResult.Error(
                    $"Invalid --display-mode value '{DisplayMode}'. Use ice-concentration, ice-sod or ice-navigational.");
            if (Metatile is < 1 or > 16)
                return ValidationResult.Error("--metatile must be between 1 and 16.");
            if (Parallel is < 1)
                return ValidationResult.Error("--parallel must be at least 1.");

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        ExchangeSetLayerResolution? resolution = null;
        var processors = new List<IDatasetProcessor>();
        try
        {
            IReadOnlyList<(string Path, string Spec)> inputs;
            bool applyUpdates = !settings.NoUpdates;
            if (settings.IsExchangeSet)
            {
                IReadOnlySet<string>? only = null;
                if (settings.Only is not null && RenderCommand.TryParseOnlySpecs(settings.Only, out var specs))
                    only = specs;

                resolution = ExchangeSetLayerResolution.Resolve(settings.ExchangeSetSource!, only);
                foreach (var warning in resolution.Warnings)
                    Console.Error.WriteLine(warning);
                inputs = resolution.Layers.Select(l => (l.Path, l.Spec)).ToList();
                applyUpdates = false;
            }
            else
            {
                var paths = settings.IsComposite ? settings.Layers : [settings.Input!];
                var resolved = new List<(string, string)>();
                foreach (var path in paths)
                {
                    var spec = DatasetPipelineFactory.DetectProductSpec(path);
                    if (spec is null)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[red]Could not detect an S-100 product specification for:[/] {path}");
                        return 2;
                    }

                    resolved.Add((path, spec));
                }

                inputs = resolved;
            }

            if (inputs.Count == 0)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]No renderable datasets were found in:[/] {settings.ExchangeSetSource}");
                return 2;
            }

            if (!string.IsNullOrWhiteSpace(settings.DisplayMode)
                && !inputs.Any(i => i.Spec.Equals("S-411", StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine("[red]--display-mode is only supported for S-411 sea-ice datasets; none of the inputs is S-411.[/]");
                return 2;
            }

            foreach (var (path, spec) in inputs)
            {
                var processor = DatasetProcessorLoader.Create(factory, spec, path, noUpdates: !applyUpdates);
                processors.Add(processor);
                if (processor.VersionAssessment?.IsWarning == true)
                    Console.Error.WriteLine(processor.VersionAssessment.BuildMessage());
                if (processor is not (IVectorPortrayalSource or ICoveragePortrayalSource))
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Tiling is not supported for {spec}:[/] {path}");
                    return 3;
                }
            }

            return Run(settings, processors, inputs.Select(i => i.Spec).ToList());
        }
        catch (Exception ex)
        {
            return RenderCommand.HandleException(ex, settings.Debug);
        }
        finally
        {
            foreach (var processor in processors)
                (processor as IDisposable)?.Dispose();
            resolution?.Dispose();
            catalogueManager.Dispose();
        }
    }

    private static int Run(Settings settings, IReadOnlyList<IDatasetProcessor> processors, IReadOnlyList<string> specs)
    {
        RenderCommand.TryParsePalette(settings.Palette, out var palette);
        RenderCommand.TryParseBasemap(settings.Basemap, out var basemap);
        RenderCommand.TryParseDisplayMode(settings.DisplayMode, out var displayModeId);
        TryParseImageFormat(settings.Format, out var format);
        TryResolveContainer(settings.Container, settings.Output!, out var container);
        var hidden = ResolveHiddenCategories(settings);
        var background = settings.Background is not null && RenderCommand.TryParseHexColor(settings.Background, out var bg)
            ? bg
            : RgbaColor.Transparent;

        RenderContext ContextFor(IDatasetProcessor processor, Viewport? viewport) =>
            RenderContextBuilder.Build(
                processor, palette, settings.SymbolScale, settings.TextScale, settings.TimeStep, hidden,
                displayModeId: displayModeId, viewport: viewport);

        // Vector products portray once, independent of the viewport; coverage
        // products portray here without a viewport (the full grid) to find the
        // extent, and again for each zoom level below.
        var stopwatch = Stopwatch.StartNew();
        var inputs = new HeadlessCompositeInput[processors.Count];
        var vectorResults = new List<VectorPortrayalResult>();
        bool hasCoverage = false;
        for (int i = 0; i < processors.Count; i++)
        {
            var processor = processors[i];
            if (processor is IVectorPortrayalSource vectorSource)
            {
                var result = vectorSource.BuildVectorPortrayalAsync(ContextFor(processor, null)).GetAwaiter().GetResult();
                vectorResults.Add(result);
                inputs[i] = HeadlessCompositeInput.ForVector(result);
            }
            else
            {
                var coverageSource = (ICoveragePortrayalSource)processor;
                var result = coverageSource.BuildCoveragePortrayalAsync(ContextFor(processor, null)).GetAwaiter().GetResult();
                inputs[i] = HeadlessCompositeInput.ForCoverage(result);
                hasCoverage = true;
            }
        }

        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());
        var compositeOptions = new HeadlessCompositeOptions
        {
            Background = background,
            Mariner = MarinerSettings.Default,
            HiddenCategories = hidden,
            Basemap = basemap,
            HonorScaleVisibility = true,
            EnableSeamWrap = false,
        };
        var scene = compositor.Prepare(inputs, compositeOptions);

        if (!TryResolveArea(settings, scene, out var area))
        {
            AnsiConsole.MarkupLine("[red]The datasets have no geometry to tile; pass --bbox to tile an explicit area.[/]");
            return 2;
        }

        double referenceLatitude = WebMercator.ToLonLat(0, (area.MinY + area.MaxY) / 2.0).Latitude;
        var (minZoom, maxZoom) = ResolveZoomRange(settings, vectorResults, area, referenceLatitude);

        var tileOptions = new HeadlessTileOptions
        {
            PixelRatio = settings.TileSize / XyzTileGrid.TileSize,
            ReferenceLatitude = referenceLatitude,
            Background = background,
        };

        // Plan every zoom level first, so the size is known before any work.
        var plans = new List<(int Zoom, IReadOnlyList<TileBlock> Blocks)>();
        long totalTiles = 0;
        for (int zoom = minZoom; zoom <= maxZoom; zoom++)
        {
            var columns = XyzTileGrid.Columns(zoom, area.MinX, area.MaxX);
            var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
            var blocks = HeadlessTileRenderer.PlanBlocks(zoom, columns, firstRow, lastRow, settings.Metatile);
            plans.Add((zoom, blocks));
            totalTiles += blocks.Sum(b => (long)b.TileCount);
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Tiling {specs.Count} dataset(s) ({string.Join(", ", specs.Distinct())}): zoom {minZoom}–{maxZoom}, {totalTiles:N0} tile(s).[/]");

        if (totalTiles > ConfirmationThreshold && !settings.Yes && !Confirm(totalTiles))
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]This tile set holds {totalTiles:N0} tiles (more than {ConfirmationThreshold:N0}).[/] Narrow --bbox or the zoom range, or pass --yes to write it anyway.");
            return 2;
        }

        var outputPath = settings.Output!;
        using ITileSink sink = container switch
        {
            TileContainer.PmTiles => new PmTilesTileSink(outputPath),
            TileContainer.MbTiles => new MbTilesTileSink(outputPath),
            _ => new XyzDirectoryTileSink(outputPath, format),
        };

        var encodeFormat = format switch
        {
            TileImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            TileImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };

        long written = 0;
        long skipped = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = settings.Parallel ?? Environment.ProcessorCount,
        };

        foreach (var (zoom, blocks) in plans)
        {
            var zoomScene = hasCoverage
                ? compositor.Prepare(PortrayCoverageForZoom(processors, inputs, ContextFor, area, zoom, tileOptions), compositeOptions)
                : scene;
            var renderer = new HeadlessTileRenderer(zoomScene, tileOptions);
            long zoomWritten = 0;

            System.Threading.Tasks.Parallel.ForEach(blocks, parallelOptions, block =>
            {
                var tiles = renderer.Render(block);
                try
                {
                    foreach (var tile in tiles)
                    {
                        if (settings.SkipEmpty && tile.IsEmpty)
                        {
                            Interlocked.Increment(ref skipped);
                            continue;
                        }

                        using var data = tile.Bitmap.Encode(encodeFormat, settings.Quality)
                            ?? throw new NotSupportedException(
                                $"SkiaSharp could not encode a tile as {encodeFormat} on this platform.");
                        sink.Write(tile.Zoom, tile.X, tile.Y, data.ToArray());
                        Interlocked.Increment(ref zoomWritten);
                    }
                }
                finally
                {
                    foreach (var tile in tiles)
                        tile.Dispose();
                }
            });

            written += zoomWritten;
            // Same culture as the summary line written through AnsiConsole.
            Console.Error.WriteLine(
                $"zoom {zoom}: {zoomWritten:N0} tile(s) written ({stopwatch.Elapsed.TotalSeconds:F1}s)");
        }

        var (west, south) = WebMercator.ToLonLat(area.MinX, area.MinY);
        var (east, north) = WebMercator.ToLonLat(area.MaxX, area.MaxY);
        if (area.MaxX - area.MinX >= WebMercator.Circumference || area.MinX < -XyzTileGrid.Extent || area.MaxX > XyzTileGrid.Extent)
        {
            // The area wraps the antimeridian; record the full longitude range.
            west = -180;
            east = 180;
        }

        sink.Complete(new TileSetMetadata
        {
            Name = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath))),
            Description = $"S-100 portrayal of {string.Join(", ", specs.Distinct())} rendered by s100 tiles export.",
            Format = format,
            MinZoom = minZoom,
            MaxZoom = maxZoom,
            Bounds = (west, south, east, north),
            TilePixelSize = settings.TileSize,
            Settings = DescribeSettings(settings, referenceLatitude),
        });

        var skippedNote = settings.SkipEmpty ? $", {skipped:N0} empty skipped" : string.Empty;
        AnsiConsole.MarkupLineInterpolated(
            $"[green]Wrote[/] {outputPath} ([grey]{container.ToString().ToLowerInvariant()}, {TileSetMetadata.FormatToken(format)}, zoom {minZoom}–{maxZoom}, {written:N0} tile(s){skippedNote}[/])");
        return 0;
    }

    /// <summary>
    /// Re-portrays each coverage input for one zoom level, against a viewport
    /// covering the whole tiled area at that zoom's resolution, so grid sampling
    /// and arrow density match the tiles. Vector inputs are reused.
    /// </summary>
    private static HeadlessCompositeInput[] PortrayCoverageForZoom(
        IReadOnlyList<IDatasetProcessor> processors,
        IReadOnlyList<HeadlessCompositeInput> inputs,
        Func<IDatasetProcessor, Viewport?, RenderContext> contextFor,
        TileArea area,
        int zoom,
        HeadlessTileOptions tileOptions)
    {
        double resolution = XyzTileGrid.Resolution(zoom);
        var (minLon, minLat) = WebMercator.ToLonLat(area.MinX, area.MinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(area.MaxX, area.MaxY, clampLatitude: false);
        var viewport = new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = (int)Math.Clamp(Math.Ceiling((area.MaxX - area.MinX) / resolution), 1, MaxCoverageViewportPixels),
            HeightPixels = (int)Math.Clamp(Math.Ceiling((area.MaxY - area.MinY) / resolution), 1, MaxCoverageViewportPixels),
            ScaleDenominator = XyzTileGrid.ScaleDenominator(zoom, tileOptions.ReferenceLatitude),
        };

        var zoomInputs = inputs.ToArray();
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i] is ICoveragePortrayalSource coverageSource)
            {
                var result = coverageSource.BuildCoveragePortrayalAsync(contextFor(processors[i], viewport))
                    .GetAwaiter().GetResult();
                zoomInputs[i] = HeadlessCompositeInput.ForCoverage(result);
            }
        }

        return zoomInputs;
    }

    /// <summary>The EPSG:3857 area to tile; <c>MaxX</c> may lie past +180° for data crossing the antimeridian.</summary>
    internal readonly record struct TileArea(double MinX, double MinY, double MaxX, double MaxY);

    private static bool TryResolveArea(Settings settings, HeadlessCompositeScene scene, out TileArea area)
    {
        if (settings.BoundingBox is not null
            && CompositeViewportBuilder.TryParseDoubles(settings.BoundingBox, 4, out var bb))
        {
            var (minX, minY) = WebMercator.FromLonLat(bb[0], Math.Max(bb[1], -WebMercator.MaxLatitude));
            var (maxX, maxY) = WebMercator.FromLonLat(bb[2], Math.Min(bb[3], WebMercator.MaxLatitude));
            area = new TileArea(minX, minY, maxX, maxY);
            return true;
        }

        if (scene.TryGetWorldBounds(out double sMinX, out double sMinY, out double sMaxX, out double sMaxY))
        {
            area = new TileArea(
                sMinX,
                Math.Max(sMinY, -XyzTileGrid.Extent),
                sMaxX,
                Math.Min(sMaxY, XyzTileGrid.Extent));
            return true;
        }

        area = default;
        return false;
    }

    /// <summary>
    /// Resolves the zoom range: explicit options win; otherwise the coarsest
    /// cell minimum display scale sets the lowest zoom and one level past the
    /// finest compilation scale sets the highest, falling back to the extent
    /// (fit in about one tile, then six levels deeper) for data without them.
    /// </summary>
    internal static (int Min, int Max) ResolveZoomRange(
        Settings settings, IReadOnlyList<VectorPortrayalResult> vectorResults, TileArea area, double referenceLatitude)
    {
        double span = Math.Max(area.MaxX - area.MinX, area.MaxY - area.MinY);
        int extentZoom = span > 0
            ? Math.Clamp((int)Math.Floor(Math.Log2(2.0 * XyzTileGrid.Extent / span)), 0, XyzTileGrid.MaxZoom)
            : DefaultZoomCeiling;

        var minimumScales = vectorResults
            .Select(r => r.CellMinimumDisplayScale)
            .OfType<int>()
            .Where(s => s > 0)
            .ToList();
        var compilationScales = vectorResults
            .Select(r => r.CellCompilationScale ?? r.CellMinimumDisplayScale)
            .OfType<int>()
            .Where(s => s > 0)
            .ToList();

        int defaultMin = minimumScales.Count > 0
            ? XyzTileGrid.ZoomForScaleDenominator(minimumScales.Max(), referenceLatitude)
            : extentZoom;
        int defaultMax = compilationScales.Count > 0
            ? XyzTileGrid.ZoomForScaleDenominator(compilationScales.Min(), referenceLatitude) + 1
            : Math.Min(defaultMin + 6, DefaultZoomCeiling);

        int min = settings.MinZoom ?? Math.Min(defaultMin, settings.MaxZoom ?? defaultMin);
        int max = settings.MaxZoom ?? Math.Max(Math.Min(defaultMax, XyzTileGrid.MaxZoom), min);
        return (min, Math.Max(min, max));
    }

    private static bool Confirm(long totalTiles)
    {
        if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            return false;

        return AnsiConsole.Confirm(
            string.Create(CultureInfo.InvariantCulture, $"Write {totalTiles:N0} tiles?"),
            defaultValue: false);
    }

    private static DrawingInstructionCategory ResolveHiddenCategories(Settings settings)
    {
        var hidden = DrawingInstructionCategory.None;
        if (settings.Hide is not null && RenderCommand.TryParseHideCategories(settings.Hide, out var parsed, out _))
            hidden |= parsed;
        if (settings.NoText)
            hidden |= DrawingInstructionCategory.Text;
        return hidden;
    }

    private static Dictionary<string, string> DescribeSettings(Settings settings, double referenceLatitude)
    {
        var description = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["generator"] = "s100 " + CliVersionInfo.FromAssembly(typeof(TilesExportCommand).Assembly).InformationalVersion,
            ["palette"] = settings.Palette.Trim().ToLowerInvariant(),
            ["symbolScale"] = settings.SymbolScale.ToString(CultureInfo.InvariantCulture),
            ["textScale"] = settings.TextScale.ToString(CultureInfo.InvariantCulture),
            ["timeStep"] = settings.TimeStep.ToString(CultureInfo.InvariantCulture),
            ["basemap"] = settings.Basemap.Trim().ToLowerInvariant(),
            ["scaleLatitude"] = referenceLatitude.ToString("F4", CultureInfo.InvariantCulture),
        };
        var hidden = ResolveHiddenCategories(settings);
        if (hidden != DrawingInstructionCategory.None)
            description["hidden"] = hidden.ToString();
        if (!string.IsNullOrWhiteSpace(settings.DisplayMode))
            description["displayMode"] = settings.DisplayMode.Trim().ToLowerInvariant();
        return description;
    }

    internal static bool TryResolveContainer(string? container, string outputPath, out TileContainer resolved)
    {
        switch (container?.Trim().ToLowerInvariant())
        {
            case null or "":
                resolved = Path.GetExtension(outputPath).ToLowerInvariant() switch
                {
                    ".pmtiles" => TileContainer.PmTiles,
                    ".mbtiles" => TileContainer.MbTiles,
                    _ => TileContainer.Xyz,
                };
                return true;
            case "xyz":
            case "dir":
            case "directory":
                resolved = TileContainer.Xyz;
                return true;
            case "pmtiles":
                resolved = TileContainer.PmTiles;
                return true;
            case "mbtiles":
                resolved = TileContainer.MbTiles;
                return true;
            default:
                resolved = TileContainer.Xyz;
                return false;
        }
    }

    internal static bool TryParseImageFormat(string? value, out TileImageFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "png":
                format = TileImageFormat.Png;
                return true;
            case "jpg":
            case "jpeg":
                format = TileImageFormat.Jpeg;
                return true;
            case "webp":
                format = TileImageFormat.Webp;
                return true;
            default:
                format = TileImageFormat.Png;
                return false;
        }
    }
}
