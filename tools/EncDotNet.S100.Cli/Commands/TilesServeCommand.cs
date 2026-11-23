using System.ComponentModel;
using System.Net;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tiles serve</c> serves raster tiles on the standard XYZ URLs, so a
/// local web map (MapLibre, Leaflet, OpenLayers) can use them as a tile source
/// (issue #865). It serves either a built tile set — an XYZ directory, a
/// PMTiles archive or an MBTiles database, such as <c>s100 tiles export</c>
/// writes — or datasets, rendering each tile when it is first asked for.
/// </summary>
/// <remarks>
/// Nothing is written. A built tile set is read as it is at each request, so
/// one exported again while it is served is picked up. Datasets are rendered
/// in any of the day, dusk and night palettes (<see cref="RenderedTileSource"/>).
/// By default the server listens on localhost only; with <c>--host</c> set to
/// another address a random access token is generated (unless <c>--token</c>
/// or <c>--no-token</c> is given) and becomes part of every URL.
/// </remarks>
internal sealed class TilesServeCommand : AsyncCommand<TilesServeCommand.Settings>
{
    internal sealed class Settings : TilesRenderSettings
    {
        [CommandOption("--host <ADDRESS>")]
        [Description("The address to listen on (default 127.0.0.1). Use 0.0.0.0 to serve other machines.")]
        public string Host { get; init; } = "127.0.0.1";

        [CommandOption("--port <PORT>")]
        [Description("The port to listen on (default 8200; 0 picks a free port).")]
        public int Port { get; init; } = 8200;

        [CommandOption("--token <TOKEN>")]
        [Description("An access token that becomes part of every URL. Generated automatically when --host is not a loopback address.")]
        public string? Token { get; init; }

        [CommandOption("--no-token")]
        [Description("Serve without a token even on a non-loopback address.")]
        public bool NoToken { get; init; }

        [CommandOption("--no-viewer")]
        [Description("Don't serve the preview map page at the root URL.")]
        public bool NoViewer { get; init; }

        [CommandOption("--ready-json")]
        [Description("When the server is listening, also print one line of JSON on standard output with the bound host, port, token and URLs, for a supervising process.")]
        public bool ReadyJson { get; init; }

        [CommandOption("--cache-mb <MEGABYTES>")]
        [Description("When rendering datasets: the most memory rendered tiles are kept in, in megabytes (default 256).")]
        [DefaultValue(256)]
        public int CacheMegabytes { get; init; } = 256;

        [CommandOption("--cache-dir <FOLDER>")]
        [Description("When rendering datasets: also keep rendered tiles in this folder, across runs. Tiles are reused only for the same data and settings.")]
        public string? CacheDirectory { get; init; }

        [CommandOption("--cache-dir-mb <MEGABYTES>")]
        [Description("The most disk space the --cache-dir tiles take, in megabytes (default 1024). The least recently used are deleted first.")]
        [DefaultValue(1024)]
        public int CacheDirectoryMegabytes { get; init; } = 1024;

        [CommandOption("--clear-cache")]
        [Description("Delete the tiles cached in --cache-dir before serving.")]
        public bool ClearCache { get; init; }

        [CommandOption("--refresh <SECONDS>")]
        [Description("When rendering datasets: how often to check their files for changes, reopening them when they change (default 10; 0 never checks).")]
        [DefaultValue(10)]
        public int RefreshSeconds { get; init; } = 10;

        [CommandOption("--manage-datasets")]
        [Description("When rendering datasets: accept requests to add and remove datasets while serving, at the datasets URL. The tiles of datasets that stay served stay cached.")]
        public bool ManageDatasets { get; init; }

        [CommandOption("--strict-time")]
        [Description("When rendering datasets: a time outside a time-varying dataset's validity (its own, or else the median interval between its steps; before, after or in a gap) draws nothing for it, instead of holding its step however far away; a tile on which nothing draws answers 204.")]
        public bool StrictTime { get; init; }

        [CommandOption("--overview")]
        [Description("When rendering datasets: below their minimum zoom, draw each dataset's coverage outline and name instead of answering 204, and advertise WMS and WMTS down to zoom 0, so GIS clients still show where the charts are when zoomed out.")]
        public bool Overview { get; init; }

