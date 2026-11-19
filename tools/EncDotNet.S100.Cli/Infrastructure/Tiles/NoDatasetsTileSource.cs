using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// What <c>tiles serve --manage-datasets</c> serves once every dataset has
/// been removed (issue #1601, handoff D32): no tiles (each answers 204), a
/// TileJSON without bounds that says nothing is served, an empty
/// <c>/datasets</c> list, and a revision of its own so clients see the change.
/// The palettes, zoom range and tile size are those of the datasets served
/// before, so the WMTS and WMS capabilities stay valid documents.
/// </summary>
internal sealed class NoDatasetsTileSource : ITileSource
{
    /// <summary>What the TileJSON and capabilities say is served.</summary>
    public const string Description = "No datasets are served. Add one with a POST to the datasets URL.";

    private readonly JsonObject _tileJson;

    /// <param name="previous">The datasets served before, whose palettes, zoom range and tile size are kept.</param>
    /// <param name="revision">The revision while nothing is served: the fingerprint of the start-up datasets with every one removed.</param>
    public NoDatasetsTileSource(RenderedTileSource previous, string revision)
        : this(previous.ToTileJson(palette: null), previous.Format, previous.MarinerStyles, revision)
    {
    }

    /// <param name="previous">The TileJSON of the datasets served before.</param>
    /// <param name="format">The encoding the tiles would have.</param>
    /// <param name="marinerStyles">The mariner settings sets registered, which stay registered.</param>
    /// <param name="revision">The revision while nothing is served.</param>
    public NoDatasetsTileSource(JsonObject previous, TileImageFormat format, MarinerStyleRegistry? marinerStyles, string revision)
    {
        ArgumentNullException.ThrowIfNull(previous);
        Format = format;
        MarinerStyles = marinerStyles;
        Revision = revision;

        var s100 = new JsonObject
        {
            ["revision"] = revision,
            ["palettes"] = new JsonArray([.. RenderedTileSource.AllPalettes.Select(p => (JsonNode?)p)]),
        };
        if (previous["s100"]?["palette"] is JsonValue palette)
            s100["palette"] = palette.DeepClone();

        // No bounds or centre: there is no coverage.
        _tileJson = new JsonObject
        {
            ["tilejson"] = "3.0.0",
            ["name"] = "s100 tiles",
            ["description"] = Description,
            ["version"] = "1.0.0",
            ["scheme"] = "xyz",
            ["format"] = TileSetMetadata.FormatToken(format),
            ["type"] = "overlay",
            ["minzoom"] = previous["minzoom"]?.DeepClone() ?? 0,
            ["maxzoom"] = previous["maxzoom"]?.DeepClone() ?? 0,
            ["tileSize"] = previous["tileSize"]?.DeepClone() ?? Rendering.Scene.XyzTileGrid.TileSize,
            ["s100"] = s100,
        };
    }

    public string Path => "no datasets";

    public TileImageFormat Format { get; }

    public IReadOnlyList<string> Palettes =>
        MarinerStyles is { } styles
            ? [.. RenderedTileSource.AllPalettes, .. styles.Styles(RenderedTileSource.AllPalettes)]
            : RenderedTileSource.AllPalettes;

    public MarinerStyleRegistry? MarinerStyles { get; }

    public bool HasDatasets => true;

    public string? Revision { get; }

    public JsonObject? ToDatasetsJson() => new() { ["notForNavigation"] = true, ["datasets"] = new JsonArray() };

    public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken) =>
        ValueTask.FromResult<byte[]?>(null);

    // 512-pixel tiles stay advertised, as before the last dataset went; they are empty too.
    public bool CanReadPixelRatio(int pixelRatio) => RenderedTileSource.IsPixelRatio(pixelRatio);

    public JsonObject ToTileJson(string? palette, DateTime? time = null) => (JsonObject)_tileJson.DeepClone();
}
