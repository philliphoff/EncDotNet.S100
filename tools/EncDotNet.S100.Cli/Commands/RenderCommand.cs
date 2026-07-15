using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using SkiaSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 render</c> renders one or more S-100 datasets to an image. Three
/// grammars are supported:
/// <list type="bullet">
///   <item><description>
///     <c>render &lt;dataset&gt; &lt;output&gt;</c> — the single-dataset form:
///     detects the dataset's product specification and rasterises it through the
///     Mapsui-free Skia headless renderer.
///   </description></item>
///   <item><description>
///     <c>render --layer A --layer B … &lt;output&gt;</c> — the composite form:
///     stacks several products into one image via the renderer-neutral S-98
///     interoperability engine (<see cref="IS100CompositeRenderer{TResult}"/>).
///   </description></item>
///   <item><description>
///     <c>render &lt;exchange-set&gt; &lt;output&gt;</c> (or
///     <c>--exchange-set &lt;path&gt;</c>) — the exchange-set form: discovers
///     every renderable dataset in a directory / <c>CATALOG.XML</c> /
///     exchange-set <c>.zip</c> and composites them through the same engine.
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// Two behaviours differ between the single form and the two composite forms and
/// are surfaced in <c>--help</c>: (1) the composite forms do <b>not</b> apply
/// S-101 sequential/sibling updates (the single-dataset form still does); and
/// (2) the S-98 authority orders layers by display plane, so <c>--layer</c>
/// order is only a within-plane tiebreak — hand-ordering layers generally has no
/// effect. In the exchange-set form, datasets whose product specification is
/// unsupported, whose file is missing, or that declare data protection
/// (encryption) are skipped with a warning on stderr rather than failing the set.
/// </remarks>
internal sealed class RenderCommand : Command<RenderCommand.Settings>
{
    /// <summary>Maximum supported output dimension (per axis), to guard against OOM.</summary>
    private const int MaxDimension = 16384;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Single-dataset form: the dataset to render. Composite form (with --layer and no -o): the output image path.")]
        public string? Input { get; init; }

        [CommandArgument(1, "[output]")]
        [Description("Single-dataset form: the output image path. The format is inferred from the file extension unless --format is given.")]
        public string? OutputArgument { get; init; }

        [CommandOption("--layer <PATH>")]
        [Description("Add a dataset as a composite layer (repeatable). When any --layer is given, several products are composited into one image. Note: the S-98 authority orders layers by display plane, so --layer order is only a within-plane tiebreak.")]
        public string[] Layers { get; init; } = [];

        [CommandOption("--exchange-set|--from <PATH>")]
        [Description("Composite every discoverable dataset in an exchange set: a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip archive whose root holds one. A directory/CATALOG.XML/.zip passed positionally is also auto-detected. Mutually exclusive with --layer. The composite applies no S-101 updates; data-protected and unsupported datasets are skipped with a warning.")]
        public string? ExchangeSet { get; init; }

