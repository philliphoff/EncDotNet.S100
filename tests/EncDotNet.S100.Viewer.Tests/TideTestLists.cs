using System.Net;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Station lists for tests: a file path, and a NOAA that never answers.</summary>
internal static class TideTestLists
{
    /// <summary>A station list read from <paramref name="path"/>, whose download fails at once.</summary>
    /// <param name="path">The cached list, which need not exist.</param>
    public static TideStationListService At(string path) => new(
        path,
        () => new HttpClient(new OfflineHandler()),
        TimeProvider.System,
        action => action(),
        logger: null);

    /// <summary>A station list in a folder that has none.</summary>
    public static TideStationListService Missing() =>
        At(Path.Combine(Path.GetTempPath(), "s100-no-tides-" + Guid.NewGuid().ToString("N"), "tide-stations.json.gz"));

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Offline in tests.", null, HttpStatusCode.ServiceUnavailable);
    }
}
