using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// The IHO S-164 test exchange sets these tests run against: the release
/// pinned in <c>s164-v2.0.0.json</c>, kept in a local cache and checked against
/// the manifest's SHA-256 before use.
/// </summary>
/// <remarks>
/// <para>
/// The S-164 data has no licence file, so it isn't committed. Set
/// <c>S100_S164_DOWNLOAD=1</c> to download missing exchange sets from the
/// release; without it a test whose data isn't cached is skipped. The cache
/// is <c>S100_S164_DATA</c> when set, otherwise
/// <c>$XDG_CACHE_HOME/EncDotNet.S100/s164/&lt;release&gt;</c> (default
/// <c>~/.cache</c>).
/// </para>
/// <para>
/// A cached file whose hash doesn't match the manifest is downloaded again
/// when downloads are on, and fails the test otherwise.
/// </para>
/// </remarks>
internal static class S164TestData
{
    /// <summary>Downloads missing exchange sets when set to <c>1</c> or <c>true</c>.</summary>
    public const string DownloadVariable = "S100_S164_DOWNLOAD";

    /// <summary>Overrides the cache folder.</summary>
    public const string DataVariable = "S100_S164_DATA";

    private const string ManifestFileName = "s164-v2.0.0.json";

    private static readonly Lazy<S164Manifest> LazyManifest = new(LoadManifest);
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Fetches = new(StringComparer.Ordinal);

    /// <summary>The pinned release.</summary>
    public static S164Manifest Manifest => LazyManifest.Value;

    /// <summary>The folder the exchange-set ZIPs are cached in.</summary>
    public static string CacheDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(DataVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cacheHome))
            {
                cacheHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            }

            return Path.Combine(cacheHome, "EncDotNet.S100", "s164", Manifest.Release);
        }
    }

    /// <summary>The names of every exchange set in the release, for theories.</summary>
    public static TheoryData<string> ExchangeSetNames()
    {
        var data = new TheoryData<string>();
        foreach (var exchangeSet in Manifest.ExchangeSets)
        {
            data.Add(exchangeSet.Name);
        }

        return data;
    }

    /// <summary>
    /// Returns the path of the verified ZIP for the exchange set
    /// <paramref name="name"/>, downloading it first when downloads are on,
    /// and skips the calling test when it isn't available.
    /// </summary>
    public static async Task<string> GetExchangeSetAsync(string name, CancellationToken cancellationToken)
    {
        var entry = Manifest.ExchangeSets.SingleOrDefault(e => e.Name == name)
            ?? throw new ArgumentException($"'{name}' isn't in {ManifestFileName}.", nameof(name));

        var path = await Fetches
            .GetOrAdd(entry.Name, _ => new Lazy<Task<string?>>(() => FetchAsync(entry)))
            .Value
            .WaitAsync(cancellationToken);

        Assert.SkipWhen(
            path is null,
            $"S-164 exchange set '{name}' isn't cached in {CacheDirectory}. Set {DownloadVariable}=1 to download the {Manifest.Release} release.");
        return path!;
    }

    private static Task<string?> FetchAsync(S164ExchangeSetEntry entry) =>
        PinnedDownload.FetchAsync(
            Path.Combine(CacheDirectory, entry.File),
            new Uri(new Uri(Manifest.DownloadBase), entry.File),
            entry.Sha256,
            ManifestFileName,
            DownloadVariable);

    private static S164Manifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ManifestFileName);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<S164Manifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"{ManifestFileName} is empty.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>A pinned S-164 test-data release.</summary>
internal sealed record S164Manifest(
    string Release,
    string Source,
    string DownloadBase,
    IReadOnlyList<S164ExchangeSetEntry> ExchangeSets);

/// <summary>One exchange-set ZIP in the release.</summary>
internal sealed record S164ExchangeSetEntry(
    string Name,
    string File,
    long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256);
