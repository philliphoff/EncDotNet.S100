using System.ComponentModel;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Rendering.Scene;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// The inputs and display options shared by the commands that render tiles
/// from datasets: <c>tiles export</c> and <c>tiles serve</c>. The input grammar
/// matches <c>s100 render</c>: one dataset, several <c>--layer</c> datasets, or
/// an exchange set.
/// </summary>
internal abstract class TilesRenderSettings : CommandSettings
{
    [CommandArgument(0, "[input]")]
    [Description("The dataset to tile, or an exchange set (a directory containing a CATALOG.XML, a CATALOG.XML, or an exchange-set .zip). tiles serve also takes a built tile set: a {z}/{x}/{y} directory, a .pmtiles archive or a .mbtiles database. Omit when using --layer or --exchange-set.")]
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

    [CommandOption("--min-zoom <ZOOM>")]
    [Description("Lowest zoom level (0-24). Default: derived from the datasets' minimum display scale, or from their extent.")]
    public int? MinZoom { get; init; }

    [CommandOption("--max-zoom <ZOOM>")]
    [Description("Highest zoom level (0-24). Default: one level past the datasets' finest compilation scale, or six levels past the minimum zoom, capped at 18.")]
    public int? MaxZoom { get; init; }

    [CommandOption("--bbox <BBOX>")]
    [Description("Limit the tiles to a WGS-84 bounding box 'minLon,minLat,maxLon,maxLat' (e.g. --bbox -1.5,50.0,-1.0,50.5). Default: the union extent of the datasets.")]
    public string? BoundingBox { get; init; }

    [CommandOption("--tile-size <PIXELS>")]
    [Description("Tile image size: 256 (default) or 512. 512 renders the same 256-pixel grid at twice the pixel density (\"@2x\" tiles for high-DPI screens); use tileSize 256 for the source in the web map.")]
    [DefaultValue(256)]
    public int TileSize { get; init; } = 256;

    [CommandOption("--format <FORMAT>")]
    [Description("Tile encoding: png (default), jpeg (jpg), or webp. jpeg has no transparency, so set --background.")]
    [DefaultValue("png")]
    public string Format { get; init; } = "png";

    [CommandOption("--quality <QUALITY>")]
    [Description("Encoder quality (1-100) for jpeg and webp. Ignored for png. Default 90.")]
    [DefaultValue(90)]
    public int Quality { get; init; } = 90;

    [CommandOption("--palette <PALETTE>")]
    [Description("Colour palette: day, dusk, or night (default day).")]
    [DefaultValue("day")]
    public string Palette { get; init; } = "day";

    [CommandOption("--symbol-scale <FACTOR>")]
    [Description("Symbol scale factor (default 1.0).")]
    [DefaultValue(1.0)]
    public double SymbolScale { get; init; } = 1.0;

    [CommandOption("--text-scale <FACTOR>")]
    [Description("Text scale factor (default 1.0).")]
    [DefaultValue(1.0)]
    public double TextScale { get; init; } = 1.0;

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

    [CommandOption("--metatile <TILES>")]
    [Description("Render blocks of N x N tiles at once and cut them apart (1-16, default 4). Larger blocks are faster for dense data and use more memory.")]
    [DefaultValue(4)]
    public int Metatile { get; init; } = 4;

    [CommandOption("--parallel <WORKERS>")]
    [Description("Number of blocks rendered at once. Default: the processor count.")]
    public int? Parallel { get; init; }

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

    /// <summary>Validates the inputs and the rendering options.</summary>
    protected ValidationResult ValidateRender()
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
        if (!TilesExportCommand.TryParseImageFormat(Format, out _))
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
