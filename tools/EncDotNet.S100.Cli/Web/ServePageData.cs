using System.Globalization;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.AspNetCore.Http;

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

    /// <summary>
    /// A built tile set's file and how it was made (issue #1584, D35);
    /// <see langword="null"/> when the tiles are rendered from datasets.
    /// </summary>
    BuiltTileSet? Built => null;

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

    /// <summary>
    /// Whether every URL carries an access token (<c>--token</c>, or one made
    /// up when serving beyond this machine), as the last segment of
    /// <see cref="PublicBaseUri"/>. The page masks it (issue #1582, D27).
    /// </summary>
    bool HasToken => false;

    /// <summary>
    /// The base URL <paramref name="request"/> reached the server on, ending
    /// in <c>/</c> and with the token when there is one: the root the
    /// TileJSON's tile URLs and the WMTS and WMS documents are built on, so
    /// the page's links agree with them, <c>X-Forwarded-*</c> included.
    /// </summary>
    Uri PublicBaseUri(HttpRequest request) => TileServer.RequestBaseUri(request);

    /// <summary>
    /// The datasets found but not served (issue #1583, handoff D31): unreadable,
    /// protected, unsupported or missing, each with why. Empty for a built tile set.
    /// </summary>
    IReadOnlyList<SkippedDataset> NotServed => [];

    /// <summary>How often the server checks the datasets' files for changes; <see langword="null"/> when it doesn't.</summary>
    TimeSpan? RefreshInterval => null;

    /// <summary>Whether the datasets are being opened again now (D28's Updating).</summary>
    bool IsUpdating => false;

    /// <summary>
    /// Calls <paramref name="handler"/> as the datasets change (D29): a reload
    /// starting and ending, a dataset added or removed. It's called on the
    /// server's thread, not the component's. Dispose the result to stop.
    /// </summary>
    IDisposable OnDatasetsUpdated(Action<DatasetsUpdate> handler) => NoSubscription.Instance;

    /// <summary>
    /// Calls <paramref name="handler"/> when the server stops cleanly (D28's
    /// Server stopped), and holds the shutdown briefly while it tells the
    /// browser. Dispose the result to stop.
    /// </summary>
    IDisposable OnStopping(Func<Task> handler) => NoSubscription.Instance;
}

/// <summary>A subscription to nothing.</summary>
internal sealed class NoSubscription : IDisposable
{
    /// <summary>The one instance.</summary>
    public static NoSubscription Instance { get; } = new();

    public void Dispose()
    {
    }
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

/// <summary>A built tile set's file and its TileJSON's <c>s100</c> block (issue #1584, D35).</summary>
/// <param name="File">The file (or folder) name.</param>
/// <param name="Container">The container: <c>PMTiles</c>, <c>MBTiles</c>, <c>GeoPackage</c> or <c>XYZ folder</c>.</param>
/// <param name="TileSize">The tiles' size in pixels, 256 or 512.</param>
/// <param name="Generator">What made it, <c>s100 1.2.0</c>, from the <c>s100</c> block; <see langword="null"/> without one.</param>
/// <param name="Palette">The palette it was built in, from the <c>s100</c> block.</param>
/// <param name="Time">The time step it was built at (UTC), from the <c>s100</c> block.</param>
internal sealed record BuiltTileSet(
    string File,
    string Container,
    int TileSize,
    string? Generator,
    string? Palette,
    DateTime? Time);

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
    string? Layer)
{
    /// <summary>A chart cell's edition number (S-101 DSED, S-57 EDTN), when known.</summary>
    public int? EditionNumber { get; init; }

    /// <summary>A chart cell's update number, the last update applied (S-57 UPDN), when known.</summary>
    public int? UpdateNumber { get; init; }

    /// <summary>A chart cell's issue date, when known.</summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>The coarsest scale denominator an S-101 cell's DataCoverage declares, when known.</summary>
    public int? MinimumDisplayScale { get; init; }

    /// <summary>The finest scale denominator an S-101 cell's DataCoverage declares, when known: its purpose band (handoff D8).</summary>
    public int? MaximumDisplayScale { get; init; }

    /// <summary>Whether <see cref="Coverage"/> is the declared data coverage (<see langword="true"/>) or only the bounds.</summary>
    public bool HasDataCoverage { get; init; }
}