        [CommandOption("--layers-by <GROUPING>")]
        [Description("When rendering datasets: also serve the datasets as WMTS layers by group, each with its own time steps and extent, besides the one layer of all of them: 'product' (chart for S-57, S-101 and S-102; S104, S111, S124, … for the rest) or 'none' (default).")]
        public string? LayersBy { get; init; }

        [CommandOption("--verbose")]
        [Description("When rendering datasets: print a line for each block rendered, with its zoom, tiles, the time spent waiting for a render slot, preparing, drawing and encoding, and how many requests waited on it.")]
        public bool Verbose { get; init; }

        /// <inheritdoc/>
        public override string? FingerprintExcludedFolder => CacheDirectory;

        /// <summary>Whether the input is a built tile set rather than datasets.</summary>
        public bool ServesTileSet =>
            !IsComposite && !IsExplicitExchangeSet && !string.IsNullOrWhiteSpace(Input) && TileSource.IsTileSet(Input);

        public override ValidationResult Validate()
        {
            if (ServesTileSet && Overview)
                return ValidationResult.Error("--overview applies only when rendering datasets, not when serving a built tile set.");
            if (ServesTileSet && CacheDirectory is not null)
                return ValidationResult.Error("--cache-dir applies only when rendering datasets, not when serving a built tile set.");
            if (ServesTileSet && ManageDatasets)
                return ValidationResult.Error("--manage-datasets applies only when rendering datasets, not when serving a built tile set.");
            if (LayersBy is not null && !TileLayers.Groupings.Contains(LayersBy.Trim().ToLowerInvariant()))
                return ValidationResult.Error($"--layers-by must be one of: {string.Join(", ", TileLayers.Groupings)}.");
            if (ServesTileSet && LayersBy is not null)
                return ValidationResult.Error("--layers-by applies only when rendering datasets, not when serving a built tile set.");
            if (ClearCache && CacheDirectory is null)
                return ValidationResult.Error("--clear-cache needs --cache-dir.");
            if (RefreshSeconds < 0)
                return ValidationResult.Error("--refresh must be zero or more seconds.");
            if (CacheDirectoryMegabytes < 1)
                return ValidationResult.Error("--cache-dir-mb must be at least 1.");
            if (CacheDirectory is not null && File.Exists(CacheDirectory))
                return ValidationResult.Error($"--cache-dir '{CacheDirectory}' is a file, not a folder.");

            if (!ServesTileSet)
            {
                if (!string.IsNullOrWhiteSpace(Input) && !Directory.Exists(Input) && !File.Exists(Input))
                    return ValidationResult.Error($"'{Input}' does not exist.");

                var render = ValidateRender();
                if (!render.Successful)
                    return render;
                if (CacheMegabytes < 1)
                    return ValidationResult.Error("--cache-mb must be at least 1.");
            }

            return ServeHost.Validate(Host, Port, Token, NoToken);
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        ITileSource source;
        ServedDatasets? datasets = null;
        try
        {
            if (settings.ServesTileSet)
            {
                source = TileSource.Open(settings.Input!);
            }
            else
            {
                // Fingerprint before opening: a change made meanwhile is then
                // seen as one, rather than missed.
                var fingerprint = TileRenderSession.Fingerprint(settings);
                // One registry across reloads, so registered settings survive them.
                var marinerStyles = new MarinerStyleRegistry();
                // With --manage-datasets, one memory cache and scale latitude across
                // changes to the datasets, so the tiles of those still served stay cached.
                var managed = settings.ManageDatasets ? new ManagedDatasetTiles(settings.CacheMegabytes * 1024L * 1024L) : null;
                DatasetChanges Changes() => datasets?.Changes ?? DatasetChanges.None;
                int exitCode = OpenRendered(settings, out var rendered, marinerStyles, managed: managed);
                if (rendered is null)
                    return exitCode;

                if (settings.RefreshSeconds == 0 && managed is null)
                {
                    source = rendered;
                }
                else
                {
                    var refreshing = new RefreshingTileSource(
                        rendered,
                        fingerprint,
                        () => TileRenderSession.Fingerprint(settings, Changes()),
                        () => OpenRendered(settings, out var reopened, marinerStyles, Changes(), managed) == 0 ? reopened : null,
                        settings.RefreshSeconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(settings.RefreshSeconds),
                        line => AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] {Markup.Escape(line)}"));
                    source = refreshing;
                    if (managed is not null)
                    {
                        datasets = new ServedDatasets(
                            refreshing,
                            changes => OpenRendered(settings, out var changed, marinerStyles, changes, managed) == 0 ? changed : null,
                            changes => TileRenderSession.Fingerprint(settings, changes));
                    }
                }

                // Outermost, over the refreshing source: a reload or a dataset
                // change gives a new revision, and the overview follows it.
                if (settings.Overview)
                    source = new OverviewTileSource(source);
            }
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(e.Message)}");
            return 2;
        }
        catch (Exception ex)
        {
            return RenderCommand.HandleException(ex, settings.Debug);
        }

