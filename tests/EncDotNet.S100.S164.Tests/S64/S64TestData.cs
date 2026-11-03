using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// The IHO S-64:100 unencrypted S-57 test data sets (S-57 test data for S-100
/// ECDIS) the S-164 Part 8 tests run against: the edition pinned in
/// <c>s64-100-v4.0.0.json</c>, kept in a local cache and checked against the
/// manifest's SHA-256 before use.
/// </summary>
/// <remarks>
/// <para>
/// The data is IHO copyright and published for ECDIS testing, so it isn't
/// committed. Set <c>S100_S164_DOWNLOAD=1</c> (the switch the S-164 data uses)
/// to download it from the IHO portal; without it a test whose data isn't
/// cached is skipped. The cache is <c>S100_S64_DATA</c> when set, otherwise
/// <c>$XDG_CACHE_HOME/EncDotNet.S100/s64/&lt;edition&gt;</c> (default
/// <c>~/.cache</c>).
/// </para>
/// <para>
/// The archive holds one folder per test, named after the manual's section
/// (<c>2.2.2 Loading of Updates</c>), each with an <c>ENC_ROOT</c> exchange set
/// and usually the reference plots. Paths here are relative to the archive's
/// root folder.
/// </para>
/// </remarks>
internal static class S64TestData
{
    /// <summary>Overrides the cache folder.</summary>
    public const string DataVariable = "S100_S64_DATA";

    private const string ManifestFileName = "s64-100-v4.0.0.json";

    private static readonly Lazy<S64Manifest> LazyManifest = new(LoadManifest);
    private static readonly Lazy<Task<string?>> Fetch = new(() => PinnedDownload.FetchAsync(
        Path.Combine(CacheDirectory, Manifest.File),
        new Uri(Manifest.Url),
        Manifest.Sha256,
        ManifestFileName,
        S164TestData.DownloadVariable));

    /// <summary>The pinned edition.</summary>
    public static S64Manifest Manifest => LazyManifest.Value;

    /// <summary>The folder the archive is cached in.</summary>
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

            return Path.Combine(cacheHome, "EncDotNet.S100", "s64", Manifest.Edition);
        }
    }

    /// <summary>
    /// Returns the path of the verified archive, downloading it first when
    /// downloads are on, and skips the calling test when it isn't available.
    /// </summary>
    public static async Task<string> GetArchiveAsync(CancellationToken cancellationToken)
    {
        var path = await Fetch.Value.WaitAsync(cancellationToken);
        Assert.SkipWhen(
            path is null,
            $"S-64 test data isn't cached in {CacheDirectory}. Set {S164TestData.DownloadVariable}=1 to download edition {Manifest.Edition}.");
        return path!;
    }

    /// <summary>The archive's files, as asset paths relative to its root folder.</summary>
    public static async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        var zip = await GetArchiveAsync(cancellationToken);
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries
            .Where(e => e.FullName.StartsWith(Manifest.Root, StringComparison.Ordinal) && e.Name.Length > 0)
            .Select(e => e.FullName[Manifest.Root.Length..])
            .ToList();
    }

    /// <summary>The archive as an asset source rooted at its root folder.</summary>
    public static async Task<ZipAssetSource> OpenSourceAsync(CancellationToken cancellationToken) =>
        ZipAssetSource.Create(await GetArchiveAsync(cancellationToken), Manifest.Root);

    private static S64Manifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ManifestFileName);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<S64Manifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"{ManifestFileName} is empty.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>A pinned S-64 test-data archive.</summary>
/// <param name="Edition">The S-64:100 edition.</param>
/// <param name="Source">The IHO page that links the archive.</param>
/// <param name="Url">Where the archive downloads from.</param>
/// <param name="File">The archive's file name in the cache.</param>
/// <param name="Root">The folder in the archive that holds the test folders.</param>
/// <param name="Bytes">The archive's size.</param>
/// <param name="Sha256">The archive's SHA-256.</param>
internal sealed record S64Manifest(
    string Edition,
    string Source,
    string Url,
    string File,
    string Root,
    long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256);
