using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Serves an <see cref="ITileSource"/> over HTTP on the standard XYZ URLs
/// (issue #865): <c>{z}/{x}/{y}.{ext}</c> tiles, a <c>tiles.json</c> TileJSON
/// document whose tile URL is absolute, and optionally a preview page at the
/// root. With a token, every route lives under <c>/&lt;token&gt;/</c>.
/// </summary>
/// <remarks>
/// Every response allows any origin (CORS), so a web map on another local
/// port can use the tiles. Tiles carry an ETag of their content and
/// <c>Cache-Control: no-cache</c>, so browsers revalidate cheaply and see a
/// tile set that is written again while it is served. A tile the set doesn't
/// have answers <c>204 No Content</c>, which web maps draw as empty.
/// </remarks>
internal sealed class TileServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TileServer(WebApplication app, Uri baseUri)
    {
        _app = app;
        BaseUri = baseUri;
    }

    /// <summary>The server's base URL, as bound (the host as given; the actual port), ending in <c>/</c>.</summary>
    public Uri BaseUri { get; }

    /// <summary>The bound port.</summary>
    public int Port => BaseUri.Port;

    /// <summary>Starts serving <paramref name="source"/> on <paramref name="address"/>:<paramref name="port"/> (0 for any free port).</summary>
    public static async Task<TileServer> StartAsync(
        ITileSource source,
        IPAddress address,
        int port,
        string? token,
        bool viewer = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(address);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(address, port));
        builder.Services.AddResponseCompression(o =>
        {
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ["application/json", "text/html"];
        });

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            return next(context);
        });
        app.UseResponseCompression();

        var prefix = token is null ? string.Empty : "/" + Uri.EscapeDataString(token);
        // RequestDelegate endpoints: nothing is bound by reflection (issue #764).
        if (viewer)
            app.MapGet(prefix + "/", ServeViewerAsync);

        app.MapGet(prefix + "/" + XyzDirectoryTileSink.TileJsonFileName, context => ServeTileJsonAsync(context, source, prefix, palette: null));
        app.MapGet(prefix + "/{z}/{x}/{tile}", context => ServeTileAsync(context, source, palette: null));
        if (source.Palettes.Count > 0)
        {
            // The same tiles in each palette, under /{palette}/.
            app.MapGet(prefix + "/{palette}/" + XyzDirectoryTileSink.TileJsonFileName, context =>
                TryPalette(context, source, out var palette) ? ServeTileJsonAsync(context, source, prefix, palette) : Task.CompletedTask);
            app.MapGet(prefix + "/{palette}/{z}/{x}/{tile}", context =>
                TryPalette(context, source, out var palette) ? ServeTileAsync(context, source, palette) : Task.CompletedTask);
        }

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        return new TileServer(app, ServeHost.BaseUri(address, bound.Port, token));
    }

    /// <summary>The tile URL template under a base URL, e.g. <c>http://127.0.0.1:8200/{z}/{x}/{y}.png</c>.</summary>
    public static string TileUrlTemplate(Uri baseUri, TileImageFormat format) =>
        baseUri.AbsoluteUri + "{z}/{x}/{y}." + TileSetMetadata.FormatToken(format);

    /// <summary>
    /// Runs until the host shuts down — on Ctrl-C (SIGINT) or SIGTERM, which
    /// the web host handles itself — or <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken) => _app.WaitForShutdownAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Reads the <c>{palette}</c> route value; answers 404 when the source has no such palette.</summary>
    private static bool TryPalette(HttpContext context, ITileSource source, out string palette)
    {
        palette = ((string)context.Request.RouteValues["palette"]!).ToLowerInvariant();
        if (source.Palettes.Contains(palette))
            return true;

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return false;
    }

    private static async Task ServeTileJsonAsync(HttpContext context, ITileSource source, string prefix, string? palette)
    {
        // The tile URL is absolute, built from the request, so it works from
        // whichever address the client reached this server on.
        var request = context.Request;
        var baseUri = new Uri($"{request.Scheme}://{request.Host}{prefix}/{(palette is null ? string.Empty : palette + "/")}");
        var json = source.ToTileJson(palette);
        json["tiles"] = new System.Text.Json.Nodes.JsonArray(TileUrlTemplate(baseUri, source.Format));

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(json.ToJsonString(TileSetMetadata.JsonOptions), context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task ServeTileAsync(HttpContext context, ITileSource source, string? palette)
    {
        var values = context.Request.RouteValues;
        var tile = (string)values["tile"]!;
        int dot = tile.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0
            || !TryParse((string)values["z"]!, out int zoom)
            || !TryParse((string)values["x"]!, out int x)
            || !TryParse(tile[..dot], out int y)
            || !TileSource.IsValid(zoom, x, y)
            || !MatchesFormat(tile[(dot + 1)..], source.Format))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var data = await source.ReadAsync(zoom, x, y, palette, context.RequestAborted).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-cache";
        if (data is null)
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var etag = "\"" + Convert.ToHexString(SHA256.HashData(data), 0, 16) + "\"";
        context.Response.Headers.ETag = etag;
        if (context.Request.Headers.IfNoneMatch.Contains(etag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.ContentType = TileSource.ContentType(source.Format);
        context.Response.ContentLength = data.Length;
        await context.Response.Body.WriteAsync(data, context.RequestAborted).ConfigureAwait(false);
    }

    private static Task ServeViewerAsync(HttpContext context)
    {
        // Routing ignores a trailing slash, but the page loads tiles.json
        // relative to itself, so it must be served from the slashed URL.
        var path = context.Request.PathBase + context.Request.Path;
        if (!path.Value!.EndsWith('/'))
        {
            context.Response.Redirect(path + "/");
            return Task.CompletedTask;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        return context.Response.Body.WriteAsync(ViewerPage, context.RequestAborted).AsTask();
    }

    private static bool TryParse(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool MatchesFormat(string extension, TileImageFormat format) =>
        string.Equals(extension, TileSetMetadata.FormatToken(format), StringComparison.OrdinalIgnoreCase)
        || (format == TileImageFormat.Jpeg && string.Equals(extension, "jpeg", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A MapLibre GL JS page showing the tiles over OpenStreetMap, framed on
    /// the set's bounds. It loads MapLibre and the basemap from the internet;
    /// the chart tiles come from this server.
    /// </summary>
    private static readonly byte[] ViewerPage = Encoding.UTF8.GetBytes("""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>s100 tiles</title>
        <link rel="stylesheet" href="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css">
        <script src="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js"></script>
        <style>html, body, #map { margin: 0; height: 100%; } #status { position: absolute; top: 8px; left: 8px; padding: 4px 8px; background: #fffc; font: 13px system-ui, sans-serif; border-radius: 4px; } #status select { margin-left: 6px; font: inherit; }</style>
        </head>
        <body>
        <div id="map"></div>
        <div id="status"></div>
        <script>
        (async () => {
          const status = document.getElementById("status");
          const tileJsonUrl = new URL("tiles.json", location.href).href;
          let tileJson;
          try {
            tileJson = await (await fetch(tileJsonUrl)).json();
          } catch (e) {
            status.textContent = "Couldn't load tiles.json: " + e;
            return;
          }
          status.textContent = (tileJson.name || "Tiles") + " · zoom " + tileJson.minzoom + "–" + tileJson.maxzoom;
          const palettes = (tileJson.s100 && tileJson.s100.palettes) || [];
          const map = new maplibregl.Map({
            container: "map",
            style: {
              version: 8,
              sources: {
                osm: {
                  type: "raster",
                  tiles: ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
                  tileSize: 256,
                  attribution: "© OpenStreetMap contributors",
                },
                chart: { type: "raster", url: tileJsonUrl, tileSize: 256 },
              },
              layers: [
                { id: "osm", type: "raster", source: "osm" },
                { id: "chart", type: "raster", source: "chart" },
              ],
            },
            center: tileJson.center ? [tileJson.center[0], tileJson.center[1]] : [0, 0],
            zoom: tileJson.center ? tileJson.center[2] : 1,
          });
          map.addControl(new maplibregl.NavigationControl());
          if (palettes.length > 0) {
            // Each palette has its own TileJSON under /{palette}/.
            const select = document.createElement("select");
            select.setAttribute("aria-label", "Palette");
            for (const palette of palettes) {
              const option = new Option(palette, palette, false, palette === tileJson.s100.palette);
              select.add(option);
            }
            select.addEventListener("change", () =>
              map.getSource("chart").setUrl(new URL(select.value + "/tiles.json", location.href).href));
            status.append(select);
          }
          if (tileJson.bounds) {
            const [w, s, e, n] = tileJson.bounds;
            map.fitBounds([[w, s], [e, n]], { padding: 20, animate: false });
          }
        })();
        </script>
        </body>
        </html>
        """);
}
