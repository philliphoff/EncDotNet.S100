using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The OGC WMTS 1.0.0 face of <see cref="TileServer"/>: a capabilities
/// document that describes the served tiles as one layer on a Web Mercator
/// tile matrix set, and the parsing of GetTile requests in both the KVP and
/// RESTful encodings. GIS desktops (QGIS, ArcGIS Pro) and OpenLayers can then
/// add the tiles from a single capabilities URL.
/// </summary>
/// <remarks>
/// <para>
/// The tile matrix set is the OGC WebMercatorQuad grid, so a WMTS tile is the
/// XYZ tile of the same zoom, column and row (rows numbered from the north in
/// both). Its matrices run from zoom 0 to the set's maximum zoom, so clients
/// can zoom out over the whole world; below the set's minimum zoom the tiles
/// are empty, as on the XYZ URLs. Tiles drawn at a pixel ratio above 1 are
/// declared at their real pixel size with the scale denominators to match,
/// and the set then has no well-known scale set.
/// </para>
/// <para>
/// Each palette is a WMTS style (the server's default palette is the
/// default style), and time-varying tiles have a <c>Time</c> dimension whose
/// values are the time steps. A built tile set has one style, <c>default</c>.
/// </para>
/// </remarks>
internal static class WmtsService
{
    /// <summary>The path, under the server's base URL, of the RESTful capabilities document.</summary>
    public const string CapabilitiesPath = "wmts/1.0.0/WMTSCapabilities.xml";

    /// <summary>The path, under the server's base URL, of the KVP endpoint.</summary>
    public const string KvpPath = "wmts";

    /// <summary>The identifier of the one layer served.</summary>
    public const string LayerIdentifier = "s100";

    /// <summary>The identifier of the tile matrix set, after OGC 17-083r4's WebMercatorQuad.</summary>
    public const string TileMatrixSetIdentifier = "WebMercatorQuad";

    /// <summary>The style of a source without palettes.</summary>
    public const string DefaultStyle = "default";

    /// <summary>The identifier of the time dimension of time-varying tiles.</summary>
    public const string TimeDimension = "Time";

    private const double WebMercatorHalfExtent = Math.PI * 6378137;

    /// <summary>The OGC standardised rendering pixel size, in metres, that scale denominators are based on.</summary>
    private const double StandardPixelSize = 0.00028;

    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    /// <summary>
    /// The capabilities document for <paramref name="source"/>, with every
    /// URL absolute under <paramref name="baseUri"/> (ending in <c>/</c>).
    /// </summary>
    public static string BuildCapabilities(ITileSource source, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseUri);

        var json = source.ToTileJson(palette: null);
        int minZoom = (int?)json["minzoom"] ?? 0;
        int maxZoom = (int?)json["maxzoom"] ?? minZoom;
        int tileSize = (int?)json["tileSize"] ?? XyzTileGridTileSize;
        var bounds = json["bounds"]?.AsArray().Select(b => (double)b!).ToArray() is { Length: 4 } b4
            ? b4
            : [-180.0, -85.0511287798, 180.0, 85.0511287798];
        var format = TileSource.ContentType(source.Format);
        var extension = TileSetMetadata.FormatToken(source.Format);
        var defaultStyle = DefaultStyleOf(source, json);
        var styles = source.Palettes.Count > 0 ? source.Palettes : [defaultStyle];
        var times = source.Times;

        var kvpUrl = baseUri.AbsoluteUri + KvpPath + "?";
        var restTemplate = baseUri.AbsoluteUri + "wmts/1.0.0/" + LayerIdentifier + "/{Style}/"
            + (times.Count > 0 ? "{" + TimeDimension + "}/" : string.Empty)
            + "{TileMatrixSet}/{TileMatrix}/{TileRow}/{TileCol}." + extension;

