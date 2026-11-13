using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The OGC WMS 1.3.0 face of <see cref="TileServer"/>: GetCapabilities,
/// GetMap drawn from the served tiles (<see cref="WmsMapComposer"/>), and,
/// when datasets are rendered, GetFeatureInfo answered from the datasets
/// (<see cref="DatasetFeatureQuery"/>), as <c>s100 identify</c> answers it.
/// </summary>
/// <remarks>
/// <para>
/// There is one layer, <c>s100</c>, in EPSG:3857, EPSG:4326 and CRS:84.
/// Palettes are styles, the default first; S-104 and S-111 time steps are a
/// <c>time</c> dimension. Version 1.1.1 requests are answered too: their
/// <c>SRS</c>, <c>X</c> and <c>Y</c> parameters and longitude-first EPSG:4326
/// boxes are read as 1.1.1 defines them. The capabilities are always 1.3.0.
/// </para>
/// <para>
/// A request the service can't answer gets a <c>ServiceExceptionReport</c>
/// with status 400, whatever <c>EXCEPTIONS</c> asks for.
/// </para>
/// </remarks>
internal static class WmsService
{
    /// <summary>The path, under the server's base URL, of the WMS endpoint.</summary>
    public const string Path = "wms";

    /// <summary>The name of the one layer served.</summary>
    public const string LayerName = WmtsService.LayerIdentifier;

    /// <summary>The style of a source without palettes.</summary>
    public const string DefaultStyle = WmtsService.DefaultStyle;

    /// <summary>The image formats GetMap draws.</summary>
    public static readonly IReadOnlyList<string> MapFormats = ["image/png", "image/jpeg"];

    /// <summary>The formats GetFeatureInfo answers in.</summary>
    public static readonly IReadOnlyList<string> InfoFormats = ["text/html", "application/json", "application/geo+json", "text/plain"];

    /// <summary>The CRSs served.</summary>
    public static readonly IReadOnlyList<string> Crss = ["EPSG:3857", "EPSG:4326", "CRS:84"];

    /// <summary>How far from the clicked pixel, in pixels, point and curve features are matched.</summary>
    public const double PickRadiusPixels = 5;

