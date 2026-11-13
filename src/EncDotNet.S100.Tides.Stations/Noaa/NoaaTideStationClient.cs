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
/// Reads tide and tidal current stations from the NOAA CO-OPS metadata API:
/// the tide prediction station list with subordinate stations' offsets,
/// each reference station's harmonic constituents and tidal datums, and the
/// current prediction station list with each harmonic current station's
/// constituents.
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
/// <para>
/// <b>Currents.</b> A current station is a station and a depth bin,
/// identified as NOAA's offsets are, <c>PUG1701_33</c>; NOAA lists each
/// station once per bin it predicts. Harmonic current stations (type
/// <c>H</c>) carry their major- and minor-axis constituents, mean currents
/// and major-axis direction for that bin; subordinate ones (type <c>S</c>)
/// their reference station and bin, time differences and speed ratios.
/// Flood and ebb directions are NOAA's <c>meanFloodDir</c> and
/// <c>meanEbbDir</c>; where NOAA gives one, the other is opposite it, and
/// where it gives neither, a harmonic station's are its major axis and a
/// subordinate station's its reference station's. Stations of
/// weak and variable current (type <c>W</c>) have no predictions and are
/// left out. A harmonic station whose M2 current ellipse is at least
/// <see cref="RotaryEllipseRatio"/> as wide as long is marked rotary.
/// </para>
/// </remarks>
public sealed partial class NoaaTideStationClient
{
    /// <summary>The provider name a downloaded catalogue carries.</summary>
    public const string ProviderName = "NOAA CO-OPS";

    /// <summary>
    /// A harmonic current station whose M2 constituent's minor-axis
    /// amplitude is at least this fraction of its major-axis amplitude is
    /// marked rotary (see <see cref="CurrentStationEntry.IsRotary"/>): its
    /// current turns round the compass rather than running to and fro.
    /// NOAA's own predictions give only the major axis.
    /// </summary>
    public const double RotaryEllipseRatio = 0.5;

