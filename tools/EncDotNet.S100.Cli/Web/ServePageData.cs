using System.Globalization;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure.Tiles;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// What the <c>tiles serve</c> web page shows (issue #1575): the page's one
/// view of what's served. Components read this, never the
/// <see cref="ITileSource"/> itself, so one can move to WebAssembly later and
/// read the same data over HTTP.
/// </summary>
internal interface IServePageData
{
    /// <summary>The tile set: its name, zoom range, bounds and centre, read now (datasets can reload).</summary>
    ServedTileSet TileSet { get; }

    /// <summary>The palettes the tiles are served in; empty when there's one look only.</summary>
    IReadOnlyList<string> Palettes { get; }

    /// <summary>The time steps (UTC, ascending); empty when the tiles don't vary with time.</summary>
    IReadOnlyList<DateTime> Times { get; }

    /// <summary>Whether the tiles are rendered from datasets, rather than a built tile set.</summary>
    bool HasDatasets { get; }

    /// <summary>The datasets the tiles are rendered from, in input order; empty for a built tile set.</summary>
    IReadOnlyList<ServedDataset> Datasets { get; }

    /// <summary>The layers besides the one of every dataset (<c>--layers-by</c>); empty when there are none.</summary>
    IReadOnlyList<string> Layers { get; }

    /// <summary>A token that changes whenever the datasets reload; <see langword="null"/> for a built tile set.</summary>
    string? Revision { get; }

    /// <summary>
    /// The features and coverage values within <paramref name="radiusMeters"/>
    /// of a point, or <see langword="null"/> for a built tile set.
    /// </summary>
    Task<FeatureQueryResult?> QueryFeaturesAsync(double latitude, double longitude, double radiusMeters, int maxResults, DateTime? time, CancellationToken cancellationToken);
}

/// <summary>The tile set as a whole, from its TileJSON.</summary>
/// <param name="Name">The TileJSON name.</param>
/// <param name="Format">The tile encoding: <c>png</c>, <c>jpg</c> or <c>webp</c>.</param>
/// <param name="MinZoom">The lowest zoom with tiles.</param>
/// <param name="MaxZoom">The highest zoom with tiles.</param>
/// <param name="Bounds">West, south, east, north in degrees; <see langword="null"/> when unknown.</param>
/// <param name="Center">Longitude, latitude and zoom; <see langword="null"/> when unknown.</param>
/// <param name="DefaultPalette">The palette the root URLs serve; <see langword="null"/> without palettes.</param>
/// <param name="HighDpi">Whether 512-pixel <c>@2x</c> tiles are served.</param>
internal sealed record ServedTileSet(
    string Name,
    string Format,
    int MinZoom,
    int MaxZoom,
    double[]? Bounds,
    double[]? Center,
    string? DefaultPalette,
    bool HighDpi);

/// <summary>One dataset the tiles are rendered from, as the <c>/datasets</c> document describes it.</summary>
/// <param name="Id">The dataset's id, unique among those served.</param>
/// <param name="File">The file name.</param>
/// <param name="Product">The product, <c>S-101</c>….</param>
/// <param name="Edition">The product edition, when known.</param>
/// <param name="Producer">The producer code, when known.</param>
/// <param name="Bounds">West, south, east, north in degrees; <see langword="null"/> when unknown.</param>
/// <param name="Coverage">The coverage as a GeoJSON MultiPolygon; <see langword="null"/> when unknown.</param>
/// <param name="Times">The dataset's time steps (UTC).</param>
/// <param name="IssueTime">When time-varying data was issued (UTC).</param>
/// <param name="Dynamicity">The data dynamicity label (Forecast, Observed…).</param>
/// <param name="Layer">The layer the dataset is in, besides the one of every dataset.</param>
internal sealed record ServedDataset(
    string Id,
    string File,
    string Product,
    string? Edition,
    string? Producer,
    double[]? Bounds,
    JsonObject? Coverage,
    IReadOnlyList<DateTime> Times,
    DateTime? IssueTime,
    string? Dynamicity,
    string? Layer);

/// <summary>The page's data, read from the <see cref="ITileSource"/> being served.</summary>
internal sealed class TileSourcePageData(ITileSource source) : IServePageData
{
    public ServedTileSet TileSet
    {
        get
        {
            var json = source.ToTileJson(palette: null);
            return new ServedTileSet(
                (string?)json["name"] ?? System.IO.Path.GetFileName(source.Path),
                (string?)json["format"] ?? TileSetMetadata.FormatToken(source.Format),
                (int?)json["minzoom"] ?? 0,
                (int?)json["maxzoom"] ?? 22,
                Numbers(json["bounds"]),
                Numbers(json["center"]),
                source.Palettes.Count > 0 ? (string?)json["s100"]?["palette"] ?? source.Palettes[0] : null,
                source.CanReadPixelRatio(2));
        }
    }

    public IReadOnlyList<string> Palettes => source.Palettes;

    public IReadOnlyList<DateTime> Times => source.Times;

    public bool HasDatasets => source.HasDatasets;

    public IReadOnlyList<ServedDataset> Datasets =>
        source.ToDatasetsJson()?["datasets"] is JsonArray datasets
            ? [.. datasets.OfType<JsonObject>().Select(Dataset)]
            : [];

    public IReadOnlyList<string> Layers => source.Layers;

    public string? Revision => source.Revision;

    public async Task<FeatureQueryResult?> QueryFeaturesAsync(double latitude, double longitude, double radiusMeters, int maxResults, DateTime? time, CancellationToken cancellationToken) =>
        source.FeatureQuery is { } query
            ? await query.QueryAsync(latitude, longitude, radiusMeters, maxResults, time, cancellationToken).ConfigureAwait(false)
            : null;

    private static ServedDataset Dataset(JsonObject d) => new(
        (string?)d["id"] ?? "",
        (string?)d["file"] ?? "",
        (string?)d["product"] ?? "",
        (string?)d["edition"],
        (string?)d["producer"],
        Numbers(d["bounds"]),
        d["coverage"] as JsonObject,
        d["times"] is JsonArray times ? [.. times.Select(t => Time((string?)t)).OfType<DateTime>()] : [],
        Time((string?)d["issueTime"]),
        (string?)d["dataDynamicity"]?["label"],
        (string?)d["layer"]);

    // TileJSON numbers are doubles or ints (a centre's zoom), as the source built them.
    private static double[]? Numbers(JsonNode? node) =>
        node is JsonArray array && array.All(n => n is JsonValue v && (v.TryGetValue(out double _) || v.TryGetValue(out int _)))
            ? [.. array.Select(n => n!.AsValue().TryGetValue(out double d) ? d : n.GetValue<int>())]
            : null;

    private static DateTime? Time(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
}
