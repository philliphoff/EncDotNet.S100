using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Net;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>What a weather service has now, for the Add-to-Library dialog.</summary>
/// <param name="LatestRun">A model service's latest run with every chosen hour published, if found.</param>
/// <param name="Items">How many items an index would list (datasets, or alerts in force).</param>
/// <param name="Detail">A line describing what was found, e.g. ERDDAP's dataset titles; null for none.</param>
public sealed record WeatherServiceSummary(DateTimeOffset? LatestRun, int Items, string? Detail = null);

/// <summary>
/// Indexes a <see cref="WeatherFeedSource"/> (#1075; design note
/// weather-sources.md §6.3) into one online item per model or dataset: the
/// latest run, as one download of the chosen parameters and hours.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// <b>GRIB2 models</b> (NOMADS GRIB filter, AWS <c>noaa-gfs-bdp-pds</c>): runs
/// are tried newest first, every <see cref="WeatherService.CadenceHours"/>
/// back to a day, and the first whose <c>.idx</c> for the last chosen hour
/// answers is the latest run, so a run still being published isn't offered.
/// Its item downloads one request per forecast hour (the filter's subset, or
/// the chosen fields' byte ranges from each file's <c>.idx</c>), appended into
/// one GRIB2 file. The item carries the run (<see cref="S100ForecastFeedIndexer.RunProperty"/>),
/// so a downloaded run is outdated once a newer one is listed, and a newer
/// run's download replaces it.
/// </description></item>
/// <item><description>
/// <b>Alerts</b> (<c>api.weather.gov</c>): the alerts in force for the area's
/// marine zones, read at each index (they are small) for their count and
/// <c>updated</c> time, and downloaded as NWS GeoJSON.
/// </description></item>
/// <item><description>
/// <b>ERDDAP</b>: each dataset's <c>info</c> gives its axes and time coverage;
/// the item downloads a NetCDF subset from now to the last chosen hour (or
/// the dataset's last hours, when it has ended).
/// </description></item>
/// </list>
/// <para>
/// Each item carries the source's licence and attribution
/// (<see cref="WeatherRuns.LicenceProperty"/>, <see cref="WeatherRuns.AttributionProperty"/>).
/// With a downloads folder, pinned runs (<see cref="Pin"/>) are listed as
/// local items beside the latest, and a service that can't be reached lists
/// its downloaded run instead (stale, with the error). Every request sends a
/// <c>User-Agent</c> (<see cref="UserAgent"/>), as the NWS API requires.
/// Weather sources are re-read on every index (no fingerprint): finding the
/// latest run is the index.
/// </para>
/// </remarks>
public sealed class WeatherFeedIndexer : ICollectionSourceIndexer
{
    /// <summary>How many runs back the latest run is looked for.</summary>
    public const int RunsTried = 5;