        try
        {
            var address = IPAddress.Parse(settings.Host);
            var token = ServeHost.ResolveToken(settings.Token, settings.NoToken, address);
            var tileJson = source.ToTileJson(palette: null);

            await using var server = await TileServer.StartAsync(source, address, settings.Port, token, viewer: !settings.NoViewer, datasets: datasets).ConfigureAwait(false);

            if (settings.ReadyJson)
            {
                // Written directly, not through the console renderer, so it's never wrapped (issue #1496).
                Console.Out.WriteLine(ServeReadyLine.Format(address, server.Port, token, source.Format, viewer: !settings.NoViewer, highDpi: source.CanReadPixelRatio(2), pmTilesArchive: TileServer.PmTilesArchive(source) is not null));
                Console.Out.Flush();
            }

            var what = source is ITileSetSource set ? $"{ContainerName(set.Container)}, " : "rendered on demand, ";
            AnsiConsole.MarkupLine(
                $"Serving [bold]{Markup.Escape(source.Path)}[/] ({what}{TileSetMetadata.FormatToken(source.Format)}, zoom {tileJson["minzoom"]?.ToString() ?? "?"}–{tileJson["maxzoom"]?.ToString() ?? "?"}):");
            foreach (var baseUri in ServeHost.DisplayAddresses(address).Select(a => ServeHost.BaseUri(a, server.Port, token)))
            {
                AnsiConsole.MarkupLine($"  Tiles:    [link]{Markup.Escape(TileServer.TileUrlTemplate(baseUri, source.Format))}[/]");
                if (source.CanReadPixelRatio(2))
                    AnsiConsole.MarkupLine($"  High-DPI: [link]{Markup.Escape(TileServer.TileUrlTemplate(baseUri, source.Format, highDpi: true))}[/] (512-pixel tiles)");
                AnsiConsole.MarkupLine($"  TileJSON: [link]{Markup.Escape(new Uri(baseUri, XyzDirectoryTileSink.TileJsonFileName).AbsoluteUri)}[/]");
                if (source.Palettes.Count > 0)
                {
                    AnsiConsole.MarkupLine(
                        $"  Palettes: [link]{Markup.Escape(baseUri.AbsoluteUri + "{palette}/{z}/{x}/{y}." + TileSetMetadata.FormatToken(source.Format))}[/] ({string.Join(", ", source.Palettes)})");
                }

                AnsiConsole.MarkupLine($"  WMTS:     [link]{Markup.Escape(new Uri(baseUri, WmtsService.CapabilitiesPath).AbsoluteUri)}[/]");
                if (source.Layers.Count > 0)
                {
                    AnsiConsole.MarkupLine(
                        $"  Layers:   [link]{Markup.Escape(baseUri.AbsoluteUri + TileServer.LayersPath + "/{layer}/" + XyzDirectoryTileSink.TileJsonFileName)}[/] ({Markup.Escape(string.Join(", ", source.Layers))})");
                }
                if (TileServer.PmTilesArchive(source) is not null)
                    AnsiConsole.MarkupLine($"  PMTiles:  [link]{Markup.Escape(new Uri(baseUri, TileServer.PmTilesArchivePath).AbsoluteUri)}[/] (the archive, for pmtiles:// clients)");
                AnsiConsole.MarkupLine($"  WMS:      [link]{Markup.Escape(new Uri(baseUri, WmsService.Path).AbsoluteUri)}[/]");
                if (datasets is not null)
                    AnsiConsole.MarkupLine($"  Datasets: [link]{Markup.Escape(new Uri(baseUri, ServedDatasets.Path).AbsoluteUri)}[/]");
                if (!settings.NoViewer)
                    AnsiConsole.MarkupLine($"  Preview:  [link]{Markup.Escape(baseUri.AbsoluteUri)}[/]");
            }

            if (((source as OverviewTileSource)?.Inner ?? source) is RefreshingTileSource && settings.RefreshSeconds > 0)
                AnsiConsole.MarkupLine($"[grey]Checking the datasets for changes every {settings.RefreshSeconds} s.[/]");
            if (source is OverviewTileSource overview)
                AnsiConsole.MarkupLine($"[grey]Drawing dataset coverage and names below zoom {overview.ChartMinZoom}.[/]");
            AnsiConsole.MarkupLine("Add the tile URL or TileJSON as a raster source with tileSize 256, or the WMTS or WMS URL in a GIS. Ctrl-C to stop.");

            // The web host handles Ctrl-C and SIGTERM and stops itself.
            await server.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            if (source is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                (source as IDisposable)?.Dispose();
        }
    }

