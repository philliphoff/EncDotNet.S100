using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Updates;

/// <summary>
/// <see cref="IGitHubReleaseClient"/> backed by the public GitHub Releases
/// REST API. Reads <c>GET /repos/{owner}/{repo}/releases/latest</c>, which
/// returns the most recent published, non-pre-release release.
/// </summary>
/// <remarks>
/// The call is unauthenticated (60 requests/hour/IP); the update service
/// throttles checks to stay well within that. All failures resolve to
/// <see langword="null"/> so an offline or rate-limited viewer simply shows
/// "couldn't check" rather than erroring.
/// </remarks>
internal sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    /// <summary>Owner of the repository releases are published under.</summary>
    public const string RepositoryOwner = "philliphoff";

    /// <summary>Repository name releases are published under.</summary>
    public const string RepositoryName = "EncDotNet.S100";

    /// <summary>Browser URL of the repository's releases page (Tier-1 update target).</summary>
    public const string ReleasesPageUrl =
        $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";

    /// <summary>Browser URL of the repository license.</summary>
    public const string LicenseUrl =
        $"https://github.com/{RepositoryOwner}/{RepositoryName}/blob/main/LICENSE";

    /// <summary>Browser URL of the repository's third-party notices document.</summary>
    public const string ThirdPartyNoticesUrl =
        $"https://github.com/{RepositoryOwner}/{RepositoryName}/blob/main/THIRD-PARTY-NOTICES.md";

    /// <summary>Browser URL of the repository (third-party notices fallback).</summary>
    public const string RepositoryUrl =
        $"https://github.com/{RepositoryOwner}/{RepositoryName}";

    private const string LatestReleaseUrl =
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubReleaseClient>? _logger;

    public GitHubReleaseClient(HttpClient httpClient, ILogger<GitHubReleaseClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            // GitHub requires a User-Agent and recommends pinning the API version.
            request.Headers.UserAgent.TryParseAdd($"{RepositoryName}-Viewer");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogDebug("GitHub releases request returned {Status}.", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Parse(document.RootElement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to query the latest GitHub release.");
            return null;
        }
    }

    /// <summary>
    /// Projects a GitHub <c>releases/latest</c> JSON payload into a
    /// <see cref="GitHubRelease"/>. Exposed for unit testing the mapping.
    /// </summary>
    internal static GitHubRelease? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (!root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        string? name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;
        string? htmlUrl = root.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String
            ? h.GetString()
            : null;
        string? body = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
            ? b.GetString()
            : null;

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out var p)
            && p.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(p.GetString(), out var parsed))
        {
            publishedAt = parsed;
        }

        var isPrerelease = root.TryGetProperty("prerelease", out var pr)
            && pr.ValueKind == JsonValueKind.True;

        long? largestAsset = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("size", out var size)
                    && size.ValueKind == JsonValueKind.Number
                    && size.TryGetInt64(out var bytes))
                {
                    largestAsset = Math.Max(largestAsset ?? 0, bytes);
                }
            }
        }

        return new GitHubRelease(tag, name, htmlUrl, body, publishedAt, isPrerelease, largestAsset);
    }
}
