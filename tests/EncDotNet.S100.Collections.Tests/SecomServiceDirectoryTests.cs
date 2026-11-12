using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The registry's S-100 services as the viewer's directory and MCP
/// <c>list_secom_services</c> both list and probe them (#1399).
/// </summary>
public sealed class SecomServiceDirectoryTests
{
    private static readonly string RegistryJson = File.ReadAllText(TestPaths.Fixture("secom-registry.json"));

    [Fact]
    public async Task Lists_s100_services_only_optionally_for_one_product()
    {
        var directory = new SecomServiceDirectory(new Registry());

        var all = await directory.ListS100Async(cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(all.Services, s => Assert.True(s.IsS100Product));
        Assert.Equal(all.Listed - all.Services.Count, all.Hidden);

        var s124 = await directory.ListS100Async(product: "s124", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(s124.Services);
        Assert.All(s124.Services, s => Assert.Equal("S-124", s.ProductSpec));
        Assert.True(s124.Services.Count < all.Services.Count);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("io")]
    [InlineData("data")]
    [InlineData("timeout")]
    public async Task A_probe_that_fails_outright_is_unreachable(string failure)
    {
        var directory = new SecomServiceDirectory(new Registry(_ => throw (failure switch
        {
            "http" => (Exception)new HttpRequestException("refused"),
            "io" => new IOException("refused"),
            "data" => new InvalidDataException("refused"),
            _ => new TaskCanceledException("refused"),
        })));

        var result = await directory.ProbeAsync(new Uri("https://service.test/"), TestContext.Current.CancellationToken);

        Assert.Equal(SecomReachability.Unreachable, result.Reachability);
        Assert.Equal("refused", result.Detail);
    }

    [Fact]
    public async Task Cancelling_a_probe_throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var directory = new SecomServiceDirectory(new Registry(_ => throw new TaskCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.ProbeAsync(new Uri("https://service.test/"), cts.Token));
    }

    [Fact]
    public async Task A_registry_that_cannot_be_parsed_reads_as_an_http_failure()
    {
        var directory = new SecomServiceDirectory(new Registry(listing: () => throw new InvalidDataException("bad json")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => directory.ListS100Async(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("bad json", ex.Message);
    }

    private sealed class Registry(Func<Uri, SecomProbeResult>? probe = null, Func<SecomRegistryListing>? listing = null) : ISecomRegistry
    {
        public async Task<SecomRegistryListing> GetServicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return listing?.Invoke() ?? SecomRegistry.Parse(RegistryJson, DateTimeOffset.UtcNow);
        }

        public async Task<SecomProbeResult> ProbeAsync(Uri serviceUri, string? identity = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return (probe ?? (_ => new SecomProbeResult(SecomReachability.Open)))(serviceUri);
        }
    }
}