    /// <summary>Centimetres per second in a metre per second: NOAA's metric current unit.</summary>
    private const double CentimetresPerMetre = 100;

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
    /// and each reference station's constituents and datums; and, unless
    /// <see cref="NoaaTideStationClientOptions.IncludeCurrentStations"/> is
    /// off, the current prediction station list and each harmonic current
    /// station's constituents.
    /// </summary>
    /// <remarks>
    /// About 2,500 requests for tides and 900 for currents, paced by
    /// <see cref="NoaaTideStationClientOptions.MaxConcurrency"/> and
    /// <see cref="NoaaTideStationClientOptions.RequestInterval"/>, so it
    /// takes some minutes. A reference station NOAA has no constituents for
    /// stays in the catalogue but can't be predicted, and neither can its
    /// subordinate stations.
    /// </remarks>
    /// <param name="progress">Receives progress as reference and harmonic current stations are read; <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The catalogue, dated now.</returns>
    /// <exception cref="HttpRequestException">A request failed after its retries.</exception>
    public async Task<TideStationCatalogue> DownloadCatalogueAsync(
        IProgress<NoaaDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var listing = await GetStationsAsync(cancellationToken).ConfigureAwait(false);
        var references = listing.Where(s => s.Kind == TideStationKind.Harmonic).ToArray();
        var details = new (IReadOnlyList<TideConstituent> Constituents, StationDatums? Datums)[references.Length];
        var currentListing = _options.IncludeCurrentStations
            ? await GetCurrentListingAsync(cancellationToken).ConfigureAwait(false)
            : [];
        var currentReferences = currentListing
            .Where(s => s.Kind == TideStationKind.Harmonic)
            .Select(s => s.NoaaId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currentHarmonics = new Dictionary<string, IReadOnlyList<CurrentHarconRowDto>>(StringComparer.Ordinal);
        var total = references.Length + currentReferences.Length;
        var completed = 0;
        progress?.Report(new NoaaDownloadProgress(0, total));

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

            progress?.Report(new NoaaDownloadProgress(Interlocked.Increment(ref completed), total));
        });
        var currentTasks = currentReferences.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var rows = await GetCurrentHarmonicsAsync(id, cancellationToken).ConfigureAwait(false);
                await PauseAsync(cancellationToken).ConfigureAwait(false);
                lock (currentHarmonics)
                {
                    currentHarmonics[id] = rows;
                }
            }
            finally
            {
                gate.Release();
            }

            progress?.Report(new NoaaDownloadProgress(Interlocked.Increment(ref completed), total));
        });
        await Task.WhenAll(tasks.Concat(currentTasks)).ConfigureAwait(false);

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

        var currents = new List<CurrentStationEntry>(currentListing.Count);
        foreach (var listed in currentListing)
        {
            if (listed.Kind == TideStationKind.Subordinate)
            {
                if (listed.Entry is { } subordinate)
                {
                    currents.Add(subordinate);
                }
            }
            else if (currentHarmonics.TryGetValue(listed.NoaaId, out var rows) && ToHarmonicEntry(listed, rows) is { } harmonic)
            {
                currents.Add(harmonic);
            }
        }

        return new TideStationCatalogue(stations, currents, ProviderName, _time.GetUtcNow());
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
    /// whose datums NOAA has accepted again. When the catalogue has current
    /// stations, it reads the current station list too (one more request)
    /// and finds current stations added, removed, moved or with changed
    /// directions or offsets. A harmonic bin NOAA lists without constants
    /// isn't counted as added while the catalogue has another bin of that
    /// station, since the download can't keep it. A change to one station's constituents alone
    /// isn't seen: NOAA revises those with its datums.
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

        if (catalogue.CurrentStations.Count > 0)
        {
            var currentListing = await GetCurrentListingAsync(cancellationToken).ConfigureAwait(false);
            var listedCurrents = new HashSet<string>(StringComparer.Ordinal);
            var cachedNoaaIds = new HashSet<string>(
                catalogue.CurrentStations.Select(s => NoaaIdOf(s.Id)), StringComparer.Ordinal);
            foreach (var station in currentListing)
            {
                listedCurrents.Add(station.Id);
                if (catalogue.FindCurrentStation(station.Id) is not { } cached)
                {
                    // NOAA lists some bins it has no constants for, and the
                    // download leaves them out: a missing bin of a station
                    // the catalogue has isn't new data, or the list would
                    // never stop being stale. A new station, or a
                    // subordinate bin the download would keep, is.
                    if (station.Kind == TideStationKind.Subordinate
                        ? station.Entry is not null
                        : !cachedNoaaIds.Contains(station.NoaaId))
                    {
                        added.Add(station.Id);
                    }
                }
                else if (HasChanged(cached, station))
                {
                    changed.Add(station.Id);
                }
            }

            removed.AddRange(catalogue.CurrentStations.Where(s => !listedCurrents.Contains(s.Id)).Select(s => s.Id));
        }

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

    // "PUG1701_33" → "PUG1701": the catalogue id is NOAA's station and bin.
    private static string NoaaIdOf(string catalogueId)
    {
        var bin = catalogueId.LastIndexOf('_');
        return bin > 0 ? catalogueId[..bin] : catalogueId;
    }

    private static bool HasChanged(CurrentStationEntry cached, CurrentListing listed) =>
        cached.Kind != listed.Kind
        || Math.Abs(cached.Position.Latitude - listed.Position.Latitude) > 1e-4
        || Math.Abs(cached.Position.Longitude - listed.Position.Longitude) > 1e-4
        || listed.FloodDirectionDegrees is { } flood && Math.Abs(cached.FloodDirectionDegrees - flood) > 0.05
        || listed.EbbDirectionDegrees is { } ebb && Math.Abs(cached.EbbDirectionDegrees - ebb) > 0.05
        || listed.Kind == TideStationKind.Subordinate
            && (!string.Equals(cached.ReferenceStationId, listed.Entry?.ReferenceStationId, StringComparison.Ordinal)
                || !Equals(cached.Offsets, listed.Entry?.Offsets));

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

    /// <summary>
    /// Reads NOAA's current prediction station list: a station per depth
    /// bin, and for a subordinate station its finished entry.
    /// </summary>
    private async Task<IReadOnlyList<CurrentListing>> GetCurrentListingAsync(CancellationToken cancellationToken)
    {
        var list = await GetAsync(
            "stations.json?type=currentpredictions&expand=currentpredictionoffsets&units=metric",
            NoaaJsonContext.Default.CurrentStationListDto,
            cancellationToken).ConfigureAwait(false);
        var result = new List<CurrentListing>(list?.Stations?.Count ?? 0);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var station in list?.Stations ?? [])
        {
            if (ToListing(station) is { } listing && seen.Add(listing.Id))
            {
                result.Add(listing);
            }
        }

        // A subordinate station NOAA gives no directions for takes its
        // reference station's.
        var byId = result.ToDictionary(l => l.Id, StringComparer.Ordinal);
        for (var i = result.Count - 1; i >= 0; i--)
        {
            if (result[i].Entry is not { } entry || double.IsFinite(entry.FloodDirectionDegrees))
            {
                continue;
            }

            if (byId.GetValueOrDefault(entry.ReferenceStationId!) is { FloodDirectionDegrees: { } flood } reference)
            {
                result[i] = result[i] with
                {
                    Entry = new CurrentStationEntry
                    {
                        Id = entry.Id,
                        Name = entry.Name,
                        Position = entry.Position,
                        Kind = entry.Kind,
                        DepthMeters = entry.DepthMeters,
                        FloodDirectionDegrees = flood,
                        EbbDirectionDegrees = reference.EbbDirectionDegrees ?? Opposite(flood),
                        ReferenceStationId = entry.ReferenceStationId,
                        Offsets = entry.Offsets,
                    },
                };
            }
            else
            {
                result.RemoveAt(i);
            }
        }

        return result;
    }

    /// <summary>Reads a current station's constituents for all its bins (NOAA's harcon.json for currents).</summary>
    private async Task<IReadOnlyList<CurrentHarconRowDto>> GetCurrentHarmonicsAsync(string stationId, CancellationToken cancellationToken)
    {
        var harcon = await GetAsync(
            $"stations/{Uri.EscapeDataString(stationId)}/harcon.json?units=metric",
            NoaaJsonContext.Default.CurrentHarconDto,
            cancellationToken).ConfigureAwait(false);
        var scale = harcon?.Units?.Contains("knot", StringComparison.OrdinalIgnoreCase) == true
            ? TidalCurrent.MetersPerSecondPerKnot
            : 1 / CentimetresPerMetre;
        var rows = harcon?.HarmonicConstituents ?? [];
        foreach (var row in rows)
        {
            row.Scale = scale;
        }

        return rows;
    }

    private static CurrentListing? ToListing(CurrentStationDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id) || dto.Lat is not { } lat || dto.Lng is not { } lng || dto.CurrBin is not { } bin)
        {
            return null;
        }

        var kind = dto.Type switch
        {
            "H" => TideStationKind.Harmonic,
            "S" => TideStationKind.Subordinate,
            _ => (TideStationKind?)null,
        };
        if (kind is null)
        {
            return null;
        }

        var offsets = dto.Offsets;
        var listing = new CurrentListing(
            dto.Id,
            $"{dto.Id}_{bin}",
            bin,
            dto.Name ?? dto.Id,
            new GeoPosition(lat, lng),
            kind.Value,
            dto.Depth,
            offsets?.MeanFloodDir,
            offsets?.MeanEbbDir,
            null);
        if (kind == TideStationKind.Harmonic)
        {
            return listing;
        }

        if (offsets is not
            {
                RefStationId: { Length: > 0 } reference,
                RefStationBin: { } referenceBin,
                SbfTimeAdjMin: { } sbf,
                MfcTimeAdjMin: { } mfc,
                SbeTimeAdjMin: { } sbe,
                MecTimeAdjMin: { } mec,
                MfcAmpAdj: { } floodRatio,
                MecAmpAdj: { } ebbRatio,
            })
        {
            return null;
        }

        // NOAA leaves out an ebb direction at a few stations, and both at
        // one: an unknown one is opposite the other, and both unknown are
        // filled from the reference station by the caller (NaN until then).
        var flood = offsets.MeanFloodDir ?? (offsets.MeanEbbDir is { } e ? Opposite(e) : double.NaN);
        var ebb = offsets.MeanEbbDir ?? (offsets.MeanFloodDir is { } f ? Opposite(f) : double.NaN);
        return listing with
        {
            Entry = new CurrentStationEntry
            {
                Id = listing.Id,
                Name = listing.Name,
                Position = listing.Position,
                Kind = TideStationKind.Subordinate,
                DepthMeters = dto.Depth,
                FloodDirectionDegrees = flood,
                EbbDirectionDegrees = ebb,
                ReferenceStationId = $"{reference}_{referenceBin}",
                Offsets = new SubordinateCurrentOffsets(
                    TimeSpan.FromMinutes(sbf),
                    TimeSpan.FromMinutes(mfc),
                    TimeSpan.FromMinutes(sbe),
                    TimeSpan.FromMinutes(mec),
                    floodRatio,
                    ebbRatio),
            },
        };
    }

    private static double Opposite(double degrees) => (degrees + 180) % 360;

    /// <summary>A harmonic current station's entry from its listing and its station's constituents for all bins.</summary>
    private static CurrentStationEntry? ToHarmonicEntry(CurrentListing listing, IReadOnlyList<CurrentHarconRowDto> rows)
    {
        var constituents = new List<CurrentConstituent>();
        CurrentHarconRowDto? first = null;
        foreach (var row in rows)
        {
            if (row.BinNbr != listing.Bin
                || row.ConstituentName is not { } name
                || !HarmonicConstituents.Contains(name)
                || constituents.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            first ??= row;
            var major = (row.MajorAmplitude ?? 0) * row.Scale;
            var minor = (row.MinorAmplitude ?? 0) * row.Scale;
            if (major != 0 || minor != 0)
            {
                constituents.Add(new CurrentConstituent(name, major, row.MajorPhaseGmt ?? 0, minor, row.MinorPhaseGmt ?? 0));
            }
        }

        if (first is null || constituents.Count == 0)
        {
            return null;
        }

        var axis = first.Azi ?? listing.FloodDirectionDegrees ?? 0;
        var m2 = constituents.FirstOrDefault(c => string.Equals(c.Name, "M2", StringComparison.OrdinalIgnoreCase));
        return new CurrentStationEntry
        {
            Id = listing.Id,
            Name = listing.Name,
            Position = listing.Position,
            Kind = TideStationKind.Harmonic,
            DepthMeters = listing.DepthMeters ?? first.BinDepth,
            FloodDirectionDegrees = listing.FloodDirectionDegrees ?? axis,
            EbbDirectionDegrees = listing.EbbDirectionDegrees ?? (listing.FloodDirectionDegrees is { } f ? Opposite(f) : Opposite(axis)),
            Constituents = constituents,
            MajorAxisDirectionDegrees = axis,
            MajorMeanVelocity = (first.MajorMeanSpeed ?? 0) * first.Scale,
            MinorMeanVelocity = (first.MinorMeanSpeed ?? 0) * first.Scale,
            IsRotary = m2 is { MajorAmplitudeMetersPerSecond: > 0 }
                && Math.Abs(m2.MinorAmplitudeMetersPerSecond) >= RotaryEllipseRatio * m2.MajorAmplitudeMetersPerSecond,
        };
    }

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

    /// <summary>A current station as NOAA lists it, per depth bin.</summary>
    /// <param name="NoaaId">NOAA's station identifier, such as <c>PUG1701</c>.</param>
    /// <param name="Id">The catalogue identifier, station and bin, such as <c>PUG1701_33</c>.</param>
    /// <param name="Bin">The depth bin.</param>
    /// <param name="Name">The station's name.</param>
    /// <param name="Position">The station's position.</param>
    /// <param name="Kind">Harmonic or subordinate.</param>
    /// <param name="DepthMeters">The bin's depth below the surface, or <c>null</c>.</param>
    /// <param name="FloodDirectionDegrees">NOAA's mean flood direction, or <c>null</c>.</param>
    /// <param name="EbbDirectionDegrees">NOAA's mean ebb direction, or <c>null</c>.</param>
    /// <param name="Entry">A subordinate station's finished entry; <c>null</c> for a harmonic station.</param>
    private sealed record CurrentListing(
        string NoaaId,
        string Id,
        int Bin,
        string Name,
        GeoPosition Position,
        TideStationKind Kind,
        double? DepthMeters,
        double? FloodDirectionDegrees,
        double? EbbDirectionDegrees,
        CurrentStationEntry? Entry);

    internal sealed class CurrentStationListDto
    {
        [JsonPropertyName("stations")]
        public List<CurrentStationDto>? Stations { get; set; }
    }

    internal sealed class CurrentStationDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("currbin")]
        public int? CurrBin { get; set; }

        [JsonPropertyName("depth")]
        public double? Depth { get; set; }

        [JsonPropertyName("lat")]
        public double? Lat { get; set; }

        [JsonPropertyName("lng")]
        public double? Lng { get; set; }

        [JsonPropertyName("currentpredictionoffsets")]
        public CurrentOffsetsDto? Offsets { get; set; }
    }

    internal sealed class CurrentOffsetsDto
    {
        [JsonPropertyName("refStationId")]
        public string? RefStationId { get; set; }

        [JsonPropertyName("refStationBin")]
        public int? RefStationBin { get; set; }

        [JsonPropertyName("meanFloodDir")]
        public double? MeanFloodDir { get; set; }

        [JsonPropertyName("meanEbbDir")]
        public double? MeanEbbDir { get; set; }

        [JsonPropertyName("mfcTimeAdjMin")]
        public double? MfcTimeAdjMin { get; set; }

        [JsonPropertyName("sbeTimeAdjMin")]
        public double? SbeTimeAdjMin { get; set; }

        [JsonPropertyName("mecTimeAdjMin")]
        public double? MecTimeAdjMin { get; set; }

        [JsonPropertyName("sbfTimeAdjMin")]
        public double? SbfTimeAdjMin { get; set; }

        [JsonPropertyName("mfcAmpAdj")]
        public double? MfcAmpAdj { get; set; }

        [JsonPropertyName("mecAmpAdj")]
        public double? MecAmpAdj { get; set; }
    }

    internal sealed class CurrentHarconDto
    {
        [JsonPropertyName("units")]
        public string? Units { get; set; }

        [JsonPropertyName("HarmonicConstituents")]
        public List<CurrentHarconRowDto>? HarmonicConstituents { get; set; }
    }

    internal sealed class CurrentHarconRowDto
    {
        [JsonPropertyName("binNbr")]
        public int? BinNbr { get; set; }

        [JsonPropertyName("binDepth")]
        public double? BinDepth { get; set; }

        [JsonPropertyName("constituentName")]
        public string? ConstituentName { get; set; }

        [JsonPropertyName("majorAmplitude")]
        public double? MajorAmplitude { get; set; }

        [JsonPropertyName("majorPhaseGMT")]
        public double? MajorPhaseGmt { get; set; }

        [JsonPropertyName("minorAmplitude")]
        public double? MinorAmplitude { get; set; }

        [JsonPropertyName("minorPhaseGMT")]
        public double? MinorPhaseGmt { get; set; }

        [JsonPropertyName("majorMeanSpeed")]
        public double? MajorMeanSpeed { get; set; }

        [JsonPropertyName("minorMeanSpeed")]
        public double? MinorMeanSpeed { get; set; }

        [JsonPropertyName("azi")]
        public double? Azi { get; set; }

        /// <summary>Metres per second per unit of NOAA's amplitudes; set after reading.</summary>
        [JsonIgnore]
        public double Scale { get; set; } = 1 / CentimetresPerMetre;
    }

    [JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(StationListDto))]
    [JsonSerializable(typeof(HarconDto))]
    [JsonSerializable(typeof(DatumsDto))]
    [JsonSerializable(typeof(CurrentStationListDto))]
    [JsonSerializable(typeof(CurrentHarconDto))]
    private sealed partial class NoaaJsonContext : JsonSerializerContext;
}
