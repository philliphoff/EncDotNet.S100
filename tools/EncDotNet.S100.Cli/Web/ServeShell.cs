using System.Globalization;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Web;

/// <summary>The panels the rail (desktop) or the sheet's tabs (phone) open, one at a time (handoff D1, D2).</summary>
public enum ServePanel
{
    /// <summary>What's served (SW2 fills it).</summary>
    Datasets,

    /// <summary>Palette, layers, basemap, times and page appearance (SW3 fills it).</summary>
    Display,

    /// <summary>Links for other map clients (SW6 fills it).</summary>
    UseInGis,

    /// <summary>A built tile set's metadata, in place of Datasets (issue #1584, D35).</summary>
    TileSet,
}

/// <summary>The phone sheet's three heights (handoff D2).</summary>
public enum SheetHeight
{
    /// <summary>The handle, the served summary and the tabs.</summary>
    Lowered,

    /// <summary>About 430 px: the panel with the map above it.</summary>
    Half,

    /// <summary>The map shows 60 px above it.</summary>
    Full,
}

/// <summary>Where the map's zoom is against the zooms the server renders (handoff D13, D14).</summary>
public enum ZoomLimit
{
    /// <summary>The chart draws.</summary>
    Within,

    /// <summary>Below the minimum zoom: no chart, the coverages are outlined.</summary>
    BelowMinimum,

    /// <summary>Past the maximum zoom: the last zoom's tiles, enlarged.</summary>
    PastMaximum,
}

/// <summary>The page appearance setting (handoff D17, D18).</summary>
public enum PageAppearance
{
    /// <summary>Follow the browser (with the Day palette).</summary>
    Auto,

    /// <summary>Light chrome; also overrides Dusk.</summary>
    Light,

    /// <summary>Dark chrome.</summary>
    Dark,
}

/// <summary>The server's state, as the header's pill shows it (handoff D28; SW7 makes it live).</summary>
public enum ServerState
{
    /// <summary>Connected, nothing changing.</summary>
    Live,

    /// <summary>Datasets reloading or the revision changing.</summary>
    Updating,

    /// <summary>The live connection dropped, but the server answers.</summary>
    Reconnecting,

    /// <summary>The server doesn't answer.</summary>
    Disconnected,

    /// <summary>The server closed the connection cleanly.</summary>
    Stopped,
}

/// <summary>What the page says is served: the header's summary and the browser tab (handoff D3, Q1).</summary>
internal static class ServeSummary
{
    /// <summary>The page's name.</summary>
    public const string Name = "s100 serve";

    /// <summary>"3 datasets · S-101", "No datasets", or a built tile set's name and container, "Solent approaches · PMTiles" (D35).</summary>
    public static string Describe(IServePageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.HasDatasets)
            return data.Built is { } built ? data.TileSet.Name + " · " + built.Container : data.TileSet.Name;
        var datasets = data.Datasets;
        if (datasets.Count == 0)
            return "No datasets";
        var products = datasets.Select(d => d.Product).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var count = datasets.Count == 1 ? "1 dataset" : datasets.Count.ToString(CultureInfo.InvariantCulture) + " datasets";
        var list = string.Join(", ", products);
        return list.Length > 0 ? count + " · " + list : count;
    }

    /// <summary>The browser tab: "3 datasets · S-101 — s100 serve".</summary>
    public static string Title(IServePageData data) => Describe(data) + " — " + Name;
}

/// <summary>Which chrome the page wears (handoff D18, Q3).</summary>
internal static class ServeTheme
{
    /// <summary>
    /// <c>night</c> with the Night palette, whatever the page setting; with
    /// Dusk, <c>dark</c> unless the page is set Light; otherwise the page
    /// setting, where <c>auto</c> follows the browser.
    /// </summary>
    public static string Resolve(string? palette, PageAppearance page)
    {
        if (string.Equals(palette, "night", StringComparison.OrdinalIgnoreCase))
            return "night";
        if (string.Equals(palette, "dusk", StringComparison.OrdinalIgnoreCase))
            return page == PageAppearance.Light ? "light" : "dark";
        return page switch
        {
            PageAppearance.Light => "light",
            PageAppearance.Dark => "dark",
            _ => "auto",
        };
    }
}

