using EncDotNet.S100.Cli.Infrastructure.Tiles;

namespace EncDotNet.S100.Cli.Web;

/// <summary>Which of the server's endpoints a <see cref="ServeLink"/> is.</summary>
internal enum ServeLinkKind
{
    /// <summary>The XYZ tile URL template.</summary>
    Xyz,

    /// <summary>The TileJSON document.</summary>
    TileJson,

    /// <summary>The WMTS capabilities document.</summary>
    Wmts,

    /// <summary>The WMS capabilities request.</summary>
    Wms,
}

/// <summary>One link of the Use in GIS panel (issue #1582, D26).</summary>
/// <param name="Kind">The endpoint.</param>
/// <param name="Name">Its label: "XYZ tiles"….</param>
/// <param name="Clients">The clients it's for: "QGIS, Leaflet"….</param>
/// <param name="Url">The full URL, with the token when there is one: what Copy copies.</param>
/// <param name="Shown">The URL as the page shows it: with the token masked, unless it's shown.</param>
/// <param name="Hint">What to pick in the client, when the link alone doesn't say: the WMTS layer and style.</param>
internal sealed record ServeLink(ServeLinkKind Kind, string Name, string Clients, string Url, string Shown, string? Hint);

/// <summary>
/// The links the Use in GIS panel offers (issue #1582, D26, D27): built on the
/// server's public base URL, for the layer and palette picked, with the token
/// masked for show.
/// </summary>
internal static class ServeLinks
{
    /// <summary>What the token is shown as (D27).</summary>
    public const string MaskedToken = "••••••••";

    /// <summary>The layer of every dataset, in WMTS and WMS.</summary>
    public const string AllLayer = WmtsService.LayerIdentifier;

    /// <summary>The WMS capabilities request, under the base URL.</summary>
    public const string WmsCapabilitiesPath = WmsService.Path + "?SERVICE=WMS&REQUEST=GetCapabilities";

    /// <summary>
    /// The path under the base URL of the tiles of <paramref name="layer"/>
    /// (<see langword="null"/> for every dataset) in <paramref name="palette"/>
    /// (<see langword="null"/> or the default for the root's), ending in <c>/</c> or empty.
    /// </summary>
    public static string TilesPath(string? layer, string? palette, string? defaultPalette) =>
        (layer is null ? "" : TileServer.LayersPath + "/" + Uri.EscapeDataString(layer) + "/")
        + (palette is null || palette == defaultPalette ? "" : Uri.EscapeDataString(palette) + "/");

    /// <summary>The four links, XYZ, TileJSON, WMTS and WMS, on <paramref name="baseUrl"/> (ending in <c>/</c>).</summary>
    /// <param name="baseUrl">The server's public base URL, with the token when there is one.</param>
    /// <param name="hasToken">Whether the last segment of <paramref name="baseUrl"/> is the token.</param>
    /// <param name="showToken">Whether to show the token rather than mask it.</param>
    /// <param name="format">The tile format token, <c>png</c>….</param>
    /// <param name="layer">The layer picked, or <see langword="null"/> for every dataset.</param>
    /// <param name="palette">The palette picked, or <see langword="null"/> without palettes.</param>
    /// <param name="defaultPalette">The palette the root serves.</param>
    public static IReadOnlyList<ServeLink> Build(string baseUrl, bool hasToken, bool showToken, string format, string? layer, string? palette, string? defaultPalette)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        var tiles = TilesPath(layer, palette, defaultPalette);
        var style = palette ?? WmtsService.DefaultStyle;
        ServeLink Link(ServeLinkKind kind, string name, string clients, string path, string? hint) =>
            new(kind, name, clients, baseUrl + path, Show(baseUrl, path, hasToken, showToken), hint);

        return
        [
            Link(ServeLinkKind.Xyz, "XYZ tiles", "QGIS, Leaflet", tiles + "{z}/{x}/{y}." + format, null),
            Link(ServeLinkKind.TileJson, "TileJSON", "MapLibre", tiles + XyzDirectoryTileSink.TileJsonFileName, null),
            Link(ServeLinkKind.Wmts, "WMTS", "QGIS, ArcGIS", WmtsService.CapabilitiesPath, $"Layer {layer ?? AllLayer}, style {style}"),
            // WMS serves every dataset as one layer.
            Link(ServeLinkKind.Wms, "WMS", "GetFeatureInfo too", WmsCapabilitiesPath, $"Layer {AllLayer}, style {style}"),
        ];
    }

    /// <summary>
    /// <paramref name="path"/> on <paramref name="baseUrl"/> as the page shows
    /// it: with the token, the base's last segment, masked as
    /// <c>…/••••••••/path</c> unless <paramref name="showToken"/> (D27).
    /// </summary>
    public static string Show(string baseUrl, string path, bool hasToken, bool showToken)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        return hasToken && !showToken ? "…/" + MaskedToken + "/" + path : baseUrl + path;
    }
}

/// <summary>The toast after Copy (issue #1582, G2, D27).</summary>
/// <param name="Title">What happened.</param>
/// <param name="Detail">The second line, or <see langword="null"/>.</param>
/// <param name="Icon">The icon's name.</param>
internal sealed record CopiedToast(string Title, string? Detail, string Icon)
{
    /// <summary>The toast after copying (<paramref name="copied"/>) or failing to, with or without the token.</summary>
    public static CopiedToast For(bool copied, bool hasToken) =>
        !copied ? new("Couldn't copy the link", "Your browser didn't allow it. Select the link and copy it instead.", "warn")
        : hasToken ? new("Link copied, with the token", "Share it only with people who should see these charts.", "lock")
        : new("Link copied", null, "check");
}