        var layer = new XElement(Wmts + "Layer",
            new XElement(Ows + "Title", (string?)json["name"] ?? "s100 tiles"),
            new XElement(Ows + "Abstract", (string?)json["description"] ?? string.Empty),
            new XElement(Ows + "WGS84BoundingBox",
                new XElement(Ows + "LowerCorner", Number(bounds[0]) + " " + Number(bounds[1])),
                new XElement(Ows + "UpperCorner", Number(bounds[2]) + " " + Number(bounds[3]))),
            new XElement(Ows + "Identifier", LayerIdentifier),
            styles.Select(style => new XElement(Wmts + "Style",
                style == defaultStyle ? new XAttribute("isDefault", "true") : null,
                new XElement(Ows + "Title", style),
                new XElement(Ows + "Identifier", style))),
            new XElement(Wmts + "Format", format),
            times.Count > 0
                ? new XElement(Wmts + "Dimension",
                    new XElement(Ows + "Identifier", TimeDimension),
                    new XElement(Ows + "UOM", "ISO8601"),
                    new XElement(Wmts + "Default", TileRenderSession.FormatTime(DefaultTime(times, json))),
                    new XElement(Wmts + "Current", "false"),
                    times.Select(t => new XElement(Wmts + "Value", TileRenderSession.FormatTime(t))))
                : null,
            new XElement(Wmts + "TileMatrixSetLink",
                new XElement(Wmts + "TileMatrixSet", TileMatrixSetIdentifier),
                new XElement(Wmts + "TileMatrixSetLimits",
                    Enumerable.Range(minZoom, maxZoom - minZoom + 1).Select(zoom => Limits(zoom, bounds)))),
            new XElement(Wmts + "ResourceURL",
                new XAttribute("format", format),
                new XAttribute("resourceType", "tile"),
                new XAttribute("template", restTemplate)));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Wmts + "Capabilities",
                new XAttribute("version", "1.0.0"),
                new XAttribute(XNamespace.Xmlns + "ows", Ows),
                new XAttribute(XNamespace.Xmlns + "xlink", XLink),
                new XElement(Ows + "ServiceIdentification",
                    new XElement(Ows + "Title", "s100 tiles serve"),
                    new XElement(Ows + "ServiceType", "OGC WMTS"),
                    new XElement(Ows + "ServiceTypeVersion", "1.0.0")),
                new XElement(Ows + "OperationsMetadata",
                    Operation("GetCapabilities", kvpUrl),
                    Operation("GetTile", kvpUrl)),
                new XElement(Wmts + "Contents",
                    layer,
                    TileMatrixSet(maxZoom, tileSize)),
                new XElement(Wmts + "ServiceMetadataURL",
                    new XAttribute(XLink + "href", baseUri.AbsoluteUri + CapabilitiesPath))));

        return Serialize(document);
    }

    /// <summary>
    /// An OWS exception report, the body WMTS gives a failed request, with
    /// <paramref name="code"/> (e.g. <c>MissingParameterValue</c>) and the
    /// <paramref name="locator"/> parameter it is about.
    /// </summary>
    public static string ExceptionReport(string code, string? locator, string text) =>
        Serialize(new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ows + "ExceptionReport",
                new XAttribute("version", "2.0.0"),
                new XAttribute(XNamespace.Xmlns + "ows", Ows),
                new XElement(Ows + "Exception",
                    new XAttribute("exceptionCode", code),
                    locator is null ? null : new XAttribute("locator", locator),
                    new XElement(Ows + "ExceptionText", text)))));

    private static string Serialize(XDocument document)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(builder), new XmlWriterSettings { Indent = true }))
            document.Save(writer);
        return builder.ToString();
    }

    /// <summary>A GetTile request, parsed and checked against the source.</summary>
    /// <param name="Zoom">The tile matrix, which is the XYZ zoom.</param>
    /// <param name="X">The tile column.</param>
    /// <param name="Y">The tile row, numbered from the north.</param>
    /// <param name="Palette">The palette the style names, or <see langword="null"/> for the default.</param>
    /// <param name="Time">The requested time, or <see langword="null"/> for the default.</param>
    public readonly record struct TileRequest(int Zoom, int X, int Y, string? Palette, DateTime? Time);

    /// <summary>Why a GetTile request was refused: the OWS exception code, the parameter and a message.</summary>
    public readonly record struct TileRequestError(string Code, string? Locator, string Message);

    /// <summary>
    /// Checks the parts of a GetTile request, as either encoding gives them,
    /// against <paramref name="source"/>. Exactly one of the results is set.
    /// </summary>
    public static (TileRequest? Request, TileRequestError? Error) ParseTile(
        ITileSource source,
        string? layer,
        string? style,
        string? format,
        string? tileMatrixSet,
        string? tileMatrix,
        string? tileRow,
        string? tileCol,
        string? time)
    {
        if (string.IsNullOrEmpty(layer))
            return Missing("LAYER");
        if (!string.Equals(layer, LayerIdentifier, StringComparison.Ordinal))
            return Invalid("LAYER", $"Unknown layer '{layer}'; this server has '{LayerIdentifier}'.");
        if (string.IsNullOrEmpty(tileMatrixSet))
            return Missing("TILEMATRIXSET");
        if (!string.Equals(tileMatrixSet, TileMatrixSetIdentifier, StringComparison.Ordinal))
            return Invalid("TILEMATRIXSET", $"Unknown tile matrix set '{tileMatrixSet}'; this server has '{TileMatrixSetIdentifier}'.");
        if (format is not null && !FormatMatches(format, source.Format))
            return Invalid("FORMAT", $"Format '{format}' isn't served; the tiles are {TileSource.ContentType(source.Format)}.");

        string? palette = null;
        if (!string.IsNullOrEmpty(style) && !string.Equals(style, DefaultStyle, StringComparison.OrdinalIgnoreCase))
        {
            palette = style.ToLowerInvariant();
            if (!source.Palettes.Contains(palette))
                return Invalid("STYLE", $"Unknown style '{style}'.");
        }

        if (tileMatrix is null)
            return Missing("TILEMATRIX");
        if (tileRow is null)
            return Missing("TILEROW");
        if (tileCol is null)
            return Missing("TILECOL");
        if (!TryParse(tileMatrix, out int zoom) || zoom > 26)
            return Invalid("TILEMATRIX", $"Unknown tile matrix '{tileMatrix}'.");
        if (!TryParse(tileRow, out int y) || y >= 1 << zoom)
            return OutOfRange("TILEROW", tileRow, zoom);
        if (!TryParse(tileCol, out int x) || x >= 1 << zoom)
            return OutOfRange("TILECOL", tileCol, zoom);

        DateTime? at = null;
        if (!string.IsNullOrWhiteSpace(time) && !string.Equals(time, "default", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return Invalid(TimeDimension, $"'{time}' isn't an ISO 8601 time.");
            at = parsed.UtcDateTime;
        }

        return (new TileRequest(zoom, x, y, palette, at), null);

        static (TileRequest?, TileRequestError?) Missing(string parameter) =>
            (null, new TileRequestError("MissingParameterValue", parameter, $"The {parameter} parameter is required."));

        static (TileRequest?, TileRequestError?) Invalid(string parameter, string message) =>
            (null, new TileRequestError("InvalidParameterValue", parameter, message));

        static (TileRequest?, TileRequestError?) OutOfRange(string parameter, string value, int zoom) =>
            (null, new TileRequestError("TileOutOfRange", parameter, $"{parameter} '{value}' is outside tile matrix {zoom}."));
    }

    private const int XyzTileGridTileSize = 256;

    private static XElement Operation(string name, string kvpUrl) =>
        new(Ows + "Operation",
            new XAttribute("name", name),
            new XElement(Ows + "DCP",
                new XElement(Ows + "HTTP",
                    new XElement(Ows + "Get",
                        new XAttribute(XLink + "href", kvpUrl),
                        new XElement(Ows + "Constraint",
                            new XAttribute("name", "GetEncoding"),
                            new XElement(Ows + "AllowedValues",
                                new XElement(Ows + "Value", "KVP")))))));

    private static XElement TileMatrixSet(int maxZoom, int tileSize)
    {
        // The WebMercatorQuad scale at zoom 0 for 256-pixel tiles; tiles of
        // another pixel size cover the same ground at a scale to match.
        double zoomZeroScale = 2 * WebMercatorHalfExtent / XyzTileGridTileSize / StandardPixelSize
            * XyzTileGridTileSize / tileSize;
        return new XElement(Wmts + "TileMatrixSet",
            new XElement(Ows + "Identifier", TileMatrixSetIdentifier),
            new XElement(Ows + "SupportedCRS", "urn:ogc:def:crs:EPSG::3857"),
            tileSize == XyzTileGridTileSize
                ? new XElement(Wmts + "WellKnownScaleSet", "urn:ogc:def:wkss:OGC:1.0:GoogleMapsCompatible")
                : null,
            Enumerable.Range(0, maxZoom + 1).Select(zoom => new XElement(Wmts + "TileMatrix",
                new XElement(Ows + "Identifier", zoom.ToString(CultureInfo.InvariantCulture)),
                new XElement(Wmts + "ScaleDenominator", Number(zoomZeroScale / (1L << zoom))),
                new XElement(Wmts + "TopLeftCorner", Number(-WebMercatorHalfExtent) + " " + Number(WebMercatorHalfExtent)),
                new XElement(Wmts + "TileWidth", tileSize),
                new XElement(Wmts + "TileHeight", tileSize),
                new XElement(Wmts + "MatrixWidth", 1L << zoom),
                new XElement(Wmts + "MatrixHeight", 1L << zoom))));
    }

    /// <summary>The rows and columns of zoom level <paramref name="zoom"/> that the bounds cover.</summary>
    private static XElement Limits(int zoom, double[] bounds)
    {
        int n = 1 << zoom;
        int minCol = Column(bounds[0]), maxCol = Column(bounds[2]);
        int minRow = Row(bounds[3]), maxRow = Row(bounds[1]);
        if (maxCol < minCol)
        {
            // Bounds across the antimeridian: every column.
            (minCol, maxCol) = (0, n - 1);
        }

        return new XElement(Wmts + "TileMatrixLimits",
            new XElement(Wmts + "TileMatrix", zoom.ToString(CultureInfo.InvariantCulture)),
            new XElement(Wmts + "MinTileRow", minRow),
            new XElement(Wmts + "MaxTileRow", maxRow),
            new XElement(Wmts + "MinTileCol", minCol),
            new XElement(Wmts + "MaxTileCol", maxCol));

        int Column(double longitude) => Math.Clamp((int)Math.Floor((longitude + 180) / 360 * n), 0, n - 1);

        int Row(double latitude)
        {
            double radians = Math.Clamp(latitude, -85.0511287798, 85.0511287798) * Math.PI / 180;
            double y = (1 - (Math.Asinh(Math.Tan(radians)) / Math.PI)) / 2;
            return Math.Clamp((int)Math.Floor(y * n), 0, n - 1);
        }
    }

    /// <summary>The style served without one: the server's default palette, or <see cref="DefaultStyle"/>.</summary>
    private static string DefaultStyleOf(ITileSource source, JsonObject json) =>
        source.Palettes.Count > 0 && json["s100"]?["palette"] is JsonValue palette && source.Palettes.Contains((string)palette!)
            ? (string)palette!
            : source.Palettes.Count > 0 ? source.Palettes[0] : DefaultStyle;

    /// <summary>The time step served without one: the <c>--time-step</c> index, or the first.</summary>
    private static DateTime DefaultTime(IReadOnlyList<DateTime> times, JsonObject json) =>
        json["s100"]?["timeStep"] is JsonValue step
        && int.TryParse((string?)step, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
        && index >= 0 && index < times.Count
            ? times[index]
            : times[0];

    private static bool FormatMatches(string format, TileImageFormat served) =>
        string.Equals(format, TileSource.ContentType(served), StringComparison.OrdinalIgnoreCase)
        || (served == TileImageFormat.Jpeg && string.Equals(format, "image/jpg", StringComparison.OrdinalIgnoreCase));

    private static bool TryParse(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A <see cref="StringWriter"/> that declares UTF-8, so the XML declaration says what the response is.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
