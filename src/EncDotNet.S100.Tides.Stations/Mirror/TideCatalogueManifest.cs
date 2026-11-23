using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>
/// Describes a catalogue a mirror publishes: which file to download, its
/// size and SHA-256 to check it by, where its stations come from and when
/// they were retrieved, and how many there are.
/// </summary>
/// <remarks>
/// <para>
/// A mirror publishes the manifest, <see cref="DefaultFileName"/>, beside
/// the catalogue, so a client can see whether there's newer data with one
/// small request. The manifest is JSON:
/// </para>
/// <code language="json">
/// {
///   "format": "tide-stations-manifest",
///   "version": 1,
///   "file": "tide-stations.json.gz",
///   "catalogueFormat": "tide-stations",
///   "catalogueVersion": 1,
///   "size": 370412,
///   "sha256": "9f2c…",
///   "provider": "NOAA CO-OPS",
///   "retrieved": "2026-11-23T06:20:41+00:00",
///   "crawled": "2026-11-23T06:20:41+00:00",
///   "checked": "2026-11-30T06:12:03+00:00",
///   "datumEpoch": "1983-2001",
///   "stationCount": 3502,
///   "harmonicStationCount": 1260,
///   "subordinateStationCount": 2242,
///   "currentStationCount": 2871
/// }
/// </code>
/// <para>
/// <c>retrieved</c> is the catalogue's own <see cref="TideStationCatalogue.Retrieved"/>:
/// when its data was read from the provider. A mirror that downloads again
/// and finds the same data keeps the file it has, so <c>retrieved</c> moves
/// only when the data changes; <c>crawled</c> is when the mirror last
/// downloaded everything, and <c>checked</c> when it last confirmed the
/// file is current.
/// </para>
/// </remarks>
public sealed partial class TideCatalogueManifest
{
    /// <summary>The value of the manifest's <c>format</c> member.</summary>
    public const string FormatName = "tide-stations-manifest";

    /// <summary>The version of the manifest this library writes, and the newest it reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The manifest's file name on a mirror.</summary>
    public const string DefaultFileName = "tide-stations.manifest.json";

    /// <summary>The catalogue's file name on a mirror: the catalogue as gzip-compressed <see cref="TideStationCatalogueFile"/> JSON.</summary>
    public const string DefaultCatalogueFileName = "tide-stations.json.gz";

    /// <summary>
    /// The catalogue file, relative to the manifest's address. Defaults to
    /// <see cref="DefaultCatalogueFileName"/>.
    /// </summary>
    public required string File { get; init; }

    /// <summary>The catalogue's file format: <see cref="TideStationCatalogueFile.FormatName"/>.</summary>
    public required string CatalogueFormat { get; init; }

    /// <summary>The catalogue's file format version.</summary>
    public required int CatalogueVersion { get; init; }

    /// <summary>The catalogue file's size in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>The catalogue file's SHA-256, as 64 lowercase hexadecimal digits.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Who provides the stations, such as <c>NOAA CO-OPS</c>, or <c>null</c>.</summary>
    public string? Provider { get; init; }

    /// <summary>When the catalogue's stations were retrieved from the provider.</summary>
    public required DateTimeOffset Retrieved { get; init; }

    /// <summary>When the mirror last downloaded the whole catalogue from the provider.</summary>
    public required DateTimeOffset Crawled { get; init; }

    /// <summary>When the mirror last confirmed that the provider has no newer data than the catalogue.</summary>
    public required DateTimeOffset Checked { get; init; }

    /// <summary>The catalogue's <see cref="TideStationCatalogue.DatumEpoch"/>.</summary>
    public string? DatumEpoch { get; init; }

    /// <summary>The catalogue's tide stations.</summary>
    public int StationCount { get; init; }

    /// <summary>The catalogue's harmonic (reference) tide stations.</summary>
    public int HarmonicStationCount { get; init; }

    /// <summary>The catalogue's subordinate tide stations.</summary>
    public int SubordinateStationCount { get; init; }

    /// <summary>The catalogue's current stations.</summary>
    public int CurrentStationCount { get; init; }

    /// <summary>
    /// Whether this library can read the catalogue: its format is
    /// <see cref="TideStationCatalogueFile.FormatName"/> at a version no newer
    /// than <see cref="TideStationCatalogueFile.CurrentVersion"/>.
    /// </summary>
    public bool IsSupported =>
        string.Equals(CatalogueFormat, TideStationCatalogueFile.FormatName, StringComparison.Ordinal)
        && CatalogueVersion <= TideStationCatalogueFile.CurrentVersion;

    /// <summary>Describes a catalogue file.</summary>
    /// <param name="content">The catalogue file's bytes, as published.</param>
    /// <param name="catalogue">The catalogue the file holds.</param>
    /// <param name="crawled">When the mirror last downloaded the whole catalogue from the provider.</param>
    /// <param name="checked">When the mirror last confirmed the provider has no newer data.</param>
    /// <param name="file">The catalogue file's name, relative to the manifest.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="ArgumentException">The catalogue is undated.</exception>
    public static TideCatalogueManifest Create(
        ReadOnlySpan<byte> content,
        TideStationCatalogue catalogue,
        DateTimeOffset crawled,
        DateTimeOffset @checked,
        string file = DefaultCatalogueFileName)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (catalogue.Retrieved is not { } retrieved)
        {
            throw new ArgumentException("A published catalogue must say when its stations were retrieved.", nameof(catalogue));
        }

