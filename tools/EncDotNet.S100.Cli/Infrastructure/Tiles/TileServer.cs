using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using EncDotNet.S100.Rendering.Scene;
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
/// document whose tile URL is absolute, an OGC WMTS 1.0.0 service under
/// <c>wmts</c> (<see cref="WmtsService"/>), an OGC WMS 1.3.0 service at
/// <c>wms</c> (<see cref="WmsService"/>), and optionally the web page at
/// the root (<see cref="Web.ServeWeb"/>, issue #1573). With a token, every
/// route lives under <c>/&lt;token&gt;/</c>, as the request's path base,
/// except the <c>/healthz</c> health check. Rendered datasets grouped into
/// layers (issue #1495) also serve each layer's tiles and TileJSON under
/// <c>layers/{layer}/</c>.
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
    /// <summary>The path, under the server's base URL, of the layers' tiles: <c>layers/{layer}/…</c>.</summary>
    public const string LayersPath = "layers";

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
    /// <remarks>With <paramref name="datasets"/>, a <c>POST</c> or <c>DELETE</c> of <c>datasets</c> adds or removes datasets (issue #1494).</remarks>
    public static async Task<TileServer> StartAsync(
        ITileSource source,
        IPAddress address,
        int port,
        string? token,
        bool viewer = true,
        CancellationToken cancellationToken = default,
        ServedDatasets? datasets = null,
        TextWriter? errors = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(address);

        var builder = ServeHost.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(address, port));
        builder.Services.AddResponseCompression(o =>
        {
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ["application/json", "application/xml", "text/html"];
        });
        if (viewer)
            Web.ServeWeb.AddServices(builder, source);

        var app = builder.Build();
        errors ??= Console.Error;
        app.Use(async (context, next) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
            {
                // Logging providers are cleared, so an unhandled error would
                // otherwise be an unexplained 500: report it with its route.
                ReportError(errors, context, e);
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.Headers.AccessControlAllowOrigin = "*";
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            }
        });
        app.UseResponseCompression();

        var prefix = token is null ? string.Empty : "/" + Uri.EscapeDataString(token);
        if (token is not null)
            UseTokenPathBase(app, PathString.FromUriComponent(prefix), viewer);

        // Routing runs after the token's path base is taken off the path, so
        // the routes below are the same with a token and without (issue #1575).
        app.UseRouting();

        // RequestDelegate endpoints: nothing is bound by reflection (issue #764).
        ServeHost.MapHealth(app, () => source.Revision);

        app.MapGet("/" + XyzDirectoryTileSink.TileJsonFileName, context => ServeTileJsonAsync(context, source, prefix, palette: null));
        app.MapGet("/{z}/{x}/{tile}", context => ServeTileAsync(context, source, palette: null));
        if (source.Palettes.Count > 0)
        {
            // The same tiles in each palette, under /{palette}/.
            app.MapGet("/{palette}/" + XyzDirectoryTileSink.TileJsonFileName, context =>
                TryPalette(context, source, out var palette) ? ServeTileJsonAsync(context, source, prefix, palette) : Task.CompletedTask);
            app.MapGet("/{palette}/{z}/{x}/{tile}", context =>
                TryPalette(context, source, out var palette) ? ServeTileAsync(context, source, palette) : Task.CompletedTask);
        }

        if (source.HasDatasets)
        {
            // Each layer's tiles (issue #1495), under /layers/{layer}/, as the
            // tiles of every dataset are under the root. Layers can come and
            // go with --manage-datasets, so they are looked up at each request.
            var layers = "/" + LayersPath + "/{layer}/";
            app.MapGet(layers + XyzDirectoryTileSink.TileJsonFileName, context =>
                TryLayer(context, source, out var layer) ? ServeTileJsonAsync(context, layer, prefix, palette: null, LayerOf(context)) : Task.CompletedTask);
            app.MapGet(layers + "{z}/{x}/{tile}", context =>
                TryLayer(context, source, out var layer) ? ServeTileAsync(context, layer, palette: null) : Task.CompletedTask);
            app.MapGet(layers + "{palette}/" + XyzDirectoryTileSink.TileJsonFileName, context =>
                TryLayer(context, source, out var layer) && TryPalette(context, layer, out var palette)
                    ? ServeTileJsonAsync(context, layer, prefix, palette, LayerOf(context))
                    : Task.CompletedTask);
            app.MapGet(layers + "{palette}/{z}/{x}/{tile}", context =>
                TryLayer(context, source, out var layer) && TryPalette(context, layer, out var palette)
                    ? ServeTileAsync(context, layer, palette)
                    : Task.CompletedTask);
        }

        // OGC WMTS 1.0.0: the capabilities document, the KVP endpoint and the
        // RESTful tile URLs, with and without the Time dimension.
        app.MapGet("/" + WmtsService.CapabilitiesPath, context => ServeWmtsCapabilitiesAsync(context, source, prefix));
        app.MapGet("/" + WmtsService.KvpPath, context => ServeWmtsKvpAsync(context, source, prefix));
        app.MapGet("/wmts/1.0.0/{layer}/{style}/{tileMatrixSet}/{tileMatrix}/{tileRow}/{tile}", context => ServeWmtsRestTileAsync(context, source));
        app.MapGet("/wmts/1.0.0/{layer}/{style}/{time}/{tileMatrixSet}/{tileMatrix}/{tileRow}/{tile}", context => ServeWmtsRestTileAsync(context, source));

        // Each dataset's metadata (issue #1504).
        app.MapGet("/" + DatasetsDocument.Path, context => DatasetsDocument.ServeAsync(context, source));

        // Mariner settings sets, served as {palette}-{hash} styles (issue #1493).
        MarinerStyleRegistry.Map(app, () => source.MarinerStyles, () => source.Palettes);

        // OGC WMS 1.3.0: GetCapabilities, GetMap and GetFeatureInfo.
        app.MapGet("/" + WmsService.Path, context => WmsService.HandleAsync(context, source, RequestBaseUri(context, prefix)));

        // Adding and removing datasets, with --manage-datasets (issue #1494).
        if (datasets is not null)
            ServedDatasets.Map(app, datasets);

        // The web page (issue #1573), at the root.
        if (viewer)
            Web.ServeWeb.Map(app);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        return new TileServer(app, ServeHost.BaseUri(address, bound.Port, token));
    }

    /// <summary>
    /// Serves every route under <paramref name="prefix"/>, <c>/{token}</c>: a
    /// request under it continues with the token moved to its path base, so
    /// links the page builds keep it; any other request but the health check
    /// is not found. The prefix alone redirects to the page's slashed URL.
    /// </summary>
    private static void UseTokenPathBase(WebApplication app, PathString prefix, bool viewer)
    {
        app.Use((context, next) =>
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments(prefix, out var matched, out var rest))
            {
                if (!rest.HasValue && viewer)
                {
                    // The page loads its files relative to itself, so it's
                    // served from the slashed URL.
                    context.Response.Redirect(request.PathBase + prefix + "/" + request.QueryString);
                    return Task.CompletedTask;
                }

                request.PathBase = request.PathBase.Add(matched);
                request.Path = rest.HasValue ? rest : "/";
                return next(context);
            }

            if (request.Path == ServeHost.HealthPath)
                return next(context);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
    }

    /// <summary>The suffix of a high-DPI tile's row in its URL (issue #1563): <c>{z}/{x}/{y}@2x.png</c>.</summary>
    public const string HighDpiSuffix = "@2x";

    /// <summary>
    /// The tile URL template under a base URL, e.g. <c>http://127.0.0.1:8200/{z}/{x}/{y}.png</c>,
    /// or with <paramref name="highDpi"/> its 512-pixel tiles', <c>…/{z}/{x}/{y}@2x.png</c>.
    /// </summary>
    public static string TileUrlTemplate(Uri baseUri, TileImageFormat format, bool highDpi = false) =>
        baseUri.AbsoluteUri + "{z}/{x}/{y}" + (highDpi ? HighDpiSuffix : string.Empty) + "." + TileSetMetadata.FormatToken(format);

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

    /// <summary>
    /// Writes an error a request failed with to <paramref name="errors"/>:
    /// the method, the path and query, the layer when the route names one
    /// (<c>{layer}</c>, or the WMTS <c>LAYER</c> parameter), and the exception.
    /// </summary>
    private static void ReportError(TextWriter errors, HttpContext context, Exception exception)
    {
        var request = context.Request;
        var layer = request.RouteValues.TryGetValue("layer", out var routed) ? routed as string
            : request.Query.TryGetValue("LAYER", out var queried) ? queried.ToString()
            : null;
        var message = $"Error serving {request.Method} {request.Path}{request.QueryString}"
            + (string.IsNullOrEmpty(layer) ? string.Empty : $" (layer '{layer}')")
            + $": {exception}";
        lock (errors)
            errors.WriteLine(message);
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

    /// <summary>Reads the <c>{layer}</c> route value; answers 404 when the source has no such layer.</summary>
    private static bool TryLayer(HttpContext context, ITileSource source, out ITileSource layer)
    {
        if (source.Layer(LayerOf(context)) is { } found)
        {
            layer = found;
            return true;
        }

        layer = source;
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return false;
    }

    private static string LayerOf(HttpContext context) => (string)context.Request.RouteValues["layer"]!;

    /// <summary>
    /// Reads the optional <c>t</c> query parameter, an ISO 8601 instant (UTC
    /// unless it says otherwise); answers 400 when it isn't one.
    /// </summary>
    private static bool TryTime(HttpContext context, out DateTime? time)
    {
        time = null;
        if (!context.Request.Query.TryGetValue("t", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
            return true;

        if (DateTimeOffset.TryParse(
                values.ToString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            time = parsed.UtcDateTime;
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return false;
    }

    /// <param name="context">The request.</param>
    /// <param name="source">The tiles: of every dataset, or of <paramref name="layer"/>.</param>
    /// <param name="prefix">The token prefix, <c>/{token}</c>, or empty.</param>
    /// <param name="palette">The palette, or <see langword="null"/> for the default.</param>
    /// <param name="layer">The layer (issue #1495), or <see langword="null"/> for every dataset.</param>
    private static async Task ServeTileJsonAsync(HttpContext context, ITileSource source, string prefix, string? palette, string? layer = null)
    {
        if (!TryTime(context, out var time) || !TryScale(context, source, out bool highDpi))
            return;

        // The tile URL is absolute, built from the request, so it works from
        // whichever address the client reached this server on.
        var root = RequestBaseUri(context, prefix);
        var layerPath = layer is null ? string.Empty : LayersPath + "/" + Uri.EscapeDataString(layer) + "/";
        var baseUri = new Uri(root, layerPath + (palette is null ? string.Empty : palette + "/"));
        var json = source.ToTileJson(palette, time);
        var tiles = TileUrlTemplate(baseUri, source.Format, highDpi);
        if (highDpi)
            json["tileSize"] = 2 * XyzTileGrid.TileSize;
        if (source.CanReadPixelRatio(2) && json["s100"] is System.Text.Json.Nodes.JsonObject ratios)
            ratios["pixelRatios"] = new System.Text.Json.Nodes.JsonArray(1, 2);
        if (json["s100"]?["time"] is System.Text.Json.Nodes.JsonValue step)
            tiles += "?t=" + Uri.EscapeDataString((string)step!);
        json["tiles"] = new System.Text.Json.Nodes.JsonArray(tiles);
        if (source.HasDatasets && json["s100"] is System.Text.Json.Nodes.JsonObject s100)
            s100["datasetsUrl"] = root.AbsoluteUri + DatasetsDocument.Path;
        if (layer is not null && json["s100"] is System.Text.Json.Nodes.JsonObject layerS100)
            layerS100["datasetsUrl"] = root.AbsoluteUri + DatasetsDocument.Path;

        // The layers of the datasets (issue #1495), each with its TileJSON.
        if (layer is null && source.Layers.Count > 0 && json["s100"] is System.Text.Json.Nodes.JsonObject withLayers)
        {
            withLayers["layers"] = new System.Text.Json.Nodes.JsonArray([.. source.Layers.Select(id => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = id,
                ["tilejson"] = root.AbsoluteUri + LayersPath + "/" + Uri.EscapeDataString(id) + "/"
                    + (palette is null ? string.Empty : palette + "/") + XyzDirectoryTileSink.TileJsonFileName,
            })]);
        }

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(json.ToJsonString(TileSetMetadata.JsonOptions), context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task ServeTileAsync(HttpContext context, ITileSource source, string? palette)
    {
        var values = context.Request.RouteValues;
        var tile = (string)values["tile"]!;
        int dot = tile.IndexOf('.', StringComparison.Ordinal);
        var row = dot < 0 ? tile : tile[..dot];

        // {y}@2x: the 512-pixel tile, drawn at twice the pixel density
        // (issue #1563), from a source that renders its tiles.
        bool highDpi = row.EndsWith(HighDpiSuffix, StringComparison.Ordinal);
        if (highDpi)
            row = row[..^HighDpiSuffix.Length];
        if (dot < 0
            || !TryParse((string)values["z"]!, out int zoom)
            || !TryParse((string)values["x"]!, out int x)
            || !TryParse(row, out int y)
            || !TileSource.IsValid(zoom, x, y)
            || !MatchesFormat(tile[(dot + 1)..], source.Format)
            || (highDpi && !source.CanReadPixelRatio(2)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!TryTime(context, out var time))
            return;

        await WriteTileAsync(context, source, zoom, x, y, palette, time, highDpi ? 2 : null).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the optional <c>scale</c> query parameter of a TileJSON request:
    /// <c>1</c>, or <c>2</c> for the 512-pixel <c>@2x</c> tiles. Answers 400
    /// when it's neither, and 404 for <c>2</c> when the source can't draw them.
    /// </summary>
    private static bool TryScale(HttpContext context, ITileSource source, out bool highDpi)
    {
        highDpi = false;
        if (!context.Request.Query.TryGetValue("scale", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
            return true;

        switch (values.ToString().Trim())
        {
            case "1":
                return true;
            case "2" when source.CanReadPixelRatio(2):
                highDpi = true;
                return true;
            case "2":
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return false;
            default:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return false;
        }
    }

    /// <summary>
    /// Writes tile <paramref name="zoom"/>/<paramref name="x"/>/<paramref name="y"/>,
    /// at <paramref name="pixelRatio"/> when given (an <c>@2x</c> tile), 204
    /// when empty, 304 when the ETag matches.
    /// </summary>
    private static async Task WriteTileAsync(HttpContext context, ITileSource source, int zoom, int x, int y, string? palette, DateTime? time, int? pixelRatio = null)
    {
        var data = pixelRatio is { } ratio
            ? await source.ReadAsync(zoom, x, y, palette, time, ratio, context.RequestAborted).ConfigureAwait(false)
            : await source.ReadAsync(zoom, x, y, palette, time, context.RequestAborted).ConfigureAwait(false);
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

    /// <summary>The base URL a client reached this server on, ending in <c>/</c>, so documents carry URLs that work from there.</summary>
    private static Uri RequestBaseUri(HttpContext context, string prefix) =>
        new($"{context.Request.Scheme}://{context.Request.Host}{prefix}/");

    private static Task ServeWmtsCapabilitiesAsync(HttpContext context, ITileSource source, string prefix)
    {
        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        return context.Response.WriteAsync(WmtsService.BuildCapabilities(source, RequestBaseUri(context, prefix)), context.RequestAborted);
    }

    /// <summary>The WMTS KVP endpoint: <c>REQUEST=GetCapabilities</c> or <c>REQUEST=GetTile</c>. Parameter names are case-insensitive.</summary>
    private static Task ServeWmtsKvpAsync(HttpContext context, ITileSource source, string prefix)
    {
        var query = context.Request.Query;
        string? Get(string name) => query.TryGetValue(name, out var values) ? values.ToString() : null;

        var service = Get("SERVICE");
        if (string.IsNullOrEmpty(service))
            return WmtsErrorAsync(context, StatusCodes.Status400BadRequest, "MissingParameterValue", "SERVICE", "The SERVICE parameter is required.");
        if (!string.Equals(service, "WMTS", StringComparison.OrdinalIgnoreCase))
            return WmtsErrorAsync(context, StatusCodes.Status400BadRequest, "InvalidParameterValue", "SERVICE", $"Unknown service '{service}'; this is WMTS.");

        var request = Get("REQUEST");
        if (string.Equals(request, "GetCapabilities", StringComparison.OrdinalIgnoreCase))
            return ServeWmtsCapabilitiesAsync(context, source, prefix);
        if (!string.Equals(request, "GetTile", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(request)
                ? WmtsErrorAsync(context, StatusCodes.Status400BadRequest, "MissingParameterValue", "REQUEST", "The REQUEST parameter is required.")
                : WmtsErrorAsync(context, StatusCodes.Status501NotImplemented, "OperationNotSupported", "REQUEST", $"Operation '{request}' isn't supported; use GetCapabilities or GetTile.");
        }

        var (tile, error) = WmtsService.ParseTile(
            source, Get("LAYER"), Get("STYLE"), Get("FORMAT"), Get("TILEMATRIXSET"),
            Get("TILEMATRIX"), Get("TILEROW"), Get("TILECOL"), Get(WmtsService.TimeDimension));
        return ServeWmtsTileAsync(context, source, tile, error);
    }

    private static Task ServeWmtsRestTileAsync(HttpContext context, ITileSource source)
    {
        var values = context.Request.RouteValues;
        var file = (string)values["tile"]!;
        int dot = file.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || !MatchesFormat(file[(dot + 1)..], source.Format))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        var (tile, error) = WmtsService.ParseTile(
            source, (string?)values["layer"], (string?)values["style"], format: null, (string?)values["tileMatrixSet"],
            (string?)values["tileMatrix"], (string?)values["tileRow"], file[..dot], (string?)values["time"]);
        return ServeWmtsTileAsync(context, source, tile, error);
    }

    private static Task ServeWmtsTileAsync(HttpContext context, ITileSource source, WmtsService.TileRequest? tile, WmtsService.TileRequestError? error)
    {
        if (error is { } e)
            return WmtsErrorAsync(context, StatusCodes.Status400BadRequest, e.Code, e.Locator, e.Message);

        var t = tile!.Value;
        var layer = t.Layer == WmtsService.LayerIdentifier ? source : source.Layer(t.Layer);
        if (layer is null)
            return WmtsErrorAsync(context, StatusCodes.Status400BadRequest, "InvalidParameterValue", "LAYER", $"Unknown layer '{t.Layer}'.");
        return WriteTileAsync(context, layer, t.Zoom, t.X, t.Y, t.Palette, t.Time);
    }

    private static Task WmtsErrorAsync(HttpContext context, int status, string code, string? locator, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/xml; charset=utf-8";
        return context.Response.WriteAsync(WmtsService.ExceptionReport(code, locator, message), context.RequestAborted);
    }

    private static bool TryParse(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool MatchesFormat(string extension, TileImageFormat format) =>
        string.Equals(extension, TileSetMetadata.FormatToken(format), StringComparison.OrdinalIgnoreCase)
        || (format == TileImageFormat.Jpeg && string.Equals(extension, "jpeg", StringComparison.OrdinalIgnoreCase));
}
