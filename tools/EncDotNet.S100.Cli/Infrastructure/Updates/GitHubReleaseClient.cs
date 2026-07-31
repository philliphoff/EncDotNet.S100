using System.Net.Http.Headers;
using System.Text.Json;

namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Retrieves the latest published release from the GitHub Releases API.
/// </summary>
internal sealed class GitHubReleaseClient : ICliReleaseClient
{
    public const string ReleasesPageUrl =
        "https://github.com/philliphoff/EncDotNet.S100/releases";

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/philliphoff/EncDotNet.S100/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubReleaseClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.TryParseAdd("EncDotNet.S100.Cli");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
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
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    internal static GitHubReleaseInfo? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var tagName = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var htmlUrl = root.TryGetProperty("html_url", out var urlElement)
            && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString()
                : null;

        return new GitHubReleaseInfo(tagName, htmlUrl);
    }
}