/// <summary>The page's data, read from the <see cref="ITileSource"/> being served.</summary>
/// <param name="source">What's served.</param>
/// <param name="hasToken">Whether the server's URLs carry an access token.</param>
/// <param name="stopping">Cancelled when the server stops cleanly; the <see cref="OnStopping"/> handlers run then.</param>
internal sealed class TileSourcePageData(ITileSource source, bool hasToken, CancellationToken stopping = default) : IServePageData
{
    /// <summary>How long a clean shutdown waits for the pages to hear of it.</summary>
    internal static readonly TimeSpan StoppingGrace = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly List<Func<Task>> _stopping = [];
    private bool _watchingStop;

    public bool HasToken => hasToken;

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

    public BuiltTileSet? Built
    {
        get
        {
            if (source is not ITileSetSource set)
                return null;
            var json = set.ToTileJson();
            var s100 = json["s100"] as JsonObject;
            return new BuiltTileSet(
                System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(set.Path)),
                set.Container switch
                {
                    TileContainer.PmTiles => "PMTiles",
                    TileContainer.MbTiles => "MBTiles",
                    TileContainer.GeoPackage => "GeoPackage",
                    _ => "XYZ folder",
                },
                json["tileSize"] is JsonValue size && size.TryGetValue(out int pixels) ? pixels : 256,
                Text(s100?["generator"]),
                Text(s100?["palette"]),
                Time(Text(s100?["time"])));
        }
    }

    public IReadOnlyList<ServedDataset> Datasets =>
        source.ToDatasetsJson()?["datasets"] is JsonArray datasets
            ? [.. datasets.OfType<JsonObject>().Select(Dataset)]
            : [];

    public IReadOnlyList<string> Layers => source.Layers;

    // Under --overview the refreshing source is wrapped; without --refresh or --manage-datasets there's none.
    private RefreshingTileSource? Refreshing => ((source as OverviewTileSource)?.Inner ?? source) as RefreshingTileSource;

    private RenderedTileSource? Rendered => Refreshing?.Current ?? ((source as OverviewTileSource)?.Inner ?? source) as RenderedTileSource;

    public IReadOnlyList<SkippedDataset> NotServed => Rendered?.Session.Skipped ?? [];

    public TimeSpan? RefreshInterval => Refreshing is { Interval: var interval } && interval != Timeout.InfiniteTimeSpan ? interval : null;

    public bool IsUpdating => Refreshing?.IsUpdating ?? false;

    public IDisposable OnDatasetsUpdated(Action<DatasetsUpdate> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Refreshing is not { } refreshing)
            return NoSubscription.Instance;
        refreshing.Updated += handler;
        return new Subscription(() => refreshing.Updated -= handler);
    }

    public IDisposable OnStopping(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _stopping.Add(handler);
            if (!_watchingStop)
            {
                // Once, for the server's life: the registration lasts as long as this.
                _watchingStop = true;
                stopping.Register(TellStopping);
            }
        }

        return new Subscription(() =>
        {
            lock (_gate)
                _stopping.Remove(handler);
        });
    }

    // Runs on the thread stopping the host: hold it until every page has been told, or the grace is up.
    private void TellStopping()
    {
        Func<Task>[] handlers;
        lock (_gate)
            handlers = [.. _stopping];
        var told = Task.WhenAll(handlers.Select(async h =>
        {
            try
            {
                await h().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A page that's gone has nothing to be told.
            }
        }));
        told.Wait(StoppingGrace);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

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
        (string?)d["layer"])
    {
        EditionNumber = (int?)d["editionNumber"],
        UpdateNumber = (int?)d["updateNumber"],
        IssueDate = DateOnly.TryParseExact((string?)d["issueDate"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued) ? issued : null,
        MinimumDisplayScale = (int?)d["displayScale"]?["minimum"],
        MaximumDisplayScale = (int?)d["displayScale"]?["maximum"],
        HasDataCoverage = (string?)d["coverageSource"] == "dataCoverage",
    };

    // TileJSON numbers are doubles or ints (a centre's zoom), as the source built them.
    private static double[]? Numbers(JsonNode? node) =>
        node is JsonArray array && array.All(n => n is JsonValue v && (v.TryGetValue(out double _) || v.TryGetValue(out int _)))
            ? [.. array.Select(n => n!.AsValue().TryGetValue(out double d) ? d : n.GetValue<int>())]
            : null;

    // A string value, or null for anything else (other tools write other metadata).
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static DateTime? Time(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
}