    private readonly HttpClient _http;
    private readonly string? _downloadsRoot;
    private readonly TimeProvider _time;

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client requests are made with.</param>
    /// <param name="downloadsDirectory">The hosts' downloads root (pinned and downloaded runs are listed from it), or null.</param>
    /// <param name="timeProvider">The clock; the system clock when null.</param>
    public WeatherFeedIndexer(HttpClient httpClient, string? downloadsDirectory = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _http = httpClient;
        _downloadsRoot = downloadsDirectory;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The <c>User-Agent</c> sent when the client sets none; <see cref="LibraryHttp.DefaultUserAgent"/> by default.</summary>
    public string UserAgent { get; init; } = LibraryHttp.DefaultUserAgent;

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is WeatherFeedSource;

    /// <inheritdoc/>
    /// <remarks>Always null: a weather source is re-read on every index.</remarks>
    public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(null);

    /// <inheritdoc/>
    public async ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        var diagnostics = new List<IndexDiagnostic>();
        var items = new List<CollectionItem>();
        progress?.Report(new IndexProgress(0, feed.ServiceUri.AbsoluteUri));
        if (feed.Service.Kind is WeatherServiceKind.GribFilter or WeatherServiceKind.ErddapGriddap
            && feed.Selection.Area is { } chosen && RequestArea(feed) is { } kept && kept != chosen)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Warning, string.Create(CultureInfo.InvariantCulture,
                $"The area crosses the antimeridian (180°), which weather grids can't cross yet; only its side from {kept.West}° to {kept.East}° is downloaded."),
                feed.ServiceUri.AbsoluteUri));
        }

        try
        {
            switch (feed.Service.Kind)
            {
                case WeatherServiceKind.GribFilter or WeatherServiceKind.GribIndex:
                    if (await FindLatestRunAsync(feed, cancellationToken).ConfigureAwait(false) is { } run)
                        items.Add(GribItem(feed, run));
                    else
                        diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, NoRunMessage(feed), feed.ServiceUri.AbsoluteUri));
                    break;
                case WeatherServiceKind.NwsAlerts:
                    items.Add(await AlertsItemAsync(feed, cancellationToken).ConfigureAwait(false));
                    break;
                case WeatherServiceKind.ErddapGriddap:
                    foreach (var dataset in feed.Selection.DatasetsOf(feed.Service))
                    {
                        try
                        {
                            items.Add(await ErddapItemAsync(feed, dataset, cancellationToken).ConfigureAwait(false));
                        }
                        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
                        {
                            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, $"{dataset.Id}: {ex.Message}",
                                WeatherRequests.ErddapInfoUri(feed.ServiceUri, dataset.Id).AbsoluteUri));
                            if (DownloadedItem(feed, dataset.Id) is { } offline)
                                items.Add(offline);
                        }
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, feed.ServiceUri.AbsoluteUri));
            if (DownloadedItem(feed, NameOf(feed)) is { } offline)
                items.Add(offline);
        }

        items.AddRange(PinnedItems(feed));
        progress?.Report(new IndexProgress(items.Count, null));
        var latest = items.Select(S100ForecastFeedIndexer.RunOf).Max();
        return new SourceIndex(source.Id, _time.GetUtcNow(), null, items, diagnostics) { PublishedAt = latest };
    }

    /// <summary>
    /// What <paramref name="feed"/>'s service has now (its latest run, the
    /// alerts in force, the datasets' titles), for the Add-to-Library dialog.
    /// </summary>
    /// <exception cref="HttpRequestException">The service can't be reached.</exception>
    public async Task<WeatherServiceSummary> DescribeAsync(WeatherFeedSource feed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        switch (feed.Service.Kind)
        {
            case WeatherServiceKind.GribFilter or WeatherServiceKind.GribIndex:
                var run = await FindLatestRunAsync(feed, cancellationToken).ConfigureAwait(false);
                return run is null ? throw new HttpRequestException(NoRunMessage(feed)) : new WeatherServiceSummary(run, 1);
            case WeatherServiceKind.NwsAlerts:
                var alerts = await AlertsItemAsync(feed, cancellationToken).ConfigureAwait(false);
                return new WeatherServiceSummary(null, int.Parse(alerts.Properties[AlertCountProperty], CultureInfo.InvariantCulture));
            default:
                var titles = new List<string>();
                foreach (var dataset in feed.Selection.DatasetsOf(feed.Service))
                {
                    var info = await ReadErddapInfoAsync(feed.ServiceUri, dataset.Id, cancellationToken).ConfigureAwait(false);
                    titles.Add(info.Title ?? dataset.Name);
                }

                return new WeatherServiceSummary(null, titles.Count, string.Join("; ", titles));
        }
    }

    /// <summary>The item property holding how many alerts an alerts item has.</summary>
    public const string AlertCountProperty = "alerts";

    /// <summary>
    /// Keeps the downloaded copy of <paramref name="item"/>'s run (a weather
    /// item, through its source's downloader) beside the latest, so a later run
    /// doesn't replace it: the copy is listed by the next index as a pinned,
    /// local item.
    /// </summary>
    /// <returns>The pinned copy's download name, or null when the item has no downloaded copy.</returns>
    public static string? Pin(EncCellDownloader downloader, CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(item);
        if (downloader.TryGetDownloaded(item.Name) is not { PublishedAt: { } run })
            return null;
        var name = WeatherRuns.PinnedName(item.Name, run);
        return downloader.Copy(item.Name, name) ? name : null;
    }

    /// <summary>Deletes a pinned copy (a <see cref="WeatherRuns.IsPinned"/> item); true when one was deleted.</summary>
    public static bool Unpin(EncCellDownloader downloader, CollectionItem pinned)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(pinned);
        return WeatherRuns.IsPinned(pinned) && downloader.Delete(pinned.Name);
    }

    /// <summary>
    /// The managed folder, relative to the downloads root, a source's runs
    /// download into: one per source, e.g. <c>weather/nomads.ncep.noaa.gov-3f2a9c1b</c>.
    /// </summary>
    public static string DownloadFolderFor(WeatherFeedSource feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(feed.ServiceUri.AbsoluteUri + "|" + feed.Id.ToString("N"))))[..8]
            .ToLowerInvariant();
        var host = new string(feed.ServiceUri.Host.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
        return $"weather/{host}-{hash}";
    }

    /// <summary>
    /// The latest run of a GRIB2 source whose last chosen hour is published,
    /// newest first over <see cref="RunsTried"/> cycles; null when none is.
    /// </summary>
    /// <exception cref="HttpRequestException">The server can't be reached (as opposed to a run not being there).</exception>
    public async Task<DateTimeOffset?> FindLatestRunAsync(WeatherFeedSource feed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var hours = feed.Selection.HoursOf(feed.Service);
        var last = hours.Count > 0 ? hours[^1] : 0;
        foreach (var run in WeatherRequests.CandidateRuns(_time.GetUtcNow(), feed.Service.CadenceHours, RunsTried))
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, WeatherRequests.IndexUri(feed.Service, run, last));
            Identify(request);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return run;
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone))
                response.EnsureSuccessStatusCode();
        }

        return null;
    }

    private CollectionItem GribItem(WeatherFeedSource feed, DateTimeOffset run)
    {
        var service = feed.Service;
        var parameters = feed.Selection.ParametersOf(service);
        var hours = feed.Selection.HoursOf(service);
        var area = service.Kind == WeatherServiceKind.GribFilter ? RequestArea(feed) : null;
        var parts = hours.Select(hour => service.Kind == WeatherServiceKind.GribFilter
                ? new RemotePart(WeatherRequests.GribFilterUri(feed.ServiceUri, service, parameters, area, run, hour))
                : new RemotePart(WeatherRequests.FileUri(service, run, hour), [.. parameters.Select(p => p.IndexMatch)]))
            .ToArray();
        var name = NameOf(feed);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"{name}.t{run.UtcDateTime:HH}z.{run.UtcDateTime:yyyyMMdd}.grib2");
        var properties = Stamp(feed, run, run.AddHours(hours.Count > 0 ? hours[^1] : 0));
        properties["parameters"] = string.Join(", ", parameters.Select(p => p.Id));
        properties["hours"] = hours.Count == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{hours[0]}–{hours[^1]} h");

        return new CollectionItem
        {
            Key = name,
            ProductSpec = "GRIB2",
            Name = name,
            Title = service.ModelName ?? service.Model,
            IssueDate = DateOnly.FromDateTime(run.UtcDateTime),
            Bounds = AreaOf(feed, area),
            Location = new RemoteItemLocation(parts[0].Uri, null, run, DownloadFolderFor(feed), Layout: new PackageLayout(fileName, []))
            {
                Parts = parts,
            },
            Properties = properties,
        };
    }

    private async Task<CollectionItem> AlertsItemAsync(WeatherFeedSource feed, CancellationToken cancellationToken)
    {
        var uri = WeatherRequests.NwsAlertsUri(feed.ServiceUri, feed.Selection.Zones);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        Identify(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GeoJsonMediaType));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var count = root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array ? features.GetArrayLength() : 0;
        DateTimeOffset? updated = root.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(u.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : _time.GetUtcNow();

        var name = NameOf(feed);
        var properties = Stamp(feed, run: null, validTo: null);
        properties[AlertCountProperty] = count.ToString(CultureInfo.InvariantCulture);
        if (feed.Selection.Zones.Count > 0)
            properties["zones"] = string.Join(", ", feed.Selection.Zones);
        return new CollectionItem
        {
            Key = name,
            ProductSpec = "CAP",
            Name = name,
            Title = feed.Service.ModelName ?? "Marine alerts",
            IssueDate = DateOnly.FromDateTime(updated.Value.UtcDateTime),
            Bounds = AreaOf(feed, feed.Selection.Area),
            Location = new RemoteItemLocation(uri, null, updated, DownloadFolderFor(feed), Layout: new PackageLayout(name + ".json", []))
            {
                Parts = [new RemotePart(uri)],
                Accept = GeoJsonMediaType,
            },
            Properties = properties,
        };
    }

    private async Task<CollectionItem> ErddapItemAsync(WeatherFeedSource feed, WeatherServiceDataset dataset, CancellationToken cancellationToken)
    {
        var info = await ReadErddapInfoAsync(feed.ServiceUri, dataset.Id, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var hours = feed.Selection.HoursOf(feed.Service);
        var ahead = TimeSpan.FromHours(hours.Count > 0 ? Math.Max(hours[^1], 1) : feed.Service.DefaultHours);
        var end = info.End is { } last && last < now + ahead ? last : now + ahead;
        var start = info.End is { } ended && ended < now ? ended - ahead : now - TimeSpan.FromHours(Math.Max(feed.Service.StepHours, 1));
        if (info.Start is { } first && start < first)
            start = first;
        if (end < start)
            end = start;

        var area = RequestArea(feed);
        var uri = WeatherRequests.ErddapGriddapUri(feed.ServiceUri, dataset, info.Axes, area, start, end);

        // An issue date counts as the run only within the forecast it describes: NWW3_Global_Best
        // carries the dataset's own dates (date_issued 2010, date_modified 2021), not its run's.
        var run = info.Issued is { } stated && (info.End is not { } coverageEnd
            || stated >= coverageEnd - TimeSpan.FromHours(Math.Max(feed.Service.MaxHours, 24)))
                ? stated
                : (DateTimeOffset?)null;
        var issued = run ?? info.End ?? now;
        var properties = Stamp(feed, run, run is null ? null : end);
        if (run is null)
            properties.Remove(WeatherRuns.StaleAfterProperty);
        properties["variables"] = string.Join(", ", dataset.Variables);
        return new CollectionItem
        {
            Key = dataset.Id,
            ProductSpec = "NetCDF",
            Name = dataset.Id,
            Title = info.Title ?? dataset.Name,
            IssueDate = DateOnly.FromDateTime(issued.UtcDateTime),
            Bounds = AreaOf(feed, area),
            Location = new RemoteItemLocation(uri, null, issued, DownloadFolderFor(feed), Layout: new PackageLayout(dataset.Id + ".nc", []))
            {
                Parts = [new RemotePart(uri)],
            },
            Properties = properties,
        };
    }

    /// <summary>An ERDDAP dataset's metadata, from <c>info/&lt;id&gt;/index.json</c>.</summary>
    internal sealed record ErddapInfo(
        IReadOnlyList<ErddapAxis> Axes, DateTimeOffset? Start, DateTimeOffset? End, DateTimeOffset? Issued, string? Title);

    internal async Task<ErddapInfo> ReadErddapInfoAsync(Uri erddapUri, string datasetId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WeatherRequests.ErddapInfoUri(erddapUri, datasetId));
        Identify(request);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseErddapInfo(document.RootElement);
    }

    /// <summary>
    /// Reads ERDDAP's <c>info</c> table (rows of row type, variable, attribute,
    /// data type and value): the axes in order (<c>dimension</c> rows; a
    /// negative <c>averageSpacing</c> is a descending axis), their
    /// <c>actual_range</c>, and the global <c>time_coverage_start</c>/<c>_end</c>,
    /// <c>title</c> and issue date (<c>date_issued</c>, else <c>date_modified</c>, else <c>date_created</c>).
    /// </summary>
    internal static ErddapInfo ParseErddapInfo(JsonElement root)
    {
        if (!root.TryGetProperty("table", out var table) || !table.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The ERDDAP info has no table.");

        var axes = new List<(string Name, bool Descending)>();
        var ranges = new Dictionary<string, (double Min, double Max)>(StringComparer.Ordinal);
        var globals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5)
                continue;
            string Cell(int i) => row[i].ValueKind == JsonValueKind.String ? row[i].GetString() ?? string.Empty : row[i].ToString();
            var (type, variable, attribute, value) = (Cell(0), Cell(1), Cell(2), Cell(4));
            if (type == "dimension")
            {
                var spacing = value.IndexOf("averageSpacing=", StringComparison.Ordinal);
                axes.Add((variable, spacing >= 0 && value.Length > spacing + 15 && value[spacing + 15] == '-'));
            }
            else if (type == "attribute" && variable == "NC_GLOBAL")
            {
                globals[attribute] = value;
            }
            else if (type == "attribute" && attribute == "actual_range")
            {
                var parts = value.Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                {
                    ranges[variable] = (Math.Min(min, max), Math.Max(min, max));
                }
            }
        }

        if (axes.Count == 0)
            throw new InvalidDataException("The ERDDAP dataset lists no axes; is it a griddap dataset?");

        DateTimeOffset? Time(string key) => globals.TryGetValue(key, out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                ? t
                : null;

        return new ErddapInfo(
            [.. axes.Select(a => new ErddapAxis(a.Name, a.Descending,
                ranges.TryGetValue(a.Name, out var r) ? r.Min : null, ranges.TryGetValue(a.Name, out var r2) ? r2.Max : null))],
            Time("time_coverage_start"),
            Time("time_coverage_end"),
            Time("date_issued") ?? Time("date_modified") ?? Time("date_created"),
            globals.GetValueOrDefault("title"));
    }

    /// <summary>The source's latest downloaded run, listed while the service can't be reached.</summary>
    private CollectionItem? DownloadedItem(WeatherFeedSource feed, string name)
    {
        if (Downloader(feed)?.TryGetDownloaded(name) is not { } copy)
            return null;
        var properties = Stamp(feed, copy.PublishedAt, null);
        if (feed.Service.Kind == WeatherServiceKind.NwsAlerts || copy.PublishedAt is null)
        {
            properties.Remove(S100ForecastFeedIndexer.RunProperty);
            properties.Remove(WeatherRuns.StaleAfterProperty);
        }

        return new CollectionItem
        {
            Key = name,
            ProductSpec = SpecOf(feed.Service.Kind),
            Name = name,
            Title = feed.Service.ModelName ?? feed.Service.Model,
            IssueDate = copy.PublishedAt is { } published ? DateOnly.FromDateTime(published.UtcDateTime) : null,
            Bounds = AreaOf(feed, RequestArea(feed)),
            Location = copy.Location,
            Properties = properties,
        };
    }

    /// <summary>Pinned runs kept in the source's download folder, as local items.</summary>
    private IEnumerable<CollectionItem> PinnedItems(WeatherFeedSource feed)
    {
        if (Downloader(feed) is not { } downloader)
            yield break;
        foreach (var name in downloader.ListDownloaded())
        {
            if (WeatherRuns.RunOfPinnedName(name, out var itemName) is not { } run || downloader.TryGetDownloaded(name) is not { } copy)
                continue;
            var properties = Stamp(feed, run, null);
            properties[WeatherRuns.PinnedProperty] = "true";
            properties[WeatherRuns.DownloadFolderProperty] = DownloadFolderFor(feed);
            properties.Remove(S100ForecastFeedIndexer.ValidToProperty);
            yield return new CollectionItem
            {
                Key = itemName + "@" + run.ToString("yyyyMMddHH", CultureInfo.InvariantCulture),
                ProductSpec = SpecOf(feed.Service.Kind),
                Name = name,
                Title = string.Create(CultureInfo.InvariantCulture,
                    $"{feed.Service.ModelName ?? feed.Service.Model} {WeatherRequests.FormatRun(run)}"),
                IssueDate = DateOnly.FromDateTime(run.UtcDateTime),
                Bounds = AreaOf(feed, feed.Service.Kind == WeatherServiceKind.GribIndex ? null : RequestArea(feed)),
                Location = copy.Location,
                Properties = properties,
            };
        }
    }

    private EncCellDownloader? Downloader(WeatherFeedSource feed) =>
        _downloadsRoot is { Length: > 0 } root ? new EncCellDownloader(_http, Path.Combine(root, DownloadFolderFor(feed))) : null;

    private static Dictionary<string, string> Stamp(WeatherFeedSource feed, DateTimeOffset? run, DateTimeOffset? validTo)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [S100ForecastFeedIndexer.ModelProperty] = feed.Service.Model,
            [WeatherRuns.ServiceProperty] = JsonNamingPolicy.CamelCase.ConvertName(feed.Service.Kind.ToString()),
            [WeatherRuns.StaleAfterProperty] = WeatherRuns.StaleAfter.TotalHours.ToString(CultureInfo.InvariantCulture),
            ["notForNavigation"] = "true",
        };
        if (run is { } r)
            properties[S100ForecastFeedIndexer.RunProperty] = r.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        if (validTo is { } v)
            properties[S100ForecastFeedIndexer.ValidToProperty] = v.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        if (feed.Licence is { Length: > 0 } licence)
            properties[WeatherRuns.LicenceProperty] = licence;
        if (feed.Attribution is { Length: > 0 } attribution)
            properties[WeatherRuns.AttributionProperty] = attribution;
        return properties;
    }

    /// <summary>
    /// The area a grid service is asked for: the chosen area, cut to one side
    /// of the antimeridian (<see cref="WeatherRequests.OneSideOfAntimeridian"/>);
    /// alerts keep the chosen area, as their zones were picked from it.
    /// </summary>
    private static GeoBounds? RequestArea(WeatherFeedSource feed) =>
        feed.Selection.Area is { } area && feed.Service.Kind is not WeatherServiceKind.NwsAlerts
            ? WeatherRequests.OneSideOfAntimeridian(area, out _)
            : feed.Selection.Area;

    private static GeoBounds AreaOf(WeatherFeedSource feed, GeoBounds? area)
    {
        if (area is not { } box)
            return new GeoBounds(-90, -180, 90, 180);
        var (south, west, north, east) = WeatherRequests.Rounded(box, feed.Service.GridDegrees);
        return new GeoBounds(south, west, north, east > 180 ? east - 360 : east);
    }

    private static string NameOf(WeatherFeedSource feed) => feed.Service.Kind switch
    {
        WeatherServiceKind.NwsAlerts => "nws-marine-alerts",
        _ => Safe(feed.Service.Model),
    };

    private static string Safe(string text) =>
        new(text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());

    private static string SpecOf(WeatherServiceKind kind) => kind switch
    {
        WeatherServiceKind.NwsAlerts => "CAP",
        WeatherServiceKind.ErddapGriddap => "NetCDF",
        _ => "GRIB2",
    };

    private static string NoRunMessage(WeatherFeedSource feed)
    {
        var hours = feed.Selection.HoursOf(feed.Service);
        return string.Create(CultureInfo.InvariantCulture,
            $"No {feed.Service.ModelName ?? feed.Service.Model} run of the last {RunsTried * feed.Service.CadenceHours} hours has forecast hour {(hours.Count > 0 ? hours[^1] : 0)} yet.");
    }

    private void Identify(HttpRequestMessage request)
    {
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            request.Headers.UserAgent.TryParseAdd(UserAgent);
    }

    private static WeatherFeedSource AsFeed(CollectionSource source) => source switch
    {
        WeatherFeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(WeatherFeedIndexer)} cannot index {source.GetType().Name}."),
    };

    private const string GeoJsonMediaType = "application/geo+json";
}