        [CommandOption("--only <SPECS>")]
        [Description("Exchange-set form only: restrict compositing to a comma-separated list of product specifications (e.g. --only S101,S128; hyphenation and case are ignored). Datasets of other specs discovered in the set are omitted.")]
        public string? Only { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output image path. Required (or given positionally) for the composite form; optional alternative to the positional <output> for the single-dataset form.")]
        public string? OutputOption { get; init; }

        [CommandOption("--bbox <BBOX>")]
        [Description("Composite only: explicit shared viewport as a WGS-84 bounding box 'minLon,minLat,maxLon,maxLat' (e.g. --bbox -1.5,50.0,-1.0,50.5). Mutually exclusive with --center/--scale. When omitted, the compositor auto-fits the union of all layers.")]
        public string? BoundingBox { get; init; }

        [CommandOption("--center <CENTER>")]
        [Description("Composite only: explicit shared viewport centre 'lon,lat' (e.g. --center -1.25,50.25). Must be used with --scale.")]
        public string? Center { get; init; }

        [CommandOption("--scale <DENOMINATOR>")]
        [Description("Composite only: explicit shared viewport scale denominator (e.g. --scale 50000 for 1:50000). Must be used with --center.")]
        public double? Scale { get; init; }

        [CommandOption("--format")]
        [Description("Output image format: png, jpeg (jpg), or webp. Default: inferred from the output file extension, falling back to png.")]
        public string? Format { get; init; }

        [CommandOption("--quality")]
        [Description("Encoder quality (1-100) for lossy formats such as jpeg and webp. Ignored for png. Default 90.")]
        [DefaultValue(90)]
        public int Quality { get; init; }

        [CommandOption("-w|--width")]
        [Description("Output image width in pixels (default 1024).")]
        [DefaultValue(1024)]
        public int Width { get; init; }

        [CommandOption("-h|--height")]
        [Description("Output image height in pixels (default 768).")]
        [DefaultValue(768)]
        public int Height { get; init; }

        [CommandOption("--palette")]
        [Description("Colour palette: day, dusk, or night (default day).")]
        [DefaultValue("day")]
        public string Palette { get; init; } = "day";

        [CommandOption("--symbol-scale")]
        [Description("Symbol scale factor (default 1.0).")]
        [DefaultValue(1.0)]
        public double SymbolScale { get; init; }

        [CommandOption("--text-scale")]
        [Description("Text scale factor (default 1.0).")]
        [DefaultValue(1.0)]
        public double TextScale { get; init; }

        [CommandOption("--time-step")]
        [Description("Zero-based time-step index for time-series datasets (S-104/S-111). Default 0.")]
        [DefaultValue(0)]
        public int TimeStep { get; init; }

        [CommandOption("--background")]
        [Description("Background colour as a hex string (e.g. #FFFFFF or #80FFFFFF). Default opaque white.")]
        public string? Background { get; init; }

        [CommandOption("--no-text")]
        [Description("Suppress text/label drawing instructions. Equivalent to --hide text. In the composite form the suppression is global (applies to every layer).")]
        [DefaultValue(false)]
        public bool NoText { get; init; }

        [CommandOption("--hide")]
        [Description("Comma-separated list of drawing-instruction categories to suppress: text, points, lines, areas (e.g. --hide text,points). Combines additively with --no-text. In the composite form the suppression is global (applies to every layer).")]
        public string? Hide { get; init; }

        [CommandOption("--debug")]
        [Description("Show full stack traces on error, and surface host/Lua portrayal diagnostics on stderr.")]
        public bool Debug { get; init; }

        [CommandOption("--no-updates")]
        [Description("Single-dataset form only: do not apply sibling S-101 sequential updates (.001, .002, …) found alongside an .000 base cell. By default they are applied best-effort. The composite form never applies updates.")]
        [DefaultValue(false)]
        public bool NoUpdates { get; init; }

        [CommandOption("--basemap <MODE>")]
        [Description("Draw a basemap beneath the chart data: none (default) or offline. 'offline' composites the bundled Natural Earth 1:10m land layer (public domain) under all chart layers, registered with the chart's own viewport. Online tile basemaps are not supported in the headless renderer. Applies to both the single-dataset and --layer composite forms.")]
        [DefaultValue("none")]
        public string Basemap { get; init; } = "none";

        [CommandOption("--display-mode <MODE>")]
        [Description("Select the S-411 sea-ice portrayal display mode (S-411 only): ice-concentration (default), ice-sod (stage of development) or ice-navigational (PROVISIONAL preview derived from total concentration — NOT a POLARIS/RIO navigational-risk computation). A single dataset carries the full WMO egg code, so the same data can be shown in any mode. Supplying this option for any other product specification is a validation error.")]
        public string? DisplayMode { get; init; }

        /// <summary>Whether this invocation composites explicit <c>--layer</c> datasets.</summary>
        public bool IsComposite => Layers.Length > 0;

        /// <summary>Whether an exchange set was named explicitly via <c>--exchange-set</c>/<c>--from</c>.</summary>
        public bool IsExplicitExchangeSet => !string.IsNullOrWhiteSpace(ExchangeSet);

        /// <summary>
        /// Whether the positional <see cref="Input"/> is (auto-detected as) an
        /// exchange set — a directory / <c>CATALOG.XML</c> / exchange-set
        /// <c>.zip</c> — rather than a single dataset. Suppressed when
        /// <c>--layer</c> or <c>--exchange-set</c> is in play.
        /// </summary>
        public bool IsPositionalExchangeSet =>
            !IsComposite
            && !IsExplicitExchangeSet
            && !string.IsNullOrWhiteSpace(Input)
            && ExchangeSetInput.LooksLikeExchangeSet(Input);

        /// <summary>Whether this invocation composites an exchange set / directory.</summary>
        public bool IsExchangeSetComposite => IsExplicitExchangeSet || IsPositionalExchangeSet;

        /// <summary>Whether the composite viewport flags (<c>--bbox</c>/<c>--center</c>/<c>--scale</c>) apply.</summary>
        public bool AllowsViewport => IsComposite || IsExchangeSetComposite;

        /// <summary>The exchange-set source path for the exchange-set form.</summary>
        public string? ExchangeSetSource => IsExplicitExchangeSet ? ExchangeSet : Input;

        /// <summary>
        /// Resolves the output path for the current grammar, or <c>null</c> when
        /// none is determinable. Mirrors the resolution enforced by
        /// <see cref="Validate"/>.
        /// </summary>
        public string? ResolveOutputPath()
        {
            if (OutputOption is not null)
                return OutputOption;
            if (IsComposite || IsExplicitExchangeSet)
                return string.IsNullOrWhiteSpace(OutputArgument) ? Input : null;
            if (IsPositionalExchangeSet)
                return OutputArgument;
            return OutputArgument;
        }

        public override ValidationResult Validate()
        {
            if (Quality is < 1 or > 100)
                return ValidationResult.Error("--quality must be between 1 and 100.");

            if (Width <= 0 || Height <= 0)
                return ValidationResult.Error("--width and --height must be positive.");

            if (Width > MaxDimension || Height > MaxDimension)
                return ValidationResult.Error($"--width and --height must not exceed {MaxDimension}.");

            if (!TryParsePalette(Palette, out _))
                return ValidationResult.Error($"Unknown palette '{Palette}'. Use day, dusk, or night.");

            if (TimeStep < 0)
                return ValidationResult.Error("--time-step must be zero or greater.");

            if (Background is not null && !TryParseHexColor(Background, out _))
                return ValidationResult.Error($"Invalid --background colour '{Background}'.");

            if (Hide is not null && !TryParseHideCategories(Hide, out _, out var badToken))
                return ValidationResult.Error(
                    $"Invalid --hide value '{badToken}'. Use a comma-separated list of: text, points, lines, areas.");

            if (!TryParseBasemap(Basemap, out _))
                return ValidationResult.Error(
                    $"Invalid --basemap value '{Basemap}'. Use none or offline.");

            if (DisplayMode is not null && !TryParseDisplayMode(DisplayMode, out _))
                return ValidationResult.Error(
                    $"Invalid --display-mode value '{DisplayMode}'. Use ice-concentration, ice-sod or ice-navigational.");

            if (IsComposite && IsExplicitExchangeSet)
                return ValidationResult.Error(
                    "--layer and --exchange-set/--from cannot be combined. Use one or the other.");

            if (Only is not null && !IsExchangeSetComposite)
                return ValidationResult.Error(
                    "--only applies only to the exchange-set form (use --exchange-set/--from or a positional exchange set).");

            if (Only is not null && !TryParseOnlySpecs(Only, out _))
                return ValidationResult.Error(
                    "--only must be a comma-separated list of product specifications (e.g. S101,S128).");

            string? output;
            if (IsExchangeSetComposite)
            {
                var source = ExchangeSetSource;
                if (string.IsNullOrWhiteSpace(source))
                    return ValidationResult.Error("An exchange-set path is required.");
                if (!ExchangeSetInput.LooksLikeExchangeSet(source))
                    return ValidationResult.Error(
                        $"Not an S-100 exchange set (expected a directory with CATALOG.XML, a CATALOG.XML, or an exchange-set .zip): {source}");

                if (IsExplicitExchangeSet)
                {
                    if (OutputOption is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(Input) || !string.IsNullOrWhiteSpace(OutputArgument))
                            return ValidationResult.Error(
                                "With --exchange-set and --output, do not also pass a positional argument.");
                        output = OutputOption;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(OutputArgument))
                            return ValidationResult.Error(
                                "With --exchange-set, pass a single output path (or use -o|--output); two positional arguments were given.");
                        output = Input;
                    }
                }
                else
                {
                    // Positional exchange set: <exchange-set> is Input, output is arg1 or -o.
                    output = OutputOption ?? OutputArgument;
                }

                if (string.IsNullOrWhiteSpace(output))
                    return ValidationResult.Error(
                        "An output path is required (positional <output> or -o|--output).");
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

                if (OutputOption is not null)
                {
                    if (!string.IsNullOrWhiteSpace(Input) || !string.IsNullOrWhiteSpace(OutputArgument))
                        return ValidationResult.Error(
                            "With --layer and --output, do not also pass a positional argument.");
                    output = OutputOption;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(OutputArgument))
                        return ValidationResult.Error(
                            "With --layer, pass a single output path (or use -o|--output); two positional arguments were given.");
                    output = Input;
                }

