using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Rendering.Scene;
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

    internal sealed class Settings : TilesRenderSettings
    {
        [CommandOption("-o|--output <PATH>")]
        [Description("Output path: a directory for --container xyz, a .pmtiles file for --container pmtiles, or a .mbtiles file for --container mbtiles. An existing archive file is replaced. Required.")]
        public string? Output { get; init; }

        [CommandOption("--container <KIND>")]
        [Description("Tile container: xyz (a {z}/{x}/{y}.<ext> directory plus a tiles.json TileJSON file), pmtiles (one PMTiles v3 archive, static-hostable) or mbtiles (one MBTiles 1.3 SQLite database, for tile servers). Default: from the output extension (.pmtiles or .mbtiles), otherwise xyz.")]
        public string? Container { get; init; }

        [CommandOption("--skip-empty")]
        [Description("Do not write tiles on which nothing was drawn (every pixel is the background). Web maps show a missing tile as empty.")]
        [DefaultValue(false)]
        public bool SkipEmpty { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Write the tile set even when it holds more than 100000 tiles, without asking.")]
        [DefaultValue(false)]
        public bool Yes { get; init; }

        public override ValidationResult Validate()
        {
            var render = ValidateRender();
            if (!render.Successful)
                return render;

            if (string.IsNullOrWhiteSpace(Output))
                return ValidationResult.Error("An output path is required (-o|--output).");
            if (!TryResolveContainer(Container, Output, out _))
                return ValidationResult.Error($"Unknown --container '{Container}'. Use xyz, pmtiles or mbtiles.");
            var parent = Path.GetDirectoryName(Path.GetFullPath(Output));
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                return ValidationResult.Error($"Output directory does not exist: {parent}");

            return ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        try
        {
            int opened = TileRenderSession.TryOpen(settings, out var session);
            if (session is null)
                return opened;

            using (session)
                return Run(settings, session);
        }
        catch (Exception ex)
        {
            return RenderCommand.HandleException(ex, settings.Debug);
        }
    }

    private static int Run(Settings settings, TileRenderSession session)
    {
        TryResolveContainer(settings.Container, settings.Output!, out var container);
        var format = session.Format;
        var specs = session.Specs;

        var stopwatch = Stopwatch.StartNew();
        var scene = session.Prepare();
        if (session.ResolveLayout(scene) is not { } layout)
        {
            AnsiConsole.MarkupLine("[red]The datasets have no geometry to tile; pass --bbox to tile an explicit area.[/]");
            return 2;
        }

        var (area, minZoom, maxZoom) = (layout.Area, layout.MinZoom, layout.MaxZoom);

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

        long written = 0;
        long skipped = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = settings.Parallel ?? Environment.ProcessorCount,
        };

        foreach (var (zoom, blocks) in plans)
        {
            var renderer = scene.RendererFor(zoom, layout);
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

                        sink.Write(tile.Zoom, tile.X, tile.Y, session.Encode(tile));
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

        sink.Complete(new TileSetMetadata
        {
            Name = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath))),
            Description = $"S-100 portrayal of {string.Join(", ", specs.Distinct())} rendered by s100 tiles export.",
            Format = format,
            MinZoom = minZoom,
            MaxZoom = maxZoom,
            Bounds = layout.Bounds,
            TilePixelSize = settings.TileSize,
            Settings = session.DescribeSettings(layout.ReferenceLatitude),
        });

        var skippedNote = settings.SkipEmpty ? $", {skipped:N0} empty skipped" : string.Empty;
        AnsiConsole.MarkupLineInterpolated(
            $"[green]Wrote[/] {outputPath} ([grey]{container.ToString().ToLowerInvariant()}, {TileSetMetadata.FormatToken(format)}, zoom {minZoom}–{maxZoom}, {written:N0} tile(s){skippedNote}[/])");
        return 0;
    }

    private static bool Confirm(long totalTiles)
    {
        if (Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
            return false;

        return AnsiConsole.Confirm(
            string.Create(CultureInfo.InvariantCulture, $"Write {totalTiles:N0} tiles?"),
            defaultValue: false);
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
