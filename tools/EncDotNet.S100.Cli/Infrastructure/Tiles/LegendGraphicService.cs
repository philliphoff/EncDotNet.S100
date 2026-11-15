using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The legend graphics of <see cref="TileServer"/> (issue #1501): the WMS
/// <c>GetLegendGraphic</c> request (SLD 1.1.0 / OGC 05-078r4 §10.2), and the
/// <c>LegendURL</c> of each style in the WMS and WMTS capabilities, which
/// point at it. GIS clients (ArcGIS Pro's Contents pane, QGIS's layer tree)
/// show the graphic as the layer's key.
/// </summary>
/// <remarks>
/// <para>
/// Parameters, case-insensitive: <c>LAYER</c> (required, <c>s100</c>),
/// <c>STYLE</c> (a style of the capabilities: a palette, or
/// <c>{palette}-{hash}</c> of registered mariner settings; the default style
/// when left out), <c>FORMAT</c> (<c>image/png</c>, the default),
/// <c>WIDTH</c> and <c>HEIGHT</c> (the size of each row's glyph in pixels,
/// as GeoServer reads them; 28 × 14 when left out), and the display
/// resolution, as GetMap reads it (<c>DPI</c>, <c>MAP_RESOLUTION</c> or
/// <c>FORMAT_OPTIONS=dpi:N</c>). <c>SLD_VERSION</c> is accepted and ignored.
/// </para>
/// </remarks>
internal static class LegendGraphicService
{
    /// <summary>The WMS request name.</summary>
    public const string RequestName = "GetLegendGraphic";

    /// <summary>The largest glyph <c>WIDTH</c> or <c>HEIGHT</c> accepted, in pixels.</summary>
    public const int MaxGlyphSize = 256;

    private static readonly XNamespace Sld = "http://www.opengis.net/sld";

    /// <summary>The URL of <paramref name="style"/>'s legend graphic under <paramref name="baseUri"/> (ending in <c>/</c>).</summary>
    public static string Url(Uri baseUri, string style) =>
        baseUri.AbsoluteUri + WmsService.Path
        + "?SERVICE=WMS&VERSION=1.3.0&REQUEST=" + RequestName
        + "&LAYER=" + Uri.EscapeDataString(WmsService.LayerName)
        + "&STYLE=" + Uri.EscapeDataString(style)
        + "&FORMAT=" + Uri.EscapeDataString(TileLegend.Format)
        + "&SLD_VERSION=1.1.0";

    /// <summary>
    /// The WMS 1.3.0 <c>LegendURL</c> of <paramref name="style"/>, or
    /// <see langword="null"/> when the source has no legend for it.
    /// </summary>
    public static XElement? WmsLegendUrl(ITileSource source, string style, Uri baseUri, XNamespace wms, XNamespace xlink)
    {
        if (!source.HasLegend || source.LegendSize(StyleParameter(style)) is not { } size)
            return null;
        return new XElement(wms + "LegendURL",
            new XAttribute("width", size.Width),
            new XAttribute("height", size.Height),
            new XElement(wms + "Format", TileLegend.Format),
            new XElement(wms + "OnlineResource",
                new XAttribute(xlink + "type", "simple"),
                new XAttribute(xlink + "href", Url(baseUri, style))));
    }

    /// <summary>
    /// The WMTS 1.0.0 <c>LegendURL</c> of <paramref name="style"/>, or
    /// <see langword="null"/> when the source has no legend for it.
    /// </summary>
    public static XElement? WmtsLegendUrl(ITileSource source, string style, Uri baseUri, XNamespace wmts, XNamespace xlink)
    {
        if (!source.HasLegend || source.LegendSize(StyleParameter(style)) is not { } size)
            return null;
        return new XElement(wmts + "LegendURL",
            new XAttribute("format", TileLegend.Format),
            new XAttribute(xlink + "href", Url(baseUri, style)),
            new XAttribute("width", size.Width),
            new XAttribute("height", size.Height));
    }

    /// <summary>
    /// The <c>sld:GetLegendGraphic</c> operation for the WMS capabilities'
    /// <c>Request</c>, as an extended operation, or <see langword="null"/>
    /// when the source has no legend.
    /// </summary>
    public static XElement? WmsOperation(ITileSource source, string endpoint, XNamespace wms, XNamespace xlink)
    {
        if (!source.HasLegend)
            return null;
        return new XElement(Sld + RequestName,
            new XAttribute(XNamespace.Xmlns + "sld", Sld),
            new XElement(wms + "Format", TileLegend.Format),
            new XElement(wms + "DCPType",
                new XElement(wms + "HTTP",
                    new XElement(wms + "Get",
                        new XElement(wms + "OnlineResource",
                            new XAttribute(xlink + "type", "simple"),
                            new XAttribute(xlink + "href", endpoint))))));
    }

    /// <summary>A GetLegendGraphic request, parsed and checked.</summary>
    /// <param name="Style">The style, or <see langword="null"/> for the default.</param>
    /// <param name="Options">The glyph size and DPI.</param>
    internal readonly record struct LegendRequest(string? Style, TileLegend.Options Options);

    /// <summary>
    /// Reads a GetLegendGraphic request's parameters against
    /// <paramref name="source"/>. Exactly one of the results is set.
    /// </summary>
    internal static (LegendRequest? Request, WmsService.WmsError? Error) Parse(ITileSource source, Func<string, string?> get)
    {
        var layer = get("LAYER");
        if (string.IsNullOrWhiteSpace(layer))
            return Fail(null, "The LAYER parameter is required.");
        if (!string.Equals(layer.Trim(), WmsService.LayerName, StringComparison.Ordinal))
            return Fail("LayerNotDefined", $"Unknown layer '{layer}'; this server has '{WmsService.LayerName}'.");

        var style = StyleParameter(get("STYLE"));
        if (style is not null && !source.Palettes.Contains(style))
            return Fail("StyleNotDefined", $"Unknown style '{get("STYLE")}'.");

        var format = get("FORMAT");
        if (!string.IsNullOrWhiteSpace(format) && !string.Equals(format.Trim(), TileLegend.Format, StringComparison.OrdinalIgnoreCase))
            return Fail("InvalidFormat", $"Format '{format}' isn't served; legend graphics are {TileLegend.Format}.");

        int width = LegendSheetDefaults.GlyphWidth, height = LegendSheetDefaults.GlyphHeight;
        if (get("WIDTH") is { Length: > 0 } w && !TryParseSize(w, out width))
            return Fail(null, $"WIDTH '{w}' must be a whole number of pixels from 1 to {MaxGlyphSize}.");
        if (get("HEIGHT") is { Length: > 0 } h && !TryParseSize(h, out height))
            return Fail(null, $"HEIGHT '{h}' must be a whole number of pixels from 1 to {MaxGlyphSize}.");

        var (dpi, dpiError) = WmsService.ParseDpi(get);
        if (dpiError is not null)
            return Fail(null, dpiError);

        return (new LegendRequest(style, new TileLegend.Options(width, height, dpi)), null);

        static (LegendRequest?, WmsService.WmsError?) Fail(string? code, string message) => (null, new WmsService.WmsError(code, message));
    }

    /// <summary>Answers a GetLegendGraphic request.</summary>
    public static async Task HandleAsync(HttpContext context, ITileSource source, Func<string, string?> get)
    {
        if (!source.HasLegend)
        {
            await ErrorAsync(context, new WmsService.WmsError("OperationNotSupported", "This server has no legend: its tiles have no legend rows, or it serves a built tile set.")).ConfigureAwait(false);
            return;
        }

        var (request, error) = Parse(source, get);
        if (error is { } e)
        {
            await ErrorAsync(context, e).ConfigureAwait(false);
            return;
        }

        var r = request!.Value;
        var png = await source.ReadLegendAsync(r.Style, r.Options, context.RequestAborted).ConfigureAwait(false);
        if (png is null)
        {
            await ErrorAsync(context, new WmsService.WmsError("StyleNotDefined", $"Unknown style '{r.Style}'.")).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = TileLegend.Format;
        context.Response.ContentLength = png.Length;
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.Body.WriteAsync(png, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>A <c>STYLE</c> value as the source takes it: lowercase, <see langword="null"/> for the default.</summary>
    private static string? StyleParameter(string? style) =>
        string.IsNullOrWhiteSpace(style) || string.Equals(style.Trim(), WmsService.DefaultStyle, StringComparison.OrdinalIgnoreCase)
            ? null
            : style.Trim().ToLowerInvariant();

    private static bool TryParseSize(string text, out int size) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out size) && size >= 1 && size <= MaxGlyphSize;

    private static Task ErrorAsync(HttpContext context, WmsService.WmsError error)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/xml; charset=utf-8";
        return context.Response.WriteAsync(WmsService.ExceptionReport(error.Code, error.Message), context.RequestAborted);
    }

    private static class LegendSheetDefaults
    {
        public const int GlyphWidth = Renderers.Skia.LegendSheetStyle.DefaultGlyphWidth;
        public const int GlyphHeight = Renderers.Skia.LegendSheetStyle.DefaultGlyphHeight;
    }
}
