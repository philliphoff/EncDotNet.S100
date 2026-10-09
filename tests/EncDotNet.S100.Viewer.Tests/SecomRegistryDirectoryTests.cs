using System.Net;
using System.Text;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Mcp.Library;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// SECOM services from the MCP service registry in the Online Catalogue
/// directory and over MCP (#822).
/// </summary>
public sealed class SecomRegistryDirectoryTests
{
    private static readonly string RegistryJson = File.ReadAllText(
        LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "secom-registry.json"));

    private static SecomRegistryListing Listing() => SecomRegistry.Parse(RegistryJson, DateTimeOffset.UtcNow);

    private static CatalogueDirectoryDialogViewModel Directory(
        Func<Uri, SecomProbeResult> probe, Func<SecomRegistryListing>? listing = null) =>
        new(KnownCatalogueSources.All,
            loadSecomRegistry: _ => Task.FromResult((listing ?? Listing)()),
            probeSecom: (uri, _) => Task.FromResult(probe(uri)));

    private static async Task SettleAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Registry_services_are_listed_only_when_asked_and_s100_only()
    {
        var directory = Directory(_ => new SecomProbeResult(SecomReachability.Open));
        Assert.True(directory.CanShowRegistry);
        Assert.DoesNotContain(directory.Entries, e => e.IsRegistry);

        directory.ShowRegistryCommand.Execute(null);
        await SettleAsync(() => directory.IsRegistryLoaded);

        Assert.False(directory.CanShowRegistry);
        var registry = directory.Entries.Where(e => e.IsRegistry).ToArray();
        // The fixture's usable services less one OTHER (not S-100 data).
        Assert.Equal(4, registry.Length);
        Assert.All(registry, e => Assert.Equal(KnownCatalogueFormat.Secom, e.Source.Format));
        Assert.Contains(directory.Rows, r => r.IsGroupHeader && r.GroupTitle!.StartsWith("SECOM service registry", StringComparison.Ordinal));
        Assert.StartsWith("4 SECOM data services", directory.RegistryText);

        // Provisional registrations show as pilots; released ones do not.
        Assert.False(registry.Single(e => e.Name.StartsWith("KHRA", StringComparison.Ordinal)).IsPilot);
        Assert.True(registry.Single(e => e.Source.CatalogUri.Host == "s124.ccg-gcc.gc.ca").IsPilot);
    }

    [Fact]
    public async Task Choosing_a_registry_service_probes_it_and_only_an_open_one_continues()
    {
        var directory = Directory(uri => uri.Host == "s124.ccg-gcc.gc.ca"
            ? new SecomProbeResult(SecomReachability.Open)
            : new SecomProbeResult(SecomReachability.NeedsCertificate, "401"));
        directory.ShowRegistryCommand.Execute(null);
        await SettleAsync(() => directory.IsRegistryLoaded);
        var khra = directory.Entries.Single(e => e.IsRegistry && e.Name.StartsWith("KHRA", StringComparison.Ordinal));
        var ccg = directory.Entries.Single(e => e.IsRegistry && e.Source.CatalogUri.Host == "s124.ccg-gcc.gc.ca");

        directory.SelectedEntry = khra;
        await SettleAsync(() => khra.Reachability is not null);
        Assert.Equal(SecomReachability.NeedsCertificate, khra.Reachability);
        Assert.Equal("Needs a certificate", khra.ReachabilityText);
        Assert.True(khra.IsReachabilityLimited && khra.HasReachabilityExplanation);
        Assert.False(directory.CanContinueWithSelection);

        directory.SelectedEntry = ccg;
        await SettleAsync(() => ccg.Reachability is not null);
        Assert.Equal("Readable without a certificate", ccg.ReachabilityText);
        Assert.True(directory.CanContinueWithSelection);

        // A curated catalogue never needs a probe.
        directory.SelectedEntry = directory.Entries.First(e => !e.IsRegistry);
        Assert.True(directory.CanContinueWithSelection);
    }

    [Fact]
    public async Task The_explanation_says_why_a_server_certificate_was_refused_or_where_it_is_from()
    {
        // #829: the probe reports SecomServerTrust's decision about the TLS certificate.
        var directory = Directory(uri => uri.Host switch
        {
            "s124.ccg-gcc.gc.ca" => new SecomProbeResult(SecomReachability.Open)
            {
                ServerTrust = new SecomServerTrustResult(SecomServerTrustOutcome.AnchorTrusted, "MCP MCC"),
            },
            _ => new SecomProbeResult(SecomReachability.UntrustedServer, "Its certificate does not name this host.")
            {
                ServerTrust = new SecomServerTrustResult(SecomServerTrustOutcome.WrongHost, "MCP MCC"),
            },
        });
        directory.ShowRegistryCommand.Execute(null);
        await SettleAsync(() => directory.IsRegistryLoaded);
        var khra = directory.Entries.Single(e => e.IsRegistry && e.Name.StartsWith("KHRA", StringComparison.Ordinal));
        var ccg = directory.Entries.Single(e => e.IsRegistry && e.Source.CatalogUri.Host == "s124.ccg-gcc.gc.ca");

        directory.SelectedEntry = khra;
        await SettleAsync(() => khra.Reachability is not null);
        Assert.Equal("Server certificate not trusted", khra.ReachabilityText);
        Assert.Equal("This service's TLS certificate does not name its address, so the connection was refused.", khra.ReachabilityExplanation);

        directory.SelectedEntry = ccg;
        await SettleAsync(() => ccg.Reachability is not null);
        Assert.Equal("Its server certificate is from MCP MCC.", ccg.ReachabilityExplanation);
        Assert.True(directory.CanContinueWithSelection);
    }

    [Fact]
    public async Task A_service_readable_with_the_identity_continues_and_a_refusal_says_whose()
    {
        // #832: probes made with the client's MCP identity.
        const string mrn = "urn:mrn:mcp:device:mcc:soundcharts:test";
        var directory = Directory(uri => uri.Host == "s124.ccg-gcc.gc.ca"
            ? new SecomProbeResult(SecomReachability.OpenWithCertificate, mrn) { Identity = mrn }
            : new SecomProbeResult(SecomReachability.CertificateRefused, "401") { Identity = mrn });
        directory.ShowRegistryCommand.Execute(null);
        await SettleAsync(() => directory.IsRegistryLoaded);
        var khra = directory.Entries.Single(e => e.IsRegistry && e.Name.StartsWith("KHRA", StringComparison.Ordinal));
        var ccg = directory.Entries.Single(e => e.IsRegistry && e.Source.CatalogUri.Host == "s124.ccg-gcc.gc.ca");

        directory.SelectedEntry = khra;
        await SettleAsync(() => khra.Reachability is not null);
        Assert.Equal("Certificate refused", khra.ReachabilityText);
        Assert.Equal($"Your MCP identity ({mrn}) was presented, but this service does not accept it.", khra.ReachabilityExplanation);
        Assert.True(khra.IsReachabilityLimited);
        Assert.False(directory.CanContinueWithSelection);

        directory.SelectedEntry = ccg;
        await SettleAsync(() => ccg.Reachability is not null);
        Assert.Equal("Readable with your certificate", ccg.ReachabilityText);
        Assert.Equal($"It shares data with your MCP identity ({mrn}).", ccg.ReachabilityExplanation);
        Assert.False(ccg.IsReachabilityLimited);
        Assert.True(directory.CanContinueWithSelection);
    }

    [Fact]
    public async Task Set_secom_identity_loads_reports_refuses_and_clears_for_the_session()
    {
        var folder = System.IO.Directory.CreateTempSubdirectory("secom-identity-").FullName;
        var trust = new SecomServerTrust();
        var tool = new SetSecomIdentityTool(trust);
        var ct = TestContext.Current.CancellationToken;

        Assert.False(Ok(await tool.InvokeAsync(null, null, null, ct)).Set);

        var path = Path.Combine(folder, "identity.p12");
        File.WriteAllBytes(path, SelfSigned("urn:mrn:mcp:device:mcc:soundcharts:test", DateTimeOffset.UtcNow.AddDays(30)));
        var set = Ok(await tool.InvokeAsync(path, "pw", null, ct));
        Assert.True(set.Set);
        Assert.Equal("urn:mrn:mcp:device:mcc:soundcharts:test", set.Mrn);
        Assert.Null(set.Anchor);  // self-signed: not under MCP MCC
        Assert.Equal("urn:mrn:mcp:device:mcc:soundcharts:test", trust.Identity?.Mrn);

        // An expired identity, a wrong password and a missing file are refused; the identity stays.
        var expired = Path.Combine(folder, "expired.p12");
        File.WriteAllBytes(expired, SelfSigned("urn:mrn:mcp:device:mcc:soundcharts:old", DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.False((await tool.InvokeAsync(expired, "pw", null, ct)).TryGetValue(out _));
        Assert.False((await tool.InvokeAsync(path, "wrong", null, ct)).TryGetValue(out _));
        Assert.False((await tool.InvokeAsync(Path.Combine(folder, "missing.p12"), null, null, ct)).TryGetValue(out _));
        Assert.NotNull(trust.Identity);

        Assert.False(Ok(await tool.InvokeAsync(null, null, clear: true, ct)).Set);
        Assert.Null(trust.Identity);
        System.IO.Directory.Delete(folder, recursive: true);
    }

    private static T Ok<T>(EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<T> result) =>
        result.TryGetValue(out var value) ? value : throw new Xunit.Sdk.XunitException("The tool failed.");

    private static byte[] SelfSigned(string mrn, DateTimeOffset notAfter)
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var subject = new System.Security.Cryptography.X509Certificates.X500DistinguishedNameBuilder();
        subject.Add("0.9.2342.19200300.100.1.1", mrn, System.Formats.Asn1.UniversalTagNumber.UTF8String);
        subject.AddCommonName("Test vessel");
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            subject.Build(), key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(notAfter.AddDays(-60), notAfter);
        return certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, "pw");
    }

    [Fact]
    public async Task A_registry_that_cannot_be_read_says_so()
    {
        var directory = Directory(_ => new SecomProbeResult(SecomReachability.Open),
            () => throw new HttpRequestException("Connection refused."));

        directory.ShowRegistryCommand.Execute(null);
        await SettleAsync(() => directory.HasRegistryError);

        Assert.Contains("Connection refused.", directory.RegistryError);
        Assert.True(directory.CanShowRegistry);  // can try again
    }

    [Fact]
    public async Task List_secom_services_filters_by_product_and_probes_when_asked()
    {
        var registry = new SecomRegistry(new HttpClient(new Server()));
        var tool = new ListSecomServicesTool(registry);

        Assert.True((await tool.InvokeAsync("s124", probe: false, TestContext.Current.CancellationToken)).TryGetValue(out var s124));
        Assert.Equal(3, s124!.Services.Count);
        Assert.All(s124.Services, s => Assert.Equal("S-124", s.Product));
        Assert.All(s124.Services, s => Assert.Null(s.Reachability));
        Assert.Equal(11, s124.Listed);

        Assert.True((await tool.InvokeAsync(null, probe: true, TestContext.Current.CancellationToken)).TryGetValue(out var all));
        Assert.Equal(4, all!.Services.Count);
        Assert.All(all.Services, s => Assert.Equal("Unreachable", s.Reachability));  // the fake answers nothing but the registry
        Assert.Equal("list_secom_services", LibraryMcpAdapters.Create(tool).ProtocolTool.Name);
    }

    /// <summary>Answers the registry search; every service endpoint is a dead host.</summary>
    private sealed class Server : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "msr.maritimeconnectivity.net")
                throw new HttpRequestException("No such host is known.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RegistryJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