        return new TideCatalogueManifest
        {
            File = file,
            CatalogueFormat = TideStationCatalogueFile.FormatName,
            CatalogueVersion = TideStationCatalogueFile.CurrentVersion,
            Size = content.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            Provider = catalogue.Provider,
            Retrieved = retrieved,
            Crawled = crawled,
            Checked = @checked,
            DatumEpoch = catalogue.DatumEpoch,
            StationCount = catalogue.Stations.Count,
            HarmonicStationCount = catalogue.Stations.Count(s => s.Kind == TideStationKind.Harmonic),
            SubordinateStationCount = catalogue.Stations.Count(s => s.Kind == TideStationKind.Subordinate),
            CurrentStationCount = catalogue.CurrentStations.Count,
        };
    }

    /// <summary>Whether some bytes are the catalogue file this manifest describes: their size and SHA-256 match.</summary>
    /// <param name="content">The bytes.</param>
    /// <returns><c>true</c> when they match.</returns>
    public bool Matches(ReadOnlySpan<byte> content) =>
        content.Length == Size
        && string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), Sha256, StringComparison.Ordinal);

    /// <summary>Reads a manifest from a stream of JSON.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="FormatException">The JSON isn't a valid manifest, or is a newer version than this library reads.</exception>
    public static async Task<TideCatalogueManifest> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ManifestDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync(stream, ManifestJsonContext.Default.ManifestDto, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Not a valid tide station manifest: {e.Message}", e);
        }

        return FromDto(dto ?? throw new FormatException("The tide station manifest is empty."));
    }

    /// <summary>Loads a manifest from a file.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="FormatException">The file isn't a valid manifest.</exception>
    public static async Task<TideCatalogueManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await using (file.ConfigureAwait(false))
        {
            return await ReadAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the manifest to a stream as indented JSON.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the manifest is written.</returns>
    public Task WriteAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return JsonSerializer.SerializeAsync(stream, ToDto(), ManifestJsonContext.Default.ManifestDto, cancellationToken);
    }

    /// <summary>Saves the manifest to a file, replacing it in one step.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is saved.</returns>
    public Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return TideStationCatalogueFile.ReplaceFileAsync(path, (stream, token) => WriteAsync(stream, token), cancellationToken);
    }

    private static TideCatalogueManifest FromDto(ManifestDto dto)
    {
        if (!string.Equals(dto.Format, FormatName, StringComparison.Ordinal))
        {
            throw new FormatException($"Not a tide station manifest: its format is '{dto.Format}', not '{FormatName}'.");
        }

        if (dto.Version is < 1 or > CurrentVersion)
        {
            throw new FormatException($"Tide station manifest version {dto.Version.ToString(CultureInfo.InvariantCulture)} isn't supported; this library reads version {CurrentVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (string.IsNullOrWhiteSpace(dto.File) || Uri.TryCreate(dto.File, UriKind.Absolute, out _) || dto.File.StartsWith('/'))
        {
            throw new FormatException("The tide station manifest must name its catalogue file relative to itself.");
        }

        if (string.IsNullOrEmpty(dto.CatalogueFormat) || dto.CatalogueVersion is not { } catalogueVersion)
        {
            throw new FormatException("The tide station manifest doesn't say which format its catalogue is in.");
        }

        if (dto.Size is not > 0 || dto.Sha256 is not { Length: 64 } sha256 || !sha256.All(char.IsAsciiHexDigitLower))
        {
            throw new FormatException("The tide station manifest must give its catalogue's size and SHA-256 (64 lowercase hexadecimal digits).");
        }

        if (dto.Retrieved is not { } retrieved || dto.Crawled is not { } crawled || dto.Checked is not { } @checked)
        {
            throw new FormatException("The tide station manifest must say when its catalogue was retrieved, crawled and checked.");
        }

        return new TideCatalogueManifest
        {
            File = dto.File,
            CatalogueFormat = dto.CatalogueFormat,
            CatalogueVersion = catalogueVersion,
            Size = dto.Size.Value,
            Sha256 = sha256,
            Provider = dto.Provider,
            Retrieved = retrieved,
            Crawled = crawled,
            Checked = @checked,
            DatumEpoch = dto.DatumEpoch,
            StationCount = dto.StationCount ?? 0,
            HarmonicStationCount = dto.HarmonicStationCount ?? 0,
            SubordinateStationCount = dto.SubordinateStationCount ?? 0,
            CurrentStationCount = dto.CurrentStationCount ?? 0,
        };
    }

    private ManifestDto ToDto() => new()
    {
        Format = FormatName,
        Version = CurrentVersion,
        File = File,
        CatalogueFormat = CatalogueFormat,
        CatalogueVersion = CatalogueVersion,
        Size = Size,
        Sha256 = Sha256,
        Provider = Provider,
        Retrieved = Retrieved,
        Crawled = Crawled,
        Checked = Checked,
        DatumEpoch = DatumEpoch,
        StationCount = StationCount,
        HarmonicStationCount = HarmonicStationCount,
        SubordinateStationCount = SubordinateStationCount,
        CurrentStationCount = CurrentStationCount,
    };

    internal sealed class ManifestDto
    {
        public string? Format { get; set; }

        public int Version { get; set; }

        public string? File { get; set; }

        public string? CatalogueFormat { get; set; }

        public int? CatalogueVersion { get; set; }

        public long? Size { get; set; }

        public string? Sha256 { get; set; }

        public string? Provider { get; set; }

        public DateTimeOffset? Retrieved { get; set; }

        public DateTimeOffset? Crawled { get; set; }

        public DateTimeOffset? Checked { get; set; }

        public string? DatumEpoch { get; set; }

        public int? StationCount { get; set; }

        public int? HarmonicStationCount { get; set; }

        public int? SubordinateStationCount { get; set; }

        public int? CurrentStationCount { get; set; }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true)]
    [JsonSerializable(typeof(ManifestDto))]
    private sealed partial class ManifestJsonContext : JsonSerializerContext;
}