    /// <summary>Opens the datasets and prepares them in the default palette, so problems show before serving.</summary>
    /// <param name="settings">The command's settings.</param>
    /// <param name="source">The source opened, or <see langword="null"/> on failure.</param>
    /// <param name="marinerStyles">The mariner settings clients register; <see langword="null"/> for a new registry.</param>
    /// <param name="changes">Datasets added and removed while serving (issue #1494); <see langword="null"/> for none.</param>
    /// <param name="managed">With <c>--manage-datasets</c>, what tiles share across changes to the datasets; <see langword="null"/> otherwise.</param>
    internal static int OpenRendered(
        Settings settings,
        out RenderedTileSource? source,
        MarinerStyleRegistry? marinerStyles = null,
        DatasetChanges? changes = null,
        ManagedDatasetTiles? managed = null)
    {
        source = null;
        int opened = TileRenderSession.TryOpen(settings, out var session, changes);
        if (session is null)
            return opened;

        try
        {
            AnsiConsole.MarkupLine($"[grey]Portraying {session.Specs.Count} dataset(s) ({Markup.Escape(string.Join(", ", session.Specs.Distinct()))})…[/]");
            var scene = session.Prepare();
            if (session.ResolveLayout(scene, managed?.ReferenceLatitude) is not { } layout)
            {
                AnsiConsole.MarkupLine("[red]The datasets have no geometry to tile; pass --bbox to serve an explicit area.[/]");
                session.Dispose();
                return 2;
            }

            DiskTileCache? diskCache = null;
            if (settings.CacheDirectory is { } cacheDirectory)
            {
                if (settings.ClearCache)
                    DiskTileCache.Clear(cacheDirectory);
                diskCache = new DiskTileCache(
                    cacheDirectory, session.Fingerprint(), session.Format, settings.CacheDirectoryMegabytes * 1024L * 1024L);
                AnsiConsole.MarkupLine(string.Create(System.Globalization.CultureInfo.CurrentCulture,
                    $"[grey]Caching tiles in {Markup.Escape(diskCache.Folder)} ({diskCache.Size / (1024.0 * 1024.0):N1} of {settings.CacheDirectoryMegabytes:N0} MB used).[/]"));
            }

            source = new RenderedTileSource(
                session,
                scene,
                layout,
                settings.Palette,
                settings.Metatile,
                settings.Parallel ?? Environment.ProcessorCount,
                settings.CacheMegabytes * 1024L * 1024L,
                diskCache,
                new DatasetFeatureQuery(settings),
                marinerStyles,
                settings.StrictTime,
                managed?.Memory,
                managed is null ? null : new DatasetTileKeys(session, layout, settings.Metatile),
                TileLayers.Assign(session.Paths, session.Specs, settings.LayersBy, changes),
                settings.Verbose ? line => AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss.fff} {Markup.Escape(line)}[/]") : null);
            managed?.ReferenceLatitude ??= layout.ReferenceLatitude;
            return 0;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static string ContainerName(TileContainer container) => container switch
    {
        TileContainer.PmTiles => "PMTiles",
        TileContainer.MbTiles => "MBTiles",
        TileContainer.GeoPackage => "GeoPackage",
        _ => "XYZ directory",
    };
}
