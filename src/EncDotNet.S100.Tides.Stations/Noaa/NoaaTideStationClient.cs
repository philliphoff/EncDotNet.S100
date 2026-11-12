using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations.Noaa;

/// <summary>
/// Reads tide stations from the NOAA CO-OPS metadata API: the tide
/// prediction station list with subordinate stations' offsets, and each
/// reference station's harmonic constituents and tidal datums.
/// </summary>
/// <remarks>
/// <para>
/// <b>Requests.</b> All requests ask for metric units. The station list is
/// one request of about 140 KB compressed; a full catalogue adds two
/// requests per reference station, about 2,500 in all, which
/// <see cref="DownloadCatalogueAsync"/> paces (see
/// <see cref="NoaaTideStationClientOptions"/>) because the API refuses a
/// client that asks too fast. Save the result with
/// <see cref="TideStationCatalogueFile.SaveAsync"/> and refresh it only when
/// <see cref="CheckForUpdatesAsync"/> says NOAA has newer data.
/// </para>
/// <para>
/// <b>Data.</b> Constituent phases are NOAA's <c>phase_GMT</c>, referred to
/// Greenwich; constituents with zero amplitude are left out. Datums are
/// heights above the station datum (STND), with LAT and HAT where NOAA gives
/// them. Subordinate offsets are NOAA's tide prediction offsets: minutes,
/// and ratios (<c>R</c>) or metres (<c>F</c>) applied on MLLW. NOAA data is
/// a work of the US Government and in the public domain.
/// </para>
/// </remarks>
public sealed partial class NoaaTideStationClient
{
    /// <summary>The provider name a downloaded catalogue carries.</summary>
    public const string ProviderName = "NOAA CO-OPS";

    private readonly HttpClient _http;
    private readonly NoaaTideStationClientOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Creates a client.</summary>
    /// <param name="httpClient">
    /// The client to fetch with. The caller owns it: its lifetime, timeout
    /// and <c>User-Agent</c>.
    /// </param>
    /// <param name="options">The options; <c>null</c> for the defaults.</param>
    /// <param name="timeProvider">The clock, for retry waits and the catalogue's date; <c>null</c> for the system clock.</param>
    public NoaaTideStationClient(HttpClient httpClient, NoaaTideStationClientOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _options = options ?? new NoaaTideStationClientOptions();
        _options.Validate();
        _http = httpClient;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Reads NOAA's tide prediction station list, with each subordinate
    /// station's reference station and offsets. Harmonic stations come back
    /// without constituents or datums.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stations, in NOAA's order.</returns>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public async Task<IReadOnlyList<TideStationEntry>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAsync(
            "stations.json?type=tidepredictions&expand=tidepredoffsets&units=metric",
            NoaaJsonContext.Default.StationListDto,
            cancellationToken).ConfigureAwait(false);
        var result = new List<TideStationEntry>(list?.Stations?.Count ?? 0);
        foreach (var station in list?.Stations ?? [])
        {
            if (ToEntry(station) is { } entry)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>Reads a station's harmonic constituents.</summary>
    /// <param name="stationId">The station identifier.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The constituents with a non-zero amplitude, with Greenwich phases; empty
    /// when NOAA has none for the station.
    /// </returns>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public async Task<IReadOnlyList<TideConstituent>> GetConstituentsAsync(string stationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stationId);
        var harcon = await GetAsync(
            $"stations/{Uri.EscapeDataString(stationId)}/harcon.json?units=metric",
            NoaaJsonContext.Default.HarconDto,
            cancellationToken).ConfigureAwait(false);
        return ToConstituents(harcon);
    }