/// <summary>A view of the map.</summary>
/// <param name="Longitude">The centre's longitude, in degrees.</param>
/// <param name="Latitude">The centre's latitude, in degrees.</param>
/// <param name="Zoom">
/// The zoom in the tile set's terms: the XYZ zoom of 256-pixel tiles, the
/// one TileJSON, GIS clients and the readout use (MapLibre's zoom is one less).
/// </param>
/// <param name="Bearing">The bearing in degrees, 0 for north up.</param>
internal readonly record struct MapView(double Longitude, double Latitude, double Zoom, double Bearing = 0);

/// <summary>The map's start view and zoom limits (handoff D12–D14).</summary>
internal static class ServeMapView
{
    private const double TilePixels = 256;
    private const double MaxLatitude = 85.0511287798066;

    /// <summary>The padding around the data when the map is framed on it, in CSS pixels.</summary>
    public const double Padding = 40;

    /// <summary>
    /// The view that frames the data in a <paramref name="width"/> by
    /// <paramref name="height"/> map, clamped to the tile set's zooms so the
    /// chart draws on open (D12). When the fit is below the minimum zoom, the
    /// view is centred on the TileJSON centre instead.
    /// </summary>
    public static MapView Start(ServedTileSet tileSet, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(tileSet);
        double longitude, latitude, fit;
        if (tileSet.Bounds is [var west, var south, var east, var north] && north > south && width > 2 * Padding && height > 2 * Padding)
        {
            if (east < west)
                east += 360; // Across the antimeridian.
            double x0 = X(west), x1 = X(east), y0 = Y(north), y1 = Y(south);
            var dx = Math.Max(x1 - x0, 1e-12);
            var dy = Math.Max(y1 - y0, 1e-12);
            fit = Math.Log2(Math.Min((width - 2 * Padding) / (dx * TilePixels), (height - 2 * Padding) / (dy * TilePixels)));
            longitude = Normalize(Longitude((x0 + x1) / 2));
            latitude = Latitude((y0 + y1) / 2);
        }
        else if (tileSet.Center is [var lon, var lat, ..] center)
        {
            longitude = lon;
            latitude = lat;
            fit = center.Length > 2 ? center[2] : tileSet.MinZoom;
        }
        else
        {
            longitude = 0;
            latitude = 0;
            fit = 1;
        }

        var zoom = Math.Clamp(fit, tileSet.MinZoom, Math.Max(tileSet.MinZoom, tileSet.MaxZoom));
        if (zoom > fit && tileSet.Center is [var centerLon, var centerLat, ..])
        {
            longitude = centerLon;
            latitude = centerLat;
        }

        return new MapView(longitude, Math.Clamp(latitude, -MaxLatitude, MaxLatitude), zoom);
    }

    /// <summary>
    /// Whether the chart draws at <paramref name="zoom"/>. MapLibre rounds
    /// a raster source's zoom, so the tiles of zoom <c>z</c> draw from
    /// <c>z − 0.5</c>.
    /// </summary>
    public static ZoomLimit Limit(double zoom, int minZoom, int maxZoom) =>
        zoom < minZoom - 0.5 ? ZoomLimit.BelowMinimum
        : zoom > maxZoom + 0.5 ? ZoomLimit.PastMaximum
        : ZoomLimit.Within;

    private static double X(double longitude) => (longitude + 180) / 360;

    private static double Y(double latitude)
    {
        var phi = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        return (1 - Math.Log(Math.Tan(phi) + 1 / Math.Cos(phi)) / Math.PI) / 2;
    }

    private static double Longitude(double x) => x * 360 - 180;

    private static double Latitude(double y) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI;

