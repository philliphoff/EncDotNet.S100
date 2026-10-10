using System.ComponentModel;
using System.Net;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tiles serve</c> serves a built raster tile set — an XYZ directory,
/// a PMTiles archive or an MBTiles database, such as <c>s100 tiles export</c>
/// writes — on the standard XYZ URLs, so a local web map (MapLibre, Leaflet,
/// OpenLayers) can use it as a tile source (issue #865).
/// </summary>
/// <remarks>
/// Nothing is written. The tile set is read as it is at each request, so one
/// exported again while it is served is picked up. By default the server
/// listens on localhost only; with <c>--host</c> set to another address a
/// random access token is generated (unless <c>--token</c> or
/// <c>--no-token</c> is given) and becomes part of every URL.
/// </remarks>
internal sealed class TilesServeCommand : AsyncCommand<TilesServeCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("The tile set to serve: a {z}/{x}/{y} directory, a .pmtiles archive or a .mbtiles database.")]
        public string Path { get; init; } = string.Empty;

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

        public override ValidationResult Validate()
        {
            if (!Directory.Exists(Path) && !File.Exists(Path))
                return ValidationResult.Error($"'{Path}' does not exist.");
            return ServeHost.Validate(Host, Port, Token, NoToken);
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ITileSource source;
        try
        {
            source = TileSource.Open(settings.Path);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(e.Message)}");
            return 2;
        }

        var address = IPAddress.Parse(settings.Host);
        var token = ServeHost.ResolveToken(settings.Token, settings.NoToken, address);
        var tileJson = source.ToTileJson();

        await using var server = await TileServer.StartAsync(source, address, settings.Port, token, viewer: !settings.NoViewer).ConfigureAwait(false);

        AnsiConsole.MarkupLine(
            $"Serving [bold]{Markup.Escape(source.Path)}[/] ({ContainerName(source.Container)}, {TileSetMetadata.FormatToken(source.Format)}, zoom {tileJson["minzoom"]?.ToString() ?? "?"}–{tileJson["maxzoom"]?.ToString() ?? "?"}):");
        foreach (var baseUri in ServeHost.DisplayAddresses(address).Select(a => ServeHost.BaseUri(a, server.Port, token)))
        {
            AnsiConsole.MarkupLine($"  Tiles:    [link]{Markup.Escape(TileServer.TileUrlTemplate(baseUri, source.Format))}[/]");
            AnsiConsole.MarkupLine($"  TileJSON: [link]{Markup.Escape(new Uri(baseUri, XyzDirectoryTileSink.TileJsonFileName).AbsoluteUri)}[/]");
            if (!settings.NoViewer)
                AnsiConsole.MarkupLine($"  Preview:  [link]{Markup.Escape(baseUri.AbsoluteUri)}[/]");
        }

        AnsiConsole.MarkupLine("Add the tile URL or TileJSON as a raster source with tileSize 256. Ctrl-C to stop.");

        // The web host handles Ctrl-C and SIGTERM and stops itself.
        await server.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    private static string ContainerName(TileContainer container) => container switch
    {
        TileContainer.PmTiles => "PMTiles",
        TileContainer.MbTiles => "MBTiles",
        _ => "XYZ directory",
    };
}
