using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// Reads and writes a <see cref="TideStationCatalogue"/> in the tide station
/// file format: JSON, optionally gzip-compressed.
/// </summary>
/// <remarks>
/// <para>
/// The same format is the offline cache of a downloaded NOAA catalogue and
/// the way to bring your own stations' constituents and datums. The
/// package README documents it field by field. In short:
/// </para>
/// <code language="json">
/// {
///   "format": "tide-stations",
///   "version": 1,
///   "provider": "NOAA CO-OPS",
///   "retrieved": "2026-11-12T16:00:00+00:00",
///   "stations": [
///     {
///       "id": "9447130", "name": "Seattle", "latitude": 47.60264, "longitude": -122.3393,
///       "region": "WA", "utcOffsetHours": -8, "kind": "harmonic",
///       "constituents": [ { "name": "M2", "amplitude": 1.063, "phase": 10.8 } ],
///       "datums": { "epoch": "1983-2001", "accepted": "2003-04-17",
///                   "heights": { "MSL": 4.443, "MLLW": 2.419 } }
///     },
///     {
///       "id": "9446486", "name": "Tacoma Narrows Bridge", "latitude": 47.2717, "longitude": -122.552,
///       "kind": "subordinate", "reference": "9447130",
///       "offsets": { "datum": "MLLW", "highWaterMinutes": 28, "lowWaterMinutes": 23,
///                    "highWaterHeight": 1.11, "lowWaterHeight": 1.02, "heightAdjustment": "ratio" }
///     }
///   ]
/// }
/// </code>
/// <para>
/// Amplitudes and heights are metres; phases are degrees, referred to
/// Greenwich unless a station says <c>"phaseReference": "local"</c>, when
/// they're referred to its <c>utcOffsetHours</c> and converted on reading.
/// </para>
/// </remarks>
public static partial class TideStationCatalogueFile
{
    /// <summary>The value of the file's <c>format</c> member.</summary>
    public const string FormatName = "tide-stations";

    /// <summary>The version of the format this library writes, and the newest it reads.</summary>
    public const int CurrentVersion = 1;

    private const string Harmonic = "harmonic";
    private const string Subordinate = "subordinate";
    private const string Ratio = "ratio";
    private const string Offset = "offset";
    private const string Local = "local";
    private const string Greenwich = "greenwich";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Reads a catalogue from a stream of JSON (not compressed).</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="FormatException">The JSON isn't a valid tide station file.</exception>
    public static async Task<TideStationCatalogue> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        FileDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync(stream, FileJsonContext.Default.FileDto, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Not a valid tide station file: {e.Message}", e);
        }