    private static readonly XNamespace Wms = "http://www.opengis.net/wms";
    private static readonly XNamespace Ogc = "http://www.opengis.net/ogc";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    /// <summary>Answers a request to the WMS endpoint.</summary>
    public static async Task HandleAsync(HttpContext context, ITileSource source, Uri baseUri)
    {
        var query = context.Request.Query;
        string? Get(string name) => query.TryGetValue(name, out var values) ? values.ToString() : null;

        var service = Get("SERVICE");
        var request = Get("REQUEST");
        if (string.IsNullOrEmpty(request))
        {
            await ErrorAsync(context, new WmsError(null, "The REQUEST parameter is required.")).ConfigureAwait(false);
            return;
        }

        // GetMap and GetFeatureInfo may leave SERVICE out (1.1.1 allows it);
        // when it's given it must be WMS.
        if (!string.IsNullOrEmpty(service) && !string.Equals(service, "WMS", StringComparison.OrdinalIgnoreCase))
        {
            await ErrorAsync(context, new WmsError(null, $"Unknown service '{service}'; this is WMS.")).ConfigureAwait(false);
            return;
        }

        if (string.Equals(request, "GetCapabilities", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/xml; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsync(BuildCapabilities(source, baseUri), context.RequestAborted).ConfigureAwait(false);
            return;
        }

        bool getMap = string.Equals(request, "GetMap", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request, "map", StringComparison.OrdinalIgnoreCase);
        bool getFeatureInfo = string.Equals(request, "GetFeatureInfo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request, "feature_info", StringComparison.OrdinalIgnoreCase);
        if (!getMap && !getFeatureInfo)
        {
            await ErrorAsync(context, new WmsError("OperationNotSupported", $"Operation '{request}' isn't supported; use GetCapabilities, GetMap or GetFeatureInfo.")).ConfigureAwait(false);
            return;
        }

        var (map, error) = ParseMap(source, Get, requireFormat: getMap);
        if (error is { } e)
        {
            await ErrorAsync(context, e).ConfigureAwait(false);
            return;
        }

        if (getMap)
        {
            var m = map!.Value;
            var image = await WmsMapComposer.ComposeAsync(
                source, m.Box, m.Width, m.Height, m.Palette, m.Time, m.Background, m.Format, context.RequestAborted).ConfigureAwait(false);
            context.Response.ContentType = TileSource.ContentType(m.Format);
            context.Response.ContentLength = image.Length;
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.Body.WriteAsync(image, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await FeatureInfoAsync(context, source, map!.Value, Get).ConfigureAwait(false);
    }

    /// <summary>The capabilities document, with every URL absolute under <paramref name="baseUri"/> (ending in <c>/</c>).</summary>
    public static string BuildCapabilities(ITileSource source, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseUri);

        var json = source.ToTileJson(palette: null);
        int minZoom = (int?)json["minzoom"] ?? 0;
        int tilePixels = (int?)json["tileSize"] ?? 256;
        var bounds = json["bounds"]?.AsArray().Select(b => (double)b!).ToArray() is { Length: 4 } b4
            ? b4
            : [-180.0, -85.0511287798, 180.0, 85.0511287798];
        var (west, south, east, north) = (bounds[0], bounds[1], bounds[2], bounds[3]);
        var (minX, minY) = Rendering.Scene.WebMercator.FromLonLat(west, Math.Max(south, -85.0511287798));
        var (maxX, maxY) = Rendering.Scene.WebMercator.FromLonLat(east, Math.Min(north, 85.0511287798));
        var endpoint = baseUri.AbsoluteUri + Path + "?";
        bool queryable = source.FeatureQuery is not null;
        var styles = Styles(source, json);
        var times = source.Times;

        // Coarser than halfway past the minimum zoom, GetMap draws nothing;
        // clients that read this hide the layer there instead of asking.
        double maxScale = 559082264.0287178 * 256 / tilePixels / Math.Pow(2, minZoom - 0.5);

        var layer = new XElement(Wms + "Layer",
            new XAttribute("queryable", queryable ? "1" : "0"),
            new XAttribute("opaque", "0"),
            new XElement(Wms + "Name", LayerName),
            new XElement(Wms + "Title", (string?)json["name"] ?? "s100 tiles"),
            new XElement(Wms + "Abstract", (string?)json["description"] ?? string.Empty),
            Crss.Select(crs => new XElement(Wms + "CRS", crs)),
            new XElement(Wms + "EX_GeographicBoundingBox",
                new XElement(Wms + "westBoundLongitude", Number(west)),
                new XElement(Wms + "eastBoundLongitude", Number(east)),
                new XElement(Wms + "southBoundLatitude", Number(south)),
                new XElement(Wms + "northBoundLatitude", Number(north))),
            Box("CRS:84", west, south, east, north),
            Box("EPSG:4326", south, west, north, east),
            Box("EPSG:3857", minX, minY, maxX, maxY),
            times.Count > 0
                ? new XElement(Wms + "Dimension",
                    new XAttribute("name", "time"),
                    new XAttribute("units", "ISO8601"),
                    new XAttribute("default", TileRenderSession.FormatTime(DefaultTime(times, json))),
                    new XAttribute("nearestValue", "1"),
                    string.Join(",", times.Select(TileRenderSession.FormatTime)))
                : null,
            styles.Select(style => new XElement(Wms + "Style",
                new XElement(Wms + "Name", style),
                new XElement(Wms + "Title", style))),
            new XElement(Wms + "MaxScaleDenominator", Number(maxScale)));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Wms + "WMS_Capabilities",
                new XAttribute("version", "1.3.0"),
                new XAttribute(XNamespace.Xmlns + "xlink", XLink),
                new XElement(Wms + "Service",
                    new XElement(Wms + "Name", "WMS"),
                    new XElement(Wms + "Title", "s100 tiles serve"),
                    Resource(baseUri.AbsoluteUri),
                    new XElement(Wms + "MaxWidth", WmsMapComposer.MaxSize),
                    new XElement(Wms + "MaxHeight", WmsMapComposer.MaxSize)),
                new XElement(Wms + "Capability",
                    new XElement(Wms + "Request",
                        Operation("GetCapabilities", ["text/xml"], endpoint),
                        Operation("GetMap", MapFormats, endpoint),
                        queryable ? Operation("GetFeatureInfo", InfoFormats, endpoint) : null),
                    new XElement(Wms + "Exception", new XElement(Wms + "Format", "XML")),
                    layer)));

        return Serialize(document);
    }

    /// <summary>A WMS <c>ServiceExceptionReport</c> with <paramref name="code"/> (none for a general error).</summary>
    public static string ExceptionReport(string? code, string text) =>
        Serialize(new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ogc + "ServiceExceptionReport",
                new XAttribute("version", "1.3.0"),
                new XElement(Ogc + "ServiceException",
                    code is null ? null : new XAttribute("code", code),
                    text))));

    /// <summary>A GetMap request (or the map part of a GetFeatureInfo request), parsed and checked.</summary>
    internal readonly record struct WmsMap(
        WmsBox Box, int Width, int Height, string? Palette, DateTime? Time, TileImageFormat Format, SKColor? Background, bool Legacy);

    /// <summary>Why a request was refused: the WMS exception code (none for a general error) and a message.</summary>
    internal readonly record struct WmsError(string? Code, string Message);

    /// <summary>
    /// Reads the map parameters every GetMap and GetFeatureInfo has:
    /// <c>LAYERS</c>, <c>STYLES</c>, <c>CRS</c> (or 1.1.1's <c>SRS</c>),
    /// <c>BBOX</c>, <c>WIDTH</c>, <c>HEIGHT</c>, <c>FORMAT</c>,
    /// <c>TRANSPARENT</c>, <c>BGCOLOR</c> and <c>TIME</c>.
    /// </summary>
    internal static (WmsMap? Map, WmsError? Error) ParseMap(ITileSource source, Func<string, string?> get, bool requireFormat)
    {
        bool legacy = get("VERSION") is { } version && version.StartsWith("1.1", StringComparison.Ordinal);

        var layers = get("LAYERS");
        if (string.IsNullOrWhiteSpace(layers))
            return Fail(null, "The LAYERS parameter is required.");
        var names = layers.Split(',');
        if (names.FirstOrDefault(n => !string.Equals(n.Trim(), LayerName, StringComparison.Ordinal)) is { } unknown)
            return Fail("LayerNotDefined", $"Unknown layer '{unknown}'; this server has '{LayerName}'.");

        string? palette = null;
        var style = (get("STYLES") ?? string.Empty).Split(',').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
        if (style is not null && !string.Equals(style, DefaultStyle, StringComparison.OrdinalIgnoreCase))
        {
            palette = style.ToLowerInvariant();
            if (!source.Palettes.Contains(palette))
                return Fail("StyleNotDefined", $"Unknown style '{style}'.");
        }

        var crsParameter = legacy ? "SRS" : "CRS";
        var crs = get(crsParameter) ?? get(legacy ? "CRS" : "SRS");
        if (string.IsNullOrWhiteSpace(crs))
            return Fail(null, $"The {crsParameter} parameter is required.");
        crs = crs.Trim().ToUpperInvariant();
        if (crs is "EPSG:900913" or "EPSG:102100" or "EPSG:102113" or "EPSG:3785")
            crs = "EPSG:3857";
        if (!Crss.Contains(crs))
            return Fail(legacy ? "InvalidSRS" : "InvalidCRS", $"CRS '{crs}' isn't served; use {string.Join(", ", Crss)}.");

        var bbox = get("BBOX");
        if (string.IsNullOrWhiteSpace(bbox))
            return Fail(null, "The BBOX parameter is required.");
        var parts = bbox.Split(',');
        var numbers = new double[4];
        if (parts.Length != 4 || !parts.Select((p, i) => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])).All(ok => ok)
            || numbers.Any(n => !double.IsFinite(n)))
        {
            return Fail(null, $"BBOX '{bbox}' isn't four numbers.");
        }

        if (numbers[0] >= numbers[2] || numbers[1] >= numbers[3])
            return Fail(null, $"BBOX '{bbox}' has no area: each minimum must be less than its maximum.");

        // WMS 1.3.0 takes EPSG:4326 latitude first; CRS:84 and 1.1.1 longitude first.
        var box = crs switch
        {
            "EPSG:3857" => WmsBox.Mercator(numbers[0], numbers[1], numbers[2], numbers[3]),
            "EPSG:4326" when !legacy => WmsBox.Degrees(numbers[1], numbers[0], numbers[3], numbers[2]),
            _ => WmsBox.Degrees(numbers[0], numbers[1], numbers[2], numbers[3]),
        };

        if (!TryParseSize(get("WIDTH"), out int width))
            return Fail(null, $"WIDTH must be a whole number from 1 to {WmsMapComposer.MaxSize}.");
        if (!TryParseSize(get("HEIGHT"), out int height))
            return Fail(null, $"HEIGHT must be a whole number from 1 to {WmsMapComposer.MaxSize}.");

        var format = TileImageFormat.Png;
        var formatText = get("FORMAT");
        if (string.IsNullOrWhiteSpace(formatText))
        {
            if (requireFormat)
                return Fail(null, "The FORMAT parameter is required.");
        }
        else
        {
            var mediaType = formatText.Split(';')[0].Trim().ToLowerInvariant();
            if (mediaType is "image/png" or "png" or "image/png8")
                format = TileImageFormat.Png;
            else if (mediaType is "image/jpeg" or "image/jpg" or "jpeg" or "jpg")
                format = TileImageFormat.Jpeg;
            else if (requireFormat)
                return Fail("InvalidFormat", $"Format '{formatText}' isn't served; use {string.Join(" or ", MapFormats)}.");
        }

        bool transparent = string.Equals(get("TRANSPARENT"), "TRUE", StringComparison.OrdinalIgnoreCase);
        var background = SKColors.White;
        if (get("BGCOLOR") is { Length: > 0 } color)
        {
            var hex = color.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? color[2..] : color.TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                return Fail(null, $"BGCOLOR '{color}' isn't a 0xRRGGBB colour.");
            background = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        DateTime? time = null;
        if (get("TIME") is { Length: > 0 } timeText
            && !string.Equals(timeText, "current", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(timeText, "default", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return Fail("InvalidDimensionValue", $"TIME '{timeText}' isn't an ISO 8601 time.");
            time = parsed.UtcDateTime;
        }

        return (new WmsMap(box, width, height, palette, time, format,
            transparent && format != TileImageFormat.Jpeg ? null : background, legacy), null);

        static (WmsMap?, WmsError?) Fail(string? code, string message) => (null, new WmsError(code, message));
    }

    private static async Task FeatureInfoAsync(HttpContext context, ITileSource source, WmsMap map, Func<string, string?> get)
    {
        var queryLayers = get("QUERY_LAYERS");
        if (string.IsNullOrWhiteSpace(queryLayers))
        {
            await ErrorAsync(context, new WmsError(null, "The QUERY_LAYERS parameter is required.")).ConfigureAwait(false);
            return;
        }

        if (queryLayers.Split(',').FirstOrDefault(n => !string.Equals(n.Trim(), LayerName, StringComparison.Ordinal)) is { } unknown)
        {
            await ErrorAsync(context, new WmsError("LayerNotDefined", $"Unknown layer '{unknown}'; this server has '{LayerName}'.")).ConfigureAwait(false);
            return;
        }

        if (source.FeatureQuery is not { } featureQuery)
        {
            await ErrorAsync(context, new WmsError("LayerNotQueryable", $"Layer '{LayerName}' is a built tile set, with no features to query.")).ConfigureAwait(false);
            return;
        }

        var infoFormat = (get("INFO_FORMAT") ?? "text/plain").Split(';')[0].Trim().ToLowerInvariant();
        if (!InfoFormats.Contains(infoFormat))
        {
            await ErrorAsync(context, new WmsError("InvalidFormat", $"INFO_FORMAT '{infoFormat}' isn't served; use {string.Join(", ", InfoFormats)}.")).ConfigureAwait(false);
            return;
        }

        var (iName, jName) = map.Legacy ? ("X", "Y") : ("I", "J");
        if (!int.TryParse(get(iName) ?? get(map.Legacy ? "I" : "X"), NumberStyles.None, CultureInfo.InvariantCulture, out int i) || i >= map.Width
            || !int.TryParse(get(jName) ?? get(map.Legacy ? "J" : "Y"), NumberStyles.None, CultureInfo.InvariantCulture, out int j) || j >= map.Height)
        {
            await ErrorAsync(context, new WmsError("InvalidPoint", $"{iName} and {jName} must be a pixel of the {map.Width} × {map.Height} map.")).ConfigureAwait(false);
            return;
        }

        int count = int.TryParse(get("FEATURE_COUNT"), NumberStyles.None, CultureInfo.InvariantCulture, out int requested)
            ? Math.Clamp(requested, 1, 200)
            : 1;
        var (latitude, longitude, pixelMetres) = map.Box.PixelPosition(i, j, map.Width, map.Height);
        var result = await featureQuery.QueryAsync(
            latitude, longitude, Math.Clamp(pixelMetres * PickRadiusPixels, 1, 100_000), count,
            map.Time, context.RequestAborted).ConfigureAwait(false);

        context.Response.Headers.CacheControl = "no-cache";
        switch (infoFormat)
        {
            case "text/html":
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(FeatureInfoHtml(result), context.RequestAborted).ConfigureAwait(false);
                break;
            case "text/plain":
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(FeatureInfoText(result), context.RequestAborted).ConfigureAwait(false);
                break;
            default:
                context.Response.ContentType = infoFormat + "; charset=utf-8";
                await context.Response.WriteAsync(FeatureInfoGeoJson(result).ToJsonString(TileSetMetadata.JsonOptions), context.RequestAborted).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// The result as a GeoJSON FeatureCollection: a feature per vector feature
    /// and per coverage sample, each a point at the queried position (the
    /// query gives no geometry), with what <c>s100 identify --format json</c>
    /// reports as its properties.
    /// </summary>
    internal static JsonObject FeatureInfoGeoJson(FeatureQueryResult result)
    {
        JsonObject Point() => new()
        {
            ["type"] = "Point",
            ["coordinates"] = new JsonArray(result.Longitude, result.Latitude),
        };

        var features = new JsonArray();
        foreach (var (match, attributes) in result.Features)
        {
            var properties = new JsonObject
            {
                ["kind"] = "feature",
                ["datasetId"] = match.DatasetId.Value,
                ["spec"] = match.Spec.Name,
                ["featureId"] = match.FeatureId,
                ["featureType"] = match.FeatureType,
                ["geometry"] = match.Geometry,
                ["containment"] = match.Containment,
                ["distanceMeters"] = match.DistanceMeters,
            };
            if (attributes is { } a)
                properties["attributes"] = JsonNode.Parse(a.GetRawText());
            if (match.ReferencedTexts is { Count: > 0 } texts)
            {
                properties["referencedTexts"] = new JsonArray(texts
                    .Select(t => (JsonNode?)new JsonObject { ["fileName"] = t.FileName, ["text"] = t.Text })
                    .ToArray());
            }

            features.Add((JsonNode)new JsonObject
            {
                ["type"] = "Feature",
                ["id"] = match.DatasetId.Value + "/" + match.FeatureId,
                ["geometry"] = Point(),
                ["properties"] = properties,
            });
        }

        foreach (var sample in result.Samples)
        {
            var properties = new JsonObject
            {
                ["kind"] = "coverage",
                ["datasetId"] = sample.Result.DatasetId.Value,
                ["spec"] = sample.Spec,
            };
            foreach (var (name, value) in IdentifyCommand.SampleToJson(sample.Result.Value))
                properties[name == "kind" ? "sampleKind" : name] = value?.DeepClone();
            features.Add((JsonNode)new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = Point(),
                ["properties"] = properties,
            });
        }

        var collection = new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["features"] = features,
        };
        if (result.Notices.Count > 0)
            collection["notices"] = new JsonArray(result.Notices.Select(n => (JsonNode?)n).ToArray());
        return collection;
    }

    internal static string FeatureInfoText(FeatureQueryResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Pick lat {result.Latitude:0.######}, lon {result.Longitude:0.######}");
        foreach (var notice in result.Notices)
            text.AppendLine(notice);
        if (result.Features.Count == 0 && result.Samples.Count == 0)
        {
            text.AppendLine("Nothing here.");
            return text.ToString();
        }

        foreach (var (match, attributes) in result.Features)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{match.FeatureType} ({match.Geometry}, {match.Containment}{(match.DistanceMeters is { } d ? string.Create(CultureInfo.InvariantCulture, $", {d:0.#} m") : string.Empty)})");
            text.AppendLine(CultureInfo.InvariantCulture, $"  {match.Spec.Name} {match.DatasetId.Value} #{match.FeatureId}");
            if (attributes is { } a)
            {
                if (AttributeRows(a) is { } rows)
                {
                    foreach (var (name, value) in rows)
                        text.AppendLine("  " + name + ": " + value);
                }
                else
                {
                    AppendText(text, a, "  ");
                }
            }
        }

        foreach (var sample in result.Samples)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{sample.Spec} {sample.Result.DatasetId.Value}: {IdentifyCommand.DescribeSample(sample.Result.Value)}");
        }

        return text.ToString();

        static void AppendText(StringBuilder text, JsonElement value, string indent)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject())
                    {
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            text.AppendLine(indent + property.Name + ":");
                            AppendText(text, property.Value, indent + "  ");
                        }
                        else
                        {
                            text.AppendLine(indent + property.Name + ": " + Scalar(property.Value));
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            text.AppendLine(indent + "-");
                            AppendText(text, item, indent + "  ");
                        }
                        else
                        {
                            text.AppendLine(indent + "- " + Scalar(item));
                        }
                    }

                    break;
                default:
                    text.AppendLine(indent + Scalar(value));
                    break;
            }
        }
    }

    internal static string FeatureInfoHtml(FeatureQueryResult result)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>s100 feature info</title>");
        html.Append("<style>body{font:13px system-ui,sans-serif;margin:8px}table{border-collapse:collapse;margin:4px 0 12px}");
        html.Append("th,td{border:1px solid #ccc;padding:2px 6px;text-align:left;vertical-align:top}th{background:#f2f2f2}h3{margin:12px 0 4px}</style></head><body>");
        html.Append(CultureInfo.InvariantCulture, $"<p>Pick lat {result.Latitude:0.######}, lon {result.Longitude:0.######}</p>");
        foreach (var notice in result.Notices)
            html.Append("<p><strong>").Append(Encode(notice)).Append("</strong></p>");
        if (result.Features.Count == 0 && result.Samples.Count == 0)
            html.Append("<p>Nothing here.</p>");

        foreach (var (match, attributes) in result.Features)
        {
            html.Append("<h3>").Append(Encode(match.FeatureType)).Append("</h3><table>");
            Row(html, "Dataset", match.DatasetId.Value);
            Row(html, "Specification", match.Spec.Name);
            Row(html, "Feature ID", match.FeatureId);
            Row(html, "Geometry", match.Geometry);
            Row(html, "Containment", match.Containment);
            if (match.DistanceMeters is { } d)
                Row(html, "Distance (m)", d.ToString("0.#", CultureInfo.InvariantCulture));
            if (attributes is { } described && AttributeRows(described) is { } rows)
            {
                foreach (var (name, value) in rows)
                    Row(html, name, value);
            }
            else if (attributes is { ValueKind: JsonValueKind.Object } a)
            {
                foreach (var property in a.EnumerateObject().Where(p => p.Name != "geometry"))
                {
                    html.Append("<tr><th>").Append(Encode(property.Name)).Append("</th><td>");
                    AppendHtml(html, property.Value);
                    html.Append("</td></tr>");
                }
            }

            html.Append("</table>");
        }

        if (result.Samples.Count > 0)
        {
            html.Append("<h3>Coverage values</h3><table><tr><th>Specification</th><th>Dataset</th><th>Value</th></tr>");
            foreach (var sample in result.Samples)
            {
                html.Append("<tr><td>").Append(Encode(sample.Spec)).Append("</td><td>").Append(Encode(sample.Result.DatasetId.Value))
                    .Append("</td><td>").Append(Encode(IdentifyCommand.DescribeSample(sample.Result.Value))).Append("</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("</body></html>");
        return html.ToString();

        static void Row(StringBuilder html, string name, string value) =>
            html.Append("<tr><th>").Append(Encode(name)).Append("</th><td>").Append(Encode(value)).Append("</td></tr>");

        static void AppendHtml(StringBuilder html, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    html.Append("<table>");
                    foreach (var property in value.EnumerateObject())
                    {
                        html.Append("<tr><th>").Append(Encode(property.Name)).Append("</th><td>");
                        AppendHtml(html, property.Value);
                        html.Append("</td></tr>");
                    }

                    html.Append("</table>");
                    break;
                case JsonValueKind.Array:
                    html.Append("<ul>");
                    foreach (var item in value.EnumerateArray())
                    {
                        html.Append("<li>");
                        AppendHtml(html, item);
                        html.Append("</li>");
                    }

                    html.Append("</ul>");
                    break;
                default:
                    html.Append(Encode(Scalar(value)));
                    break;
            }
        }
    }

    /// <summary>
    /// The attributes of a described feature as name and value rows, for the
    /// text and HTML answers: an encoded product's attribute list (acronym and
    /// value, a complex attribute's row empty above its parts) and a
    /// sounding's depths; <see langword="null"/> when the description has no
    /// such list, to show it whole instead.
    /// </summary>
    internal static List<(string Name, string Value)>? AttributeRows(JsonElement described)
    {
        if (described.ValueKind != JsonValueKind.Object
            || !described.TryGetProperty("attributes", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var rows = new List<(string, string)>();
        foreach (var attribute in list.EnumerateArray())
        {
            if (attribute.ValueKind == JsonValueKind.Object
                && attribute.TryGetProperty("acronym", out var acronym)
                && acronym.ValueKind == JsonValueKind.String)
            {
                rows.Add((acronym.GetString()!, attribute.TryGetProperty("value", out var value) ? Scalar(value) : string.Empty));
            }
        }

        if (described.TryGetProperty("geometry", out var geometry)
            && geometry.ValueKind == JsonValueKind.Object
            && geometry.TryGetProperty("depths", out var depths)
            && depths.ValueKind == JsonValueKind.Array
            && depths.GetArrayLength() > 0)
        {
            var unit = geometry.TryGetProperty("depthUnit", out var u) && u.ValueKind == JsonValueKind.String ? " " + u.GetString() : string.Empty;
            rows.Add(("depth", string.Join(", ", depths.EnumerateArray().Select(Scalar)) + unit));
        }

        return rows;
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => "(none)",
        _ => value.GetRawText(),
    };

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static Task ErrorAsync(HttpContext context, WmsError error)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/xml; charset=utf-8";
        return context.Response.WriteAsync(ExceptionReport(error.Code, error.Message), context.RequestAborted);
    }

    private static bool TryParseSize(string? text, out int size) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out size) && size is >= 1 and <= WmsMapComposer.MaxSize;

    /// <summary>The styles, the default (the <c>--palette</c> one) first, as WMS takes the first as the default.</summary>
    private static List<string> Styles(ITileSource source, JsonObject json)
    {
        if (source.Palettes.Count == 0)
            return [DefaultStyle];

        var styles = source.Palettes.ToList();
        if (json["s100"]?["palette"] is JsonValue value && (string?)value is { } palette && styles.Remove(palette))
            styles.Insert(0, palette);
        return styles;
    }

    private static DateTime DefaultTime(IReadOnlyList<DateTime> times, JsonObject json) =>
        json["s100"]?["timeStep"] is JsonValue step
        && int.TryParse((string?)step, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
        && index >= 0 && index < times.Count
            ? times[index]
            : times[0];

    private static XElement Box(string crs, double minX, double minY, double maxX, double maxY) =>
        new(Wms + "BoundingBox",
            new XAttribute("CRS", crs),
            new XAttribute("minx", Number(minX)),
            new XAttribute("miny", Number(minY)),
            new XAttribute("maxx", Number(maxX)),
            new XAttribute("maxy", Number(maxY)));

    private static XElement Resource(string href) =>
        new(Wms + "OnlineResource",
            new XAttribute(XLink + "type", "simple"),
            new XAttribute(XLink + "href", href));

    private static XElement Operation(string name, IEnumerable<string> formats, string endpoint) =>
        new(Wms + name,
            formats.Select(f => new XElement(Wms + "Format", f)),
            new XElement(Wms + "DCPType",
                new XElement(Wms + "HTTP",
                    new XElement(Wms + "Get", Resource(endpoint)))));

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Serialize(XDocument document)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(builder), new XmlWriterSettings { Indent = true }))
            document.Save(writer);
        return builder.ToString();
    }

    /// <summary>A <see cref="StringWriter"/> that declares UTF-8, so the XML declaration says what the response is.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
