using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Datasets.Pipelines;
using SkiaSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 render &lt;dataset&gt; &lt;output&gt;</c> — detects the dataset's product
/// specification, runs its portrayal pipeline through the Mapsui-free Skia
/// headless renderer, and writes a PNG image.
/// </summary>
internal sealed class RenderCommand : Command<RenderCommand.Settings>
{
    /// <summary>Maximum supported output dimension (per axis), to guard against OOM.</summary>
    private const int MaxDimension = 16384;

    internal sealed class Settings : DatasetCommandSettings
    {
        [CommandArgument(1, "<output>")]
        [Description("Path of the PNG image to write.")]
        public string OutputPath { get; init; } = string.Empty;

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
        [Description("Suppress text/label drawing instructions. Equivalent to --hide text.")]
        [DefaultValue(false)]
        public bool NoText { get; init; }

        [CommandOption("--hide")]
        [Description("Comma-separated list of drawing-instruction categories to suppress: text, points, lines, areas (e.g. --hide text,points). Combines additively with --no-text.")]
        public string? Hide { get; init; }

        public override ValidationResult Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(OutputPath))
                return ValidationResult.Error("An output path is required.");

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

            var dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                return ValidationResult.Error($"Output directory does not exist: {dir}");

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        try
        {
            var spec = DatasetPipelineFactory.DetectProductSpec(settings.DatasetPath);
            if (spec is null)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Could not detect an S-100 product specification for:[/] {settings.DatasetPath}");
                return 2;
            }

            var processor = factory.CreateProcessor(settings.DatasetPath);

            if (processor is not IHeadlessImageRenderer headless)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Headless image rendering is not supported for {spec}.[/]");
                return 3;
            }

            TryParsePalette(settings.Palette, out var palette);
            RgbaColor? background = null;
            if (settings.Background is not null && TryParseHexColor(settings.Background, out var bg))
                background = bg;

            var hidden = DrawingInstructionCategory.None;
            if (settings.Hide is not null
                && TryParseHideCategories(settings.Hide, out var parsedHide, out _))
                hidden |= parsedHide;
            if (settings.NoText)
                hidden |= DrawingInstructionCategory.Text;

            var renderContext = RenderContextBuilder.Build(
                processor, palette, settings.SymbolScale, settings.TextScale, settings.TimeStep, hidden);

            using var bitmap = headless
                .RenderHeadlessAsync(settings.Width, settings.Height, renderContext, background)
                .GetAwaiter().GetResult();

            WritePng(bitmap, settings.OutputPath);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Wrote[/] {settings.OutputPath} ([grey]{spec}, {bitmap.Width}x{bitmap.Height}[/])");
            return 0;
        }
        catch (NotSupportedException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Not supported:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 4;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            catalogueManager.Dispose();
        }
    }

    private static void WritePng(SKBitmap bitmap, string outputPath)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
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
}