        return FromDto(dto ?? throw new FormatException("The tide station file is empty."));
    }

    /// <summary>
    /// Loads a catalogue from a file, gzip-compressed or not: a cached
    /// catalogue, or your own stations.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="FormatException">The file isn't a valid tide station file.</exception>
    public static async Task<TideStationCatalogue> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await using (file.ConfigureAwait(false))
        {
            var magic = new byte[2];
            var read = await file.ReadAtLeastAsync(magic, 2, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            if (read == 2 && magic[0] == 0x1f && magic[1] == 0x8b)
            {
                var gzip = new GZipStream(file, CompressionMode.Decompress);
                await using (gzip.ConfigureAwait(false))
                {
                    return await ReadAsync(gzip, cancellationToken).ConfigureAwait(false);
                }
            }

            return await ReadAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes a catalogue to a stream as compact JSON (not compressed).</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="catalogue">The catalogue.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the catalogue is written.</returns>
    public static Task WriteAsync(Stream stream, TideStationCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(catalogue);
        return JsonSerializer.SerializeAsync(stream, ToDto(catalogue), FileJsonContext.Default.FileDto, cancellationToken);
    }

    /// <summary>
    /// Saves a catalogue to a file, gzip-compressed when the path ends in
    /// <c>.gz</c>. The file is written beside the target and moved over it,
    /// so a reader never sees half a file.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="catalogue">The catalogue.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is saved.</returns>
    public static async Task SaveAsync(string path, TideStationCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(catalogue);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using (file.ConfigureAwait(false))
            {
                if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
                    await using (gzip.ConfigureAwait(false))
                    {
                        await WriteAsync(gzip, catalogue, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await WriteAsync(file, catalogue, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static TideStationCatalogue FromDto(FileDto dto)
    {
        if (!string.Equals(dto.Format, FormatName, StringComparison.Ordinal))
        {
            throw new FormatException($"Not a tide station file: format is '{dto.Format}', not '{FormatName}'.");
        }

        if (dto.Version is < 1 or > CurrentVersion)
        {
            throw new FormatException($"Tide station file version {dto.Version} isn't supported; this library reads versions 1 to {CurrentVersion}.");
        }

        var stations = new List<TideStationEntry>(dto.Stations?.Count ?? 0);
        foreach (var station in dto.Stations ?? [])
        {
            stations.Add(FromDto(station));
        }

        try
        {
            return new TideStationCatalogue(stations, dto.Provider, dto.Retrieved);
        }
        catch (ArgumentException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static TideStationEntry FromDto(StationDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new FormatException("A station has no id.");
        }

        if (dto.Latitude is not (>= -90 and <= 90) || dto.Longitude is not (>= -180 and <= 180))
        {
            throw new FormatException($"Station '{dto.Id}' has no valid latitude and longitude.");
        }

        var kind = dto.Kind switch
        {
            Harmonic => TideStationKind.Harmonic,
            Subordinate => TideStationKind.Subordinate,
            _ => throw new FormatException($"Station '{dto.Id}' has kind '{dto.Kind}', not '{Harmonic}' or '{Subordinate}'."),
        };

        var utcOffset = dto.UtcOffsetHours is { } hours ? TimeSpan.FromHours(hours) : (TimeSpan?)null;
        var local = dto.PhaseReference switch
        {
            null or Greenwich => false,
            Local => true,
            _ => throw new FormatException($"Station '{dto.Id}' has phaseReference '{dto.PhaseReference}', not '{Greenwich}' or '{Local}'."),
        };
        if (local && utcOffset is null)
        {
            throw new FormatException($"Station '{dto.Id}' gives local phases but no utcOffsetHours.");
        }

        var constituents = new List<TideConstituent>(dto.Constituents?.Count ?? 0);
        foreach (var c in dto.Constituents ?? [])
        {
            if (string.IsNullOrEmpty(c.Name) || !HarmonicConstituents.Contains(c.Name))
            {
                throw new FormatException($"Station '{dto.Id}' has an unknown constituent '{c.Name}'.");
            }

            constituents.Add(local && utcOffset is { } offset
                ? TideConstituent.FromLocalPhase(c.Name, c.Amplitude, c.Phase, offset)
                : new TideConstituent(c.Name, c.Amplitude, c.Phase));
        }

        return new TideStationEntry
        {
            Id = dto.Id,
            Name = dto.Name ?? dto.Id,
            Position = new GeoPosition(dto.Latitude.Value, dto.Longitude.Value),
            Kind = kind,
            Region = dto.Region,
            UtcOffset = utcOffset,
            Constituents = constituents,
            Datums = dto.Datums is { } datums ? FromDto(dto.Id, datums) : null,
            ReferenceStationId = dto.Reference,
            Offsets = dto.Offsets is { } offsets ? FromDto(dto.Id, offsets) : null,
            OffsetsDatum = dto.Offsets?.Datum ?? TideDatumNames.MeanLowerLowWater,
        };
    }

    private static StationDatums FromDto(string id, DatumsDto dto)
    {
        DateOnly? accepted = null;
        if (dto.Accepted is { } text)
        {
            accepted = DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : throw new FormatException($"Station '{id}' has an accepted date '{text}' that isn't {DateFormat}.");
        }

        try
        {
            return new StationDatums(dto.Heights ?? [], dto.Epoch, accepted);
        }
        catch (ArgumentException e)
        {
            throw new FormatException($"Station '{id}': {e.Message}", e);
        }
    }

    private static SubordinateOffsets FromDto(string id, OffsetsDto dto)
    {
        var adjustment = dto.HeightAdjustment switch
        {
            Ratio => SubordinateHeightAdjustment.Ratio,
            Offset => SubordinateHeightAdjustment.Offset,
            _ => throw new FormatException($"Station '{id}' has heightAdjustment '{dto.HeightAdjustment}', not '{Ratio}' or '{Offset}'."),
        };
        return new SubordinateOffsets(
            TimeSpan.FromMinutes(dto.HighWaterMinutes),
            TimeSpan.FromMinutes(dto.LowWaterMinutes),
            dto.HighWaterHeight,
            dto.LowWaterHeight,
            adjustment);
    }

    private static FileDto ToDto(TideStationCatalogue catalogue) => new()
    {
        Format = FormatName,
        Version = CurrentVersion,
        Provider = catalogue.Provider,
        Retrieved = catalogue.Retrieved,
        Stations = [.. catalogue.Stations.Select(ToDto)],
    };

    private static StationDto ToDto(TideStationEntry station) => new()
    {
        Id = station.Id,
        Name = station.Name,
        Latitude = station.Position.Latitude,
        Longitude = station.Position.Longitude,
        Region = station.Region,
        UtcOffsetHours = station.UtcOffset?.TotalHours,
        Kind = station.Kind == TideStationKind.Harmonic ? Harmonic : Subordinate,
        Constituents = station.Constituents.Count == 0
            ? null
            : [.. station.Constituents.Select(c => new ConstituentDto { Name = c.Name, Amplitude = c.AmplitudeMeters, Phase = c.PhaseDegrees })],
        Datums = station.Datums is { } datums
            ? new DatumsDto
            {
                Epoch = datums.Epoch,
                Accepted = datums.Accepted?.ToString(DateFormat, CultureInfo.InvariantCulture),
                Heights = datums.Heights.ToDictionary(),
            }
            : null,
        Reference = station.ReferenceStationId,
        Offsets = station.Offsets is { } offsets
            ? new OffsetsDto
            {
                Datum = station.OffsetsDatum,
                HighWaterMinutes = offsets.HighWaterTime.TotalMinutes,
                LowWaterMinutes = offsets.LowWaterTime.TotalMinutes,
                HighWaterHeight = offsets.HighWaterHeight,
                LowWaterHeight = offsets.LowWaterHeight,
                HeightAdjustment = offsets.HeightAdjustment == SubordinateHeightAdjustment.Ratio ? Ratio : Offset,
            }
            : null,
    };

    internal sealed class FileDto
    {
        public string? Format { get; set; }

        public int Version { get; set; }

        public string? Provider { get; set; }

        public DateTimeOffset? Retrieved { get; set; }

        public List<StationDto>? Stations { get; set; }
    }

    internal sealed class StationDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public string? Region { get; set; }

        public double? UtcOffsetHours { get; set; }

        public string? Kind { get; set; }

        public string? PhaseReference { get; set; }

        public List<ConstituentDto>? Constituents { get; set; }

        public DatumsDto? Datums { get; set; }

        public string? Reference { get; set; }

        public OffsetsDto? Offsets { get; set; }
    }

    internal sealed class ConstituentDto
    {
        public string? Name { get; set; }

        public double Amplitude { get; set; }

        public double Phase { get; set; }
    }

    internal sealed class DatumsDto
    {
        public string? Epoch { get; set; }

        public string? Accepted { get; set; }

        public Dictionary<string, double>? Heights { get; set; }
    }

    internal sealed class OffsetsDto
    {
        public string? Datum { get; set; }

        public double HighWaterMinutes { get; set; }

        public double LowWaterMinutes { get; set; }

        public double HighWaterHeight { get; set; }

        public double LowWaterHeight { get; set; }

        public string? HeightAdjustment { get; set; }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(FileDto))]
    private sealed partial class FileJsonContext : JsonSerializerContext;
}