    /// <summary>Reads a station's tidal datums.</summary>
    /// <param name="stationId">The station identifier.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The datums, or <c>null</c> when NOAA has none for the station.</returns>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public async Task<StationDatums?> GetDatumsAsync(string stationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(stationId);
        var datums = await GetAsync(
            $"stations/{Uri.EscapeDataString(stationId)}/datums.json?units=metric",
            NoaaJsonContext.Default.DatumsDto,
            cancellationToken).ConfigureAwait(false);
        return ToDatums(datums);
    }

    /// <summary>
    /// Downloads NOAA's whole tide prediction catalogue: the station list,
    /// and each reference station's constituents and datums.
    /// </summary>
    /// <remarks>
    /// About 2,500 requests, paced by <see cref="NoaaTideStationClientOptions.MaxConcurrency"/>
    /// and <see cref="NoaaTideStationClientOptions.RequestInterval"/>, so it
    /// takes some minutes. A reference station NOAA has no constituents for
    /// stays in the catalogue but can't be predicted, and neither can its
    /// subordinate stations.
    /// </remarks>
    /// <param name="progress">Receives progress as reference stations are read; <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The catalogue, dated now.</returns>
    /// <exception cref="HttpRequestException">A request failed after its retries.</exception>
    public async Task<TideStationCatalogue> DownloadCatalogueAsync(
        IProgress<NoaaDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var listing = await GetStationsAsync(cancellationToken).ConfigureAwait(false);
        var references = listing.Where(s => s.Kind == TideStationKind.Harmonic).ToArray();
        var details = new (IReadOnlyList<TideConstituent> Constituents, StationDatums? Datums)[references.Length];
        var completed = 0;
        progress?.Report(new NoaaDownloadProgress(0, references.Length));

        using var gate = new SemaphoreSlim(_options.MaxConcurrency);
        var tasks = references.Select(async (station, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var constituents = await GetConstituentsAsync(station.Id, cancellationToken).ConfigureAwait(false);
                await PauseAsync(cancellationToken).ConfigureAwait(false);
                var datums = await GetDatumsAsync(station.Id, cancellationToken).ConfigureAwait(false);
                await PauseAsync(cancellationToken).ConfigureAwait(false);
                details[index] = (constituents, datums);
            }
            finally
            {
                gate.Release();
            }

            progress?.Report(new NoaaDownloadProgress(Interlocked.Increment(ref completed), references.Length));
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < references.Length; i++)
        {
            byId[references[i].Id] = i;
        }

        var stations = new List<TideStationEntry>(listing.Count);
        foreach (var station in listing)
        {
            if (station.Kind == TideStationKind.Harmonic && byId.TryGetValue(station.Id, out var i))
            {
                stations.Add(new TideStationEntry
                {
                    Id = station.Id,
                    Name = station.Name,
                    Position = station.Position,
                    Kind = station.Kind,
                    Region = station.Region,
                    UtcOffset = station.UtcOffset,
                    Constituents = details[i].Constituents,
                    Datums = details[i].Datums,
                });
            }
            else
            {
                stations.Add(station);
            }
        }

        return new TideStationCatalogue(stations, ProviderName, _time.GetUtcNow());
    }

    /// <summary>
    /// Checks cheaply whether NOAA has published newer data than a
    /// catalogue holds: reads the station list (one request) and the
    /// datums of the <see cref="NoaaTideStationClientOptions.SentinelStationIds"/>,
    /// not every station's constituents.
    /// </summary>
    /// <remarks>
    /// It finds stations NOAA has added, removed or moved, changed
    /// subordinate offsets, a new tidal datum epoch, and sentinel stations
    /// whose datums NOAA has accepted again. A change to one station's
    /// constituents alone isn't seen: NOAA revises those with its datums.
    /// </remarks>
    /// <param name="catalogue">The catalogue to check, as downloaded by <see cref="DownloadCatalogueAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>What NOAA has changed.</returns>
    /// <exception cref="HttpRequestException">A request failed.</exception>
    public async Task<TideCatalogueUpdateCheck> CheckForUpdatesAsync(
        TideStationCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var listing = await GetStationsAsync(cancellationToken).ConfigureAwait(false);

        var added = new List<string>();
        var changed = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var station in listing)
        {
            listed.Add(station.Id);
            if (catalogue.Find(station.Id) is not { } cached)
            {
                added.Add(station.Id);
            }
            else if (HasChanged(cached, station))
            {
                changed.Add(station.Id);
            }
        }

        var removed = catalogue.Stations.Where(s => !listed.Contains(s.Id)).Select(s => s.Id).ToList();

        string? newEpoch = null;
        var revised = new List<string>();
        foreach (var id in _options.SentinelStationIds)
        {
            var datums = await GetDatumsAsync(id, cancellationToken).ConfigureAwait(false);
            var cached = catalogue.Find(id)?.Datums;
            if (datums?.Epoch is { } epoch
                && IsEpoch(epoch)
                && !string.Equals(epoch, catalogue.DatumEpoch, StringComparison.Ordinal)
                && !string.Equals(epoch, cached?.Epoch, StringComparison.Ordinal))
            {
                newEpoch ??= epoch;
            }

            if (datums?.Accepted is { } accepted && (cached?.Accepted is not { } was || accepted > was))
            {
                revised.Add(id);
            }
        }

        return new TideCatalogueUpdateCheck
        {
            Checked = _time.GetUtcNow(),
            NewDatumEpoch = newEpoch,
            AddedStationIds = added,
            RemovedStationIds = removed,
            ChangedStationIds = changed,
            RevisedDatumStationIds = revised,
        };
    }

    private static bool HasChanged(TideStationEntry cached, TideStationEntry listed) =>
        cached.Kind != listed.Kind
        || Math.Abs(cached.Position.Latitude - listed.Position.Latitude) > 1e-4
        || Math.Abs(cached.Position.Longitude - listed.Position.Longitude) > 1e-4
        || !string.Equals(cached.ReferenceStationId, listed.ReferenceStationId, StringComparison.Ordinal)
        || !Equals(cached.Offsets, listed.Offsets);

    /// <summary>Whether an epoch is a range of years, such as <c>1983-2001</c>; NOAA also gives <c>Special</c> or <c>NONE</c>.</summary>
    private static bool IsEpoch(string epoch) =>
        epoch.Length == 9
        && epoch[4] == '-'
        && int.TryParse(epoch.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var from)
        && int.TryParse(epoch.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var to)
        && from > 1800
        && to > from;

    private Task PauseAsync(CancellationToken cancellationToken) =>
        _options.RequestInterval > TimeSpan.Zero
            ? Task.Delay(_options.RequestInterval, _time, cancellationToken)
            : Task.CompletedTask;

    private async Task<T?> GetAsync<T>(string relativeUri, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        var uri = new Uri(_options.BaseAddress, relativeUri);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    // The handler may not decompress for us.
                    var body = response.Content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase)
                        ? new GZipStream(stream, CompressionMode.Decompress)
                        : stream;
                    await using (body.ConfigureAwait(false))
                    {
                        try
                        {
                            return await JsonSerializer.DeserializeAsync(body, typeInfo, cancellationToken).ConfigureAwait(false);
                        }
                        catch (JsonException e)
                        {
                            throw new HttpRequestException($"NOAA returned a response that isn't the expected JSON for {uri}: {e.Message}", e);
                        }
                    }
                }
            }
            catch (Exception e) when (attempt < _options.MaxRetries && IsTransient(e, cancellationToken))
            {
                var delay = _options.RetryDelay * Math.Pow(2, attempt);
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception e, CancellationToken cancellationToken) => e switch
    {
        HttpRequestException { StatusCode: null } => e.InnerException is not JsonException,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500
            || status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout,
        TaskCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    private static TideStationEntry? ToEntry(StationDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id) || dto.Lat is not { } lat || dto.Lng is not { } lng)
        {
            return null;
        }

        var kind = dto.Type switch
        {
            "R" => TideStationKind.Harmonic,
            "S" => TideStationKind.Subordinate,
            _ => (TideStationKind?)null,
        };
        if (kind is null)
        {
            return null;
        }

        SubordinateOffsets? offsets = null;
        string? reference = null;
        if (kind == TideStationKind.Subordinate)
        {
            offsets = ToOffsets(dto.TidePredOffsets);
            reference = string.IsNullOrEmpty(dto.TidePredOffsets?.RefStationId) ? dto.ReferenceId : dto.TidePredOffsets.RefStationId;
            if (offsets is null || string.IsNullOrEmpty(reference))
            {
                return null;
            }
        }

        return new TideStationEntry
        {
            Id = dto.Id,
            Name = dto.Name ?? dto.Id,
            Position = new GeoPosition(lat, lng),
            Kind = kind.Value,
            Region = string.IsNullOrEmpty(dto.State) ? null : dto.State,
            UtcOffset = dto.TimeZoneCorr is { } hours ? TimeSpan.FromHours(hours) : null,
            ReferenceStationId = reference,
            Offsets = offsets,
        };
    }

    private static SubordinateOffsets? ToOffsets(OffsetsDto? dto)
    {
        if (dto is not
            {
                HeightOffsetHighTide: { } highHeight,
                HeightOffsetLowTide: { } lowHeight,
                TimeOffsetHighTide: { } highTime,
                TimeOffsetLowTide: { } lowTime,
            })
        {
            return null;
        }

        SubordinateHeightAdjustment? adjustment = dto.HeightAdjustedType switch
        {
            "R" => SubordinateHeightAdjustment.Ratio,
            "F" => SubordinateHeightAdjustment.Offset,
            _ => null,
        };
        return adjustment is { } a
            ? new SubordinateOffsets(TimeSpan.FromMinutes(highTime), TimeSpan.FromMinutes(lowTime), highHeight, lowHeight, a)
            : null;
    }

    private static IReadOnlyList<TideConstituent> ToConstituents(HarconDto? dto)
    {
        var result = new List<TideConstituent>();
        foreach (var c in dto?.HarmonicConstituents ?? [])
        {
            if (c.Name is { } name
                && c.Amplitude is { } amplitude and not 0
                && c.PhaseGmt is { } phase
                && HarmonicConstituents.Contains(name))
            {
                result.Add(new TideConstituent(name, amplitude, phase));
            }
        }

        return result;
    }

    private static StationDatums? ToDatums(DatumsDto? dto)
    {
        if (dto?.Datums is not { Count: > 0 } list)
        {
            return null;
        }

        var heights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var datum in list)
        {
            if (datum.Name is { } name && datum.Value is { } value && TideDatumNames.ToVerticalDatum(name) is not null)
            {
                heights[name] = value;
            }
        }

        if (dto.Lat is { } lat)
        {
            heights[TideDatumNames.LowestAstronomicalTide] = lat;
        }

        if (dto.Hat is { } hat)
        {
            heights[TideDatumNames.HighestAstronomicalTide] = hat;
        }

        DateOnly? accepted = DateOnly.TryParseExact(
            dto.Accepted, ["MMM d yyyy", "MMM dd yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;
        return new StationDatums(heights, string.IsNullOrWhiteSpace(dto.Epoch) ? null : dto.Epoch, accepted);
    }

    internal sealed class StationListDto
    {
        [JsonPropertyName("stations")]
        public List<StationDto>? Stations { get; set; }
    }

    internal sealed class StationDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("reference_id")]
        public string? ReferenceId { get; set; }

        [JsonPropertyName("lat")]
        public double? Lat { get; set; }

        [JsonPropertyName("lng")]
        public double? Lng { get; set; }

        [JsonPropertyName("timezonecorr")]
        public double? TimeZoneCorr { get; set; }

        [JsonPropertyName("tidepredoffsets")]
        public OffsetsDto? TidePredOffsets { get; set; }
    }

    internal sealed class OffsetsDto
    {
        [JsonPropertyName("refStationId")]
        public string? RefStationId { get; set; }

        [JsonPropertyName("heightOffsetHighTide")]
        public double? HeightOffsetHighTide { get; set; }

        [JsonPropertyName("heightOffsetLowTide")]
        public double? HeightOffsetLowTide { get; set; }

        [JsonPropertyName("timeOffsetHighTide")]
        public double? TimeOffsetHighTide { get; set; }

        [JsonPropertyName("timeOffsetLowTide")]
        public double? TimeOffsetLowTide { get; set; }

        [JsonPropertyName("heightAdjustedType")]
        public string? HeightAdjustedType { get; set; }
    }

    internal sealed class HarconDto
    {
        [JsonPropertyName("HarmonicConstituents")]
        public List<HarconConstituentDto>? HarmonicConstituents { get; set; }
    }

    internal sealed class HarconConstituentDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("amplitude")]
        public double? Amplitude { get; set; }

        [JsonPropertyName("phase_GMT")]
        public double? PhaseGmt { get; set; }
    }

    internal sealed class DatumsDto
    {
        [JsonPropertyName("accepted")]
        public string? Accepted { get; set; }

        [JsonPropertyName("epoch")]
        public string? Epoch { get; set; }

        [JsonPropertyName("datums")]
        public List<DatumDto>? Datums { get; set; }

        [JsonPropertyName("LAT")]
        public double? Lat { get; set; }

        [JsonPropertyName("HAT")]
        public double? Hat { get; set; }
    }

    internal sealed class DatumDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public double? Value { get; set; }
    }

    [JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(StationListDto))]
    [JsonSerializable(typeof(HarconDto))]
    [JsonSerializable(typeof(DatumsDto))]
    private sealed partial class NoaaJsonContext : JsonSerializerContext;
}
