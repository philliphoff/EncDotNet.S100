using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Secom;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The SECOM service registry (issue #822): the MSR listing is cleaned, cached,
/// and each service probed for whether it can be read. <c>secom-registry.json</c>
/// holds real MCC registry entries captured on 2026-10-07.
/// </summary>
public sealed class SecomRegistryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Fixture() => File.ReadAllText(TestPaths.Fixture("secom-registry.json"));

    [Fact]
    public void The_listing_drops_unusable_entries_and_repairs_geometry()
    {
        var listing = SecomRegistry.Parse(Fixture(), DateTimeOffset.UnixEpoch);

        Assert.Equal(11, listing.Listed);
        // Dropped: an MRN endpoint, a bare host, localhost, example.com, a deleted
        // instance, and one of two instances sharing an endpoint.
        Assert.Equal(6, listing.Dropped);
        Assert.Equal(5, listing.Services.Count);
        Assert.Equal(SecomRegistryStatus.Released, listing.Services[0].Status);  // released first

        // CCG registers longitudes off by whole turns (−414…−522): shifted back.
        var ccg = listing.Services.Single(s => s.InstanceId == "urn:mrn:mcp:org:mcc:ccg:s124:prod");
        Assert.Equal("S-124", ccg.ProductSpec);
        Assert.Equal("ccg", ccg.OrganizationName);
        Assert.Equal("s124 CCG service prod service", ccg.Name);  // trimmed
        Assert.NotNull(ccg.Bounds);
        Assert.InRange(ccg.Bounds!.Value.West, -163, -162);
        Assert.InRange(ccg.Bounds.Value.East, -55, -54);

        // An empty geometry has no area; a geometry collection does.
        Assert.Null(listing.Services.Single(s => s.InstanceId.EndsWith("elmansrl:s124", StringComparison.Ordinal)).Bounds);
        Assert.NotNull(listing.Services.Single(s => s.InstanceId.EndsWith("weather:0.1", StringComparison.Ordinal)).Bounds);

        // A registered MRN with stray spaces is trimmed.
        Assert.Contains(listing.Services, s => s.InstanceId == "urn:mrn:mcp:service:mcc:khra:instance:nav-warning");
        Assert.Equal(new[] { "OTHER", "S-124", "S-124", "S-124", "S-412" }, listing.Services.Select(s => s.ProductSpec).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("https://s124.ccg-gcc.gc.ca/api/secom", true)]
    [InlineData("http://118.220.143.176:18092/sv30", true)]
    [InlineData("http://localhost:8090", false)]
    [InlineData("https://dmatest:8080/", false)]
    [InlineData("urn:mrn:iho:country:au:s124", false)]
    [InlineData("atoninformation.bluemap.kr", false)]
    [InlineData("https://example.com", false)]
    [InlineData("ftp://files.test/secom", false)]
    public void Only_absolute_http_endpoints_on_real_hosts_are_usable(string endpoint, bool usable) =>
        Assert.Equal(usable, SecomRegistry.UsableEndpoint(endpoint) is not null);

    [Fact]
    public void Bounds_shift_each_part_by_whole_turns_and_reject_bad_latitudes()
    {
        GeoBounds? Bounds(string json) => SecomRegistry.BoundsOf(JsonDocument.Parse(json).RootElement);

        Assert.Equal(new GeoBounds(10, 20, 10, 20), Bounds("""{"type":"Point","coordinates":[380,10]}"""));
        Assert.Equal(new GeoBounds(0, -10, 5, 10), Bounds("""{"type":"LineString","coordinates":[[-370,0],[-350,5]]}"""));
        Assert.Null(Bounds("""{"type":"Point","coordinates":[10,120]}"""));
        Assert.Null(Bounds("""{"type":"Point","coordinates":[]}"""));
        Assert.Equal(new GeoBounds(-1, -1, 2, 2), Bounds("""
            {"type":"GeometryCollection","geometries":[
              {"type":"Point","coordinates":[-1,-1]},
              {"type":"Polygon","coordinates":[[[0,0],[2,0],[2,2],[0,0]]]}]}
            """));
    }

    [Fact]
    public async Task The_listing_is_cached_and_served_stale_when_the_registry_is_down()
    {
        var server = new RegistryServer(Fixture());
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var registry = new SecomRegistry(new HttpClient(server), _temp.Path, timeProvider: time);

        var first = await registry.GetServicesAsync(cancellationToken: Ct);
        Assert.Equal(5, first.Services.Count);
        Assert.Equal(HttpMethod.Post, server.Methods.Single());

        // Recent: reused without asking the registry.
        await registry.GetServicesAsync(cancellationToken: Ct);
        Assert.Single(server.Methods);

        // Stale and down: the cached copy, with the reason.
        time.Advance(TimeSpan.FromHours(2));
        server.Down = true;
        var stale = await registry.GetServicesAsync(cancellationToken: Ct);
        Assert.Equal(5, stale.Services.Count);
        Assert.NotNull(stale.Stale);

        // Down with nothing cached: an error.
        var fresh = new SecomRegistry(new HttpClient(server), Path.Combine(_temp.Path, "none"));
        await Assert.ThrowsAsync<HttpRequestException>(() => fresh.GetServicesAsync(cancellationToken: Ct));
    }

    [Theory]
    [InlineData("open", SecomReachability.Open)]
    [InlineData("signed-search-only", SecomReachability.NeedsCertificate)]
    [InlineData("refuses", SecomReachability.NeedsCertificate)]
    [InlineData("website", SecomReachability.Unreachable)]
    [InlineData("down", SecomReachability.Unreachable)]
    [InlineData("untrusted-tls", SecomReachability.UntrustedServer)]
    public async Task Probes_classify_what_a_service_answers(string behaviour, SecomReachability expected)
    {
        var registry = new SecomRegistry(new HttpClient(new ServiceServer(behaviour)));

        var result = await registry.ProbeAsync(new Uri("https://service.test/api/secom"), Ct);

        Assert.Equal(expected, result.Reachability);
    }

    /// <summary>Serves a registry search on POST, or fails when down.</summary>
    private sealed class RegistryServer(string body) : HttpMessageHandler
    {
        public bool Down { get; set; }

        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
                throw new HttpRequestException("Connection refused.");
            Methods.Add(request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>A service that answers SECOM fully, in part, or not at all.</summary>
    private sealed class ServiceServer(string behaviour) : HttpMessageHandler
    {
        private const string Capability = """{"capability":[{"containerType":0,"dataProductType":"S124","implementedInterfaces":{"get":true,"getSummary":true}}]}""";
        private const string Summary = """{"summaryObject":[],"pagination":{"totalItems":0,"maxItemsPerPage":1}}""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var capability = path.EndsWith("/capability", StringComparison.Ordinal);
            return behaviour switch
            {
                "down" => throw new HttpRequestException("No such host is known."),
                "untrusted-tls" => throw new HttpRequestException("The SSL connection could not be established.",
                    new AuthenticationException("The remote certificate is invalid according to the validation procedure.")),
                "open" => Json(capability ? Capability : Summary),
                "signed-search-only" => capability ? Json(Capability) : Status(HttpStatusCode.NotFound),
                "refuses" => Status(HttpStatusCode.Unauthorized),
                "website" => Status(HttpStatusCode.NotFound),
                _ => throw new InvalidOperationException(behaviour),
            };
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        private static Task<HttpResponseMessage> Status(HttpStatusCode code) => Task.FromResult(new HttpResponseMessage(code));
    }
}