    private static double Normalize(double longitude) => longitude > 180 ? longitude - 360 : longitude;
}

/// <summary>
/// The page's state in the URL hash (handoff D36):
/// <c>#13/50.775/-1.235&amp;palette=night&amp;dataset=…</c>. The view is
/// <c>zoom/latitude/longitude[/bearing]</c> (MapLibre's hash, with the tile
/// set's zoom); the rest are <c>name=value</c> pairs, defaults left out.
/// The hash never reaches the server: the JS module reads and writes it.
/// </summary>
internal static class ServeHash
{
    /// <summary>Reads a hash, with or without its <c>#</c>. Parameters the page doesn't know are kept.</summary>
    public static (MapView? View, IReadOnlyDictionary<string, string> Parameters) Parse(string? hash)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        MapView? view = null;
        if (string.IsNullOrEmpty(hash))
            return (view, parameters);

        var parts = hash.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                view ??= ParseView(part);
                continue;
            }

            var name = Uri.UnescapeDataString(part[..equals]);
            if (name.Length > 0)
                parameters[name] = Uri.UnescapeDataString(part[(equals + 1)..]);
        }

        return (view, parameters);
    }

    /// <summary>The <c>name=value</c> part of a hash, in name order, without the view and its <c>&amp;</c>.</summary>
    public static string FormatParameters(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return string.Join("&", parameters
            .Where(p => p.Key.Length > 0)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => Uri.EscapeDataString(p.Key) + "=" + Escape(p.Value)));
    }

    // Values keep the characters a hash allows (`:` and `,` in times and points).
    private static string Escape(string value) =>
        Uri.EscapeDataString(value).Replace("%3A", ":", StringComparison.Ordinal).Replace("%2C", ",", StringComparison.Ordinal);

    private static MapView? ParseView(string text)
    {
        var numbers = text.Split('/');
        if (numbers.Length is < 3 or > 5)
            return null;
        var values = new double[numbers.Length];
        for (int i = 0; i < numbers.Length; i++)
        {
            if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                return null;
        }

        var (zoom, latitude, longitude) = (values[0], values[1], values[2]);
        if (zoom is < 0 or > 30 || latitude is < -90 or > 90 || longitude is < -540 or > 540)
            return null;
        return new MapView(longitude, latitude, zoom, values.Length > 3 ? values[3] : 0);
    }
}

/// <summary>The coverage outlines the map draws (handoff D13, D15), as GeoJSON for the JS module.</summary>
internal static class ServeOutlines
{
    /// <summary>
    /// A FeatureCollection with a feature per dataset that has a place: its
    /// data coverage, or else its bounds. Each has the dataset's <c>id</c>
    /// and <c>product</c>, and its <c>layer</c> when it's in one, so the map
    /// can outline only the layer chosen (D17).
    /// </summary>
    public static JsonObject FeatureCollection(IEnumerable<ServedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        var features = new JsonArray();
        foreach (var dataset in datasets)
        {
            var geometry = dataset.Coverage?.DeepClone() as JsonObject ?? BoundsPolygon(dataset.Bounds);
            if (geometry is null)
                continue;
            var properties = new JsonObject { ["id"] = dataset.Id, ["product"] = dataset.Product };
            if (dataset.Layer is { } layer)
                properties["layer"] = layer;
            features.Add((JsonNode)new JsonObject
            {
                ["type"] = "Feature",
                ["properties"] = properties,
                ["geometry"] = geometry,
            });
        }

        return new JsonObject { ["type"] = "FeatureCollection", ["features"] = features };
    }

    private static JsonObject? BoundsPolygon(double[]? bounds) =>
        bounds is [var w, var s, var e, var n]
            ? new JsonObject
            {
                ["type"] = "Polygon",
                ["coordinates"] = new JsonArray(new JsonArray(Point(w, s), Point(e, s), Point(e, n), Point(w, n), Point(w, s))),
            }
            : null;

    private static JsonArray Point(double x, double y) => new(x, y);
}
