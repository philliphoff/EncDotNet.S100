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

        /// <summary>Whether the input is a built tile set rather than datasets.</summary>
        public bool ServesTileSet =>
            !IsComposite && !IsExplicitExchangeSet && !string.IsNullOrWhiteSpace(Input) && TileSource.IsTileSet(Input);

        public override ValidationResult Validate()
        {
            if (ServesTileSet && CacheDirectory is not null)
                return ValidationResult.Error("--cache-dir applies only when rendering datasets, not when serving a built tile set.");
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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var diagnosticTrace = settings.Debug ? DiagnosticTraceScope.ToStandardError() : null;
        ITileSource source;
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
                int exitCode = OpenRendered(settings, out var rendered);
                if (rendered is null)
                    return exitCode;

                source = settings.RefreshSeconds == 0
                    ? rendered
                    : new RefreshingTileSource(
                        rendered,
                        fingerprint,
                        () => TileRenderSession.Fingerprint(settings),
                        () => OpenRendered(settings, out var reopened) == 0 ? reopened : null,
                        TimeSpan.FromSeconds(settings.RefreshSeconds),
                        line => AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] {Markup.Escape(line)}"));
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

            await using var server = await TileServer.StartAsync(source, address, settings.Port, token, viewer: !settings.NoViewer).ConfigureAwait(false);

            var what = source is ITileSetSource set ? $"{ContainerName(set.Container)}, " : "rendered on demand, ";
            AnsiConsole.MarkupLine(
                $"Serving [bold]{Markup.Escape(source.Path)}[/] ({what}{TileSetMetadata.FormatToken(source.Format)}, zoom {tileJson["minzoom"]?.ToString() ?? "?"}–{tileJson["maxzoom"]?.ToString() ?? "?"}):");
            foreach (var baseUri in ServeHost.DisplayAddresses(address).Select(a => ServeHost.BaseUri(a, server.Port, token)))
            {
                AnsiConsole.MarkupLine($"  Tiles:    [link]{Markup.Escape(TileServer.TileUrlTemplate(baseUri, source.Format))}[/]");
                AnsiConsole.MarkupLine($"  TileJSON: [link]{Markup.Escape(new Uri(baseUri, XyzDirectoryTileSink.TileJsonFileName).AbsoluteUri)}[/]");
                if (source.Palettes.Count > 0)
                {
                    AnsiConsole.MarkupLine(
                        $"  Palettes: [link]{Markup.Escape(baseUri.AbsoluteUri + "{palette}/{z}/{x}/{y}." + TileSetMetadata.FormatToken(source.Format))}[/] ({string.Join(", ", source.Palettes)})");
                }

                if (!settings.NoViewer)
                    AnsiConsole.MarkupLine($"  Preview:  [link]{Markup.Escape(baseUri.AbsoluteUri)}[/]");
            }

            if (source is RefreshingTileSource)
                AnsiConsole.MarkupLine($"[grey]Checking the datasets for changes every {settings.RefreshSeconds} s.[/]");
            AnsiConsole.MarkupLine("Add the tile URL or TileJSON as a raster source with tileSize 256. Ctrl-C to stop.");

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
    internal static int OpenRendered(Settings settings, out RenderedTileSource? source)
    {
        source = null;
        int opened = TileRenderSession.TryOpen(settings, out var session);
        if (session is null)
            return opened;

        try
        {
            AnsiConsole.MarkupLine($"[grey]Portraying {session.Specs.Count} dataset(s) ({Markup.Escape(string.Join(", ", session.Specs.Distinct()))})…[/]");
            var scene = session.Prepare();
            if (session.ResolveLayout(scene) is not { } layout)
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
                diskCache);
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
        _ => "XYZ directory",
    };
}