                if (string.IsNullOrWhiteSpace(output))
                    return ValidationResult.Error(
                        "An output path is required (positional <output> or -o|--output).");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Input))
                    return ValidationResult.Error("A dataset path is required (or use --layer to composite).");
                if (!File.Exists(Input))
                    return ValidationResult.Error($"Dataset file not found: {Input}");

                output = OutputOption ?? OutputArgument;
                if (string.IsNullOrWhiteSpace(output))
                    return ValidationResult.Error("An output path is required.");
            }

            if (!TryResolveFormat(Format, output, out _, out var formatError))
                return ValidationResult.Error(formatError);

            var viewportResult = ValidateViewport();
            if (!viewportResult.Successful)
                return viewportResult;

            var dir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                return ValidationResult.Error($"Output directory does not exist: {dir}");

            return ValidationResult.Success();
        }

        private ValidationResult ValidateViewport()
        {
            bool hasBbox = !string.IsNullOrWhiteSpace(BoundingBox);
            bool hasCenter = !string.IsNullOrWhiteSpace(Center);
            bool hasScale = Scale is not null;

            if ((hasBbox || hasCenter || hasScale) && !AllowsViewport)
                return ValidationResult.Error(
                    "--bbox, --center, and --scale apply only to the composite forms (--layer or --exchange-set).");

            if (hasBbox && (hasCenter || hasScale))
                return ValidationResult.Error("--bbox cannot be combined with --center/--scale.");

            if (hasCenter != hasScale)
                return ValidationResult.Error("--center and --scale must be used together.");

            if (hasBbox)
            {
                if (!CompositeViewportBuilder.TryParseDoubles(BoundingBox!, 4, out var bb))
                    return ValidationResult.Error(
                        "--bbox must be four numbers: minLon,minLat,maxLon,maxLat.");
                if (bb[0] >= bb[2] || bb[1] >= bb[3])
                    return ValidationResult.Error(
                        "--bbox requires minLon < maxLon and minLat < maxLat.");
            }

            if (hasCenter && !CompositeViewportBuilder.TryParseDoubles(Center!, 2, out _))
                return ValidationResult.Error("--center must be two numbers: lon,lat.");

            if (hasScale && Scale <= 0)
                return ValidationResult.Error("--scale must be a positive denominator.");

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.IsExchangeSetComposite)
            return ExecuteExchangeSetComposite(settings);
        return settings.IsComposite
            ? ExecuteComposite(settings)
            : ExecuteSingle(settings);
    }

    private static int ExecuteSingle(Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        var datasetPath = settings.Input!;
        var outputPath = settings.ResolveOutputPath()!;
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        try
        {
            var spec = DatasetPipelineFactory.DetectProductSpec(datasetPath);
            if (spec is null)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Could not detect an S-100 product specification for:[/] {datasetPath}");
                return 2;
            }

            var processor = DatasetProcessorLoader.Create(factory, spec, datasetPath, settings.NoUpdates);

            // Non-blocking: warn (on stderr) when the dataset's declared
            // edition diverges from what this build implements. Rendering still
            // proceeds (issue #248).
            if (processor.VersionAssessment?.IsWarning == true)
            {
                Console.Error.WriteLine(processor.VersionAssessment.BuildMessage());
            }

            if (processor is not IHeadlessImageRenderer headless)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Headless image rendering is not supported for {spec}.[/]");
                return 3;
            }

            TryParsePalette(settings.Palette, out var palette);
            RgbaColor? background = ResolveBackground(settings);
            var hidden = ResolveHiddenCategories(settings);

            if (!TryResolveDisplayModeId(settings, processor.Spec.Name, out var displayModeId, out var displayModeError))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{displayModeError}[/]");
                return 2;
            }

            var renderContext = RenderContextBuilder.Build(
                processor, palette, settings.SymbolScale, settings.TextScale, settings.TimeStep, hidden,
                ResolveBasemap(settings), displayModeId);

            using var bitmap = headless
                .RenderHeadlessAsync(settings.Width, settings.Height, renderContext, background)
                .GetAwaiter().GetResult();

            TryResolveFormat(settings.Format, outputPath, out var format, out _);
            WriteImage(bitmap, outputPath, format, settings.Quality);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Wrote[/] {outputPath} ([grey]{spec}, {format}, {bitmap.Width}x{bitmap.Height}[/])");
            return 0;
        }
        catch (Exception ex)
        {
            return HandleException(ex, settings.Debug);
        }
        finally
        {
            catalogueManager.Dispose();
        }
    }

    private static int ExecuteComposite(Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        var outputPath = settings.ResolveOutputPath()!;

        var resolved = new List<(string Path, string Spec)>(settings.Layers.Length);
        foreach (var layerPath in settings.Layers)
            resolved.Add((layerPath, DatasetPipelineFactory.DetectProductSpec(layerPath) ?? "unknown"));

        return RenderComposite(resolved, settings, outputPath, "composite");
    }

    private static int ExecuteExchangeSetComposite(Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        var source = settings.ExchangeSetSource!;
        var outputPath = settings.ResolveOutputPath()!;

        IReadOnlySet<string>? only = null;
        if (settings.Only is not null && TryParseOnlySpecs(settings.Only, out var onlySpecs))
            only = onlySpecs;

        try
        {
            using var resolution = ExchangeSetLayerResolution.Resolve(source, only);

            // Surface discovery-time skips (unsupported spec, missing file,
            // orphan update, data protection) on stderr so they are visible
            // without polluting the success line on stdout.
            foreach (var warning in resolution.Warnings)
                Console.Error.WriteLine(warning);

            if (resolution.Layers.Count == 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]No renderable datasets were discovered in exchange set:[/] {source}");
                return 2;
            }

            var resolved = resolution.Layers
                .Select(l => (l.Path, l.Spec))
                .ToList();

            return RenderComposite(resolved, settings, outputPath, "exchange-set composite");
        }
        catch (Exception ex)
        {
            return HandleException(ex, settings.Debug);
        }
    }

    /// <summary>
    /// Opens each resolved dataset as a composite layer and renders them through
    /// the renderer-neutral S-98 interoperability engine, writing the encoded
    /// image to <paramref name="outputPath"/>. Shared by the <c>--layer</c> and
    /// exchange-set forms.
    /// </summary>
    private static int RenderComposite(
        IReadOnlyList<(string Path, string Spec)> resolved,
        Settings settings,
        string outputPath,
        string label)
    {
        var datasets = new List<S100Dataset>(resolved.Count);
        try
        {
            var specNames = new List<string>(resolved.Count);
            var layers = new List<S100Layer>(resolved.Count);
            foreach (var (path, spec) in resolved)
            {
                var dataset = S100Dataset.Open(path);
                datasets.Add(dataset);
                layers.Add(new S100Layer { Dataset = dataset });
                specNames.Add(spec);
            }

            TryParsePalette(settings.Palette, out var palette);

            if (!string.IsNullOrWhiteSpace(settings.DisplayMode) &&
                !specNames.Any(s => s.Equals("S-411", StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine(
                    "[red]--display-mode is only supported for S-411 sea-ice datasets; none of the composited layers is S-411.[/]");
                return 2;
            }
            TryParseDisplayMode(settings.DisplayMode, out var displayModeId);

            var options = new S100CompositeOptions
            {
                Width = settings.Width,
                Height = settings.Height,
                Palette = palette,
                SymbolScale = settings.SymbolScale,
                TextScale = settings.TextScale,
                TimeStep = settings.TimeStep,
                Background = ResolveBackground(settings),
                HiddenCategories = ResolveHiddenCategories(settings),
                Viewport = ResolveViewport(settings),
                Basemap = ResolveBasemap(settings),
                DisplayModeId = displayModeId,
            };

            using var renderer = new PngS100DatasetRenderer();
            byte[] png = renderer.RenderAsync(layers, options).GetAwaiter().GetResult();

            TryResolveFormat(settings.Format, outputPath, out var format, out _);
            using var bitmap = SKBitmap.Decode(png)
                ?? throw new NotSupportedException("The composite renderer produced an image that could not be decoded.");
            WriteImage(bitmap, outputPath, format, settings.Quality);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Wrote[/] {outputPath} ([grey]{label} of {layers.Count} layer(s): {string.Join(", ", specNames)}; {format}, {bitmap.Width}x{bitmap.Height}[/])");
            return 0;
        }
        catch (Exception ex)
        {
            return HandleException(ex, settings.Debug);
        }
        finally
        {
            foreach (var dataset in datasets)
                dataset.Dispose();
        }
    }

    private static RgbaColor? ResolveBackground(Settings settings)
    {
        if (settings.Background is not null && TryParseHexColor(settings.Background, out var bg))
            return bg;
        return null;
    }

    private static DrawingInstructionCategory ResolveHiddenCategories(Settings settings)
    {
        var hidden = DrawingInstructionCategory.None;
        if (settings.Hide is not null
            && TryParseHideCategories(settings.Hide, out var parsedHide, out _))
            hidden |= parsedHide;
        if (settings.NoText)
            hidden |= DrawingInstructionCategory.Text;
        return hidden;
    }

    private static BasemapKind ResolveBasemap(Settings settings)
        => TryParseBasemap(settings.Basemap, out var basemap) ? basemap : BasemapKind.None;

    private static Viewport? ResolveViewport(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BoundingBox)
            && CompositeViewportBuilder.TryParseDoubles(settings.BoundingBox, 4, out var bb))
        {
            return CompositeViewportBuilder.FromBoundingBox(
                bb[0], bb[1], bb[2], bb[3], settings.Width, settings.Height);
        }

        if (!string.IsNullOrWhiteSpace(settings.Center) && settings.Scale is { } scale
            && CompositeViewportBuilder.TryParseDoubles(settings.Center, 2, out var c))
        {
            return CompositeViewportBuilder.FromCenterScale(
                c[0], c[1], scale, settings.Width, settings.Height);
        }

        return null;
    }

    private static int HandleException(Exception ex, bool debug)
    {
        switch (ex)
        {
            case NotSupportedException:
                AnsiConsole.MarkupLineInterpolated($"[red]Not supported:[/] {ex.Message}");
                if (debug) AnsiConsole.WriteException(ex);
                return 4;

            // Distinct from NotSupportedException (e.g. the dcf8 headless path):
            // readers raise this for recognised-but-not-yet-implemented spec
            // features such as data coding format 1 (irregular fixed-station
            // time series). It does not derive from NotSupportedException, so it
            // needs its own case to avoid the generic exit-1 path. See issue #253.
            case S100DatasetNotSupportedException:
                AnsiConsole.MarkupLineInterpolated($"[red]Not supported:[/] {ex.Message}");
                if (debug) AnsiConsole.WriteException(ex);
                return 4;

            case S100DatasetSchemaException:
                AnsiConsole.MarkupLineInterpolated($"[yellow]Non-conforming dataset:[/] {ex.Message}");
                if (debug) AnsiConsole.WriteException(ex);
                return 5;

            default:
                AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
                if (debug) AnsiConsole.WriteException(ex);
                return 1;
        }
    }

    private static void WriteImage(SKBitmap bitmap, string outputPath, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
            ?? throw new NotSupportedException(
                $"SkiaSharp could not encode the image as {format} on this platform.");
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Resolves the desired <see cref="SKEncodedImageFormat"/> from an explicit
    /// <paramref name="format"/> option when supplied, otherwise infers it from
    /// the extension of <paramref name="outputPath"/>, falling back to PNG when
    /// the extension is absent or unrecognised and no explicit format is given.
    /// Returns <see langword="false"/> with a populated <paramref name="error"/>
    /// when an explicit format is unrecognised or an explicit format conflicts
    /// with a recognised, differing output extension.
    /// </summary>
    internal static bool TryResolveFormat(
        string? format, string outputPath, out SKEncodedImageFormat resolved, out string error)
    {
        resolved = SKEncodedImageFormat.Png;
        error = string.Empty;

        var extKnown = TryParseFormatToken(
            Path.GetExtension(outputPath).TrimStart('.'), out var extFormat);

        if (!string.IsNullOrWhiteSpace(format))
        {
            if (!TryParseFormatToken(format, out resolved))
            {
                error = $"Unknown --format '{format}'. Use png, jpeg, or webp.";
                return false;
            }

            if (extKnown && extFormat != resolved)
            {
                error =
                    $"--format {resolved} conflicts with the output extension " +
                    $"'{Path.GetExtension(outputPath)}' ({extFormat}). " +
                    "Use a matching extension or omit --format.";
                return false;
            }

            return true;
        }

        if (extKnown)
            resolved = extFormat;

        return true;
    }

    private static bool TryParseFormatToken(string value, out SKEncodedImageFormat format)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "png": format = SKEncodedImageFormat.Png; return true;
            case "jpg":
            case "jpeg": format = SKEncodedImageFormat.Jpeg; return true;
            case "webp": format = SKEncodedImageFormat.Webp; return true;
            default: format = SKEncodedImageFormat.Png; return false;
        }
    }

    internal static bool TryParsePalette(string value, out PaletteType palette)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "day": palette = PaletteType.Day; return true;
            case "dusk": palette = PaletteType.Dusk; return true;
            case "night": palette = PaletteType.Night; return true;
            default: palette = PaletteType.Day; return false;
        }
    }

    internal static bool TryParseHexColor(string value, out RgbaColor color)
    {
        color = default;
        var hex = value.Trim().TrimStart('#');

        if (hex.Length is not (6 or 8))
            return false;

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return false;

        byte r, g, b, a;
        if (hex.Length == 6)
        {
            r = (byte)((packed >> 16) & 0xFF);
            g = (byte)((packed >> 8) & 0xFF);
            b = (byte)(packed & 0xFF);
            a = 255;
        }
        else
        {
            a = (byte)((packed >> 24) & 0xFF);
            r = (byte)((packed >> 16) & 0xFF);
            g = (byte)((packed >> 8) & 0xFF);
            b = (byte)(packed & 0xFF);
        }

        color = new RgbaColor(r, g, b, a);
        return true;
    }

    /// <summary>
    /// Parses a comma-separated list of drawing-instruction category tokens
    /// (e.g. <c>"text,points"</c>) into a <see cref="DrawingInstructionCategory"/>
    /// flags value. Tokens are case-insensitive and accept both singular and
    /// plural forms (e.g. <c>text</c>, <c>label</c>, <c>labels</c>; <c>point</c>
    /// or <c>points</c>; etc.). Returns <see langword="false"/> on the first
    /// unrecognised token; the offending token is returned via
    /// <paramref name="badToken"/>.
    /// </summary>
    internal static bool TryParseHideCategories(
        string value, out DrawingInstructionCategory categories, out string badToken)
    {
        categories = DrawingInstructionCategory.None;
        badToken = string.Empty;

        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "text":
                case "texts":
                case "label":
                case "labels":
                    categories |= DrawingInstructionCategory.Text;
                    break;
                case "point":
                case "points":
                case "symbol":
                case "symbols":
                    categories |= DrawingInstructionCategory.Points;
                    break;
                case "line":
                case "lines":
                    categories |= DrawingInstructionCategory.Lines;
                    break;
                case "area":
                case "areas":
                case "fill":
                case "fills":
                    categories |= DrawingInstructionCategory.Areas;
                    break;
                default:
                    badToken = raw;
                    categories = DrawingInstructionCategory.None;
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Parses the <c>--display-mode</c> token (case-insensitive) into the
    /// spec-native S-411 display-mode id declared in the portrayal catalogue.
    /// Accepts <c>ice-concentration</c>, <c>ice-sod</c> and
    /// <c>ice-navigational</c> (plus the bare <c>concentration</c>/<c>sod</c>/
    /// <c>navigational</c> aliases). A <c>null</c>/empty value maps to
    /// <see langword="null"/> (each catalogue's default mode). Note:
    /// <c>ice-navigational</c> is a provisional concentration-derived preview,
    /// not a POLARIS/RIO navigational-risk computation.
    /// </summary>
    internal static bool TryParseDisplayMode(string? value, out string? displayModeId)
        => S411DisplayModes.TryParseToken(value, out displayModeId);

    /// <summary>
    /// Parses the <c>--display-mode</c> option for a single dataset. Enforces
    /// that the option is only meaningful for S-411 (issue #416): supplying it
    /// for any other product is a user error. Returns <see langword="false"/>
    /// with <paramref name="error"/> set on rejection.
    /// </summary>
    private static bool TryResolveDisplayModeId(
        Settings settings, string specName, out string? displayModeId, out string? error)
    {
        displayModeId = null;
        error = null;
        if (string.IsNullOrWhiteSpace(settings.DisplayMode))
            return true;

        if (!specName.Equals("S-411", StringComparison.OrdinalIgnoreCase))
        {
            error = $"--display-mode is only supported for S-411 sea-ice datasets, not {specName}.";
            return false;
        }

        TryParseDisplayMode(settings.DisplayMode, out displayModeId);
        return true;
    }

    /// <summary>
    /// Parses the <c>--basemap</c> token (case-insensitive) into a
    /// <see cref="BasemapKind"/>. Accepts <c>none</c> and <c>offline</c>; a
    /// <c>null</c>/empty value maps to <see cref="BasemapKind.None"/>.
    /// </summary>
    internal static bool TryParseBasemap(string? value, out BasemapKind basemap)
    {
        basemap = BasemapKind.None;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "none":
            case "off":
                basemap = BasemapKind.None;
                return true;
            case "offline":
            case "land":
                basemap = BasemapKind.Offline;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses the <c>--only</c> value into a set of normalized product
    /// specification tokens (hyphenation and case removed, e.g. <c>S-101</c> and
    /// <c>s101</c> both normalize to <c>S101</c>). Returns <see langword="false"/>
    /// when the value contains no non-empty tokens.
    /// </summary>
    internal static bool TryParseOnlySpecs(string value, out IReadOnlySet<string> specs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(ExchangeSetLayerResolution.NormalizeSpec(raw));

        specs = set;
        return set.Count > 0;
    }
}
