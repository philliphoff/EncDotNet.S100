using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Per-source identities over real TLS (#883): a source presents its own
/// identity, only when the service asks, and a refused one is never followed
/// by another.
/// </summary>
public sealed class SecomIdentityTlsTests(SecomIdentityTlsTests.Pkis pkis) : IClassFixture<SecomIdentityTlsTests.Pkis>, IDisposable
{
    private readonly TempDirectory _temp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    /// <summary>The CAs, a server certificate and two identities, made once per class (slow on macOS).</summary>
    public sealed class Pkis : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Pkis()
        {
            Server = Trusted.Leaf(dnsNames: ["localhost"]);
            Organisation = Load(Trusted.ClientLeaf("urn:mrn:mcp:org:mcc:soundcharts", "SoundCharts"));
            BridgePc = Load(Other.ClientLeaf("urn:mrn:mcp:device:other:soundcharts:bridge-pc", "Bridge PC"));
        }

        public TestPki Trusted { get; } = TestPki.Create();

        public TestPki Other { get; } = TestPki.Create();

        public X509Certificate2 Server { get; }

        /// <summary>Issued under the trusted PKI: the service accepts it.</summary>
        public SecomClientIdentity Organisation { get; }

        /// <summary>Issued under another PKI: the service refuses it.</summary>
        public SecomClientIdentity BridgePc { get; }

        private SecomClientIdentity Load(X509Certificate2 issued)
        {
            using (issued)
            {
                var path = Path.Combine(_temp.Path, $"{Guid.NewGuid():N}.p12");
                File.WriteAllBytes(path, issued.Export(X509ContentType.Pkcs12, "t"));
                return SecomClientIdentity.Load(path, "t", Trusted.Anchors);
            }
        }

        public void Dispose()
        {
            Organisation.Dispose();
            BridgePc.Dispose();
            Server.Dispose();
            Trusted.Dispose();
            Other.Dispose();
            _temp.Dispose();
        }
    }

    [Theory]
    [InlineData(ClientCertificateMode.ForSummary)]
    [InlineData(ClientCertificateMode.AtHandshake)]
    public async Task A_refused_identity_is_presented_once_and_no_other_is_tried(ClientCertificateMode mode)
    {
        await using var server = await TlsServer.StartAsync(pkis.Server, pkis.Trusted.Intermediate, mode, pkis.Trusted.Issued);
        var (trust, indexer) = Create();
        var bridgeId = Register(trust, pkis.BridgePc);
        var organisationId = Register(trust, pkis.Organisation);
        trust.DefaultIdentityId = organisationId;
        var source = new SecomSource(Guid.NewGuid(), null, server.Uri("/api/secom"), SecomFilter.All) { Identity = bridgeId };

        var refused = await indexer.IndexAsync(source, null, Ct);

        Assert.Equal(SecomDiagnosticCodes.CertificateRefused, Assert.Single(refused.Diagnostics).Code);
        var presentation = refused.Presentation!;
        Assert.Equal(SecomPresentationOutcome.Refused, presentation.Outcome);
        Assert.Equal(pkis.BridgePc.Mrn, presentation.Presented);
        Assert.True(presentation.CertificateRequested);
        Assert.Equal(bridgeId, Assert.Single(presentation.Refusals).IdentityId);
        // A single handshake, with the source's identity: the default (accepted) is never tried.
        Assert.Equal(1, server.Connections);
        Assert.Equal([pkis.BridgePc.Certificate.Thumbprint], server.ClientCertificates.Select(c => c?.Thumbprint));

        // Choosing the organisation identity reads with it, and the refusal is remembered.
        var readable = await indexer.IndexAsync(source with { Identity = organisationId }, null, Ct);
        Assert.Empty(readable.Diagnostics);
        Assert.Equal(SecomPresentationOutcome.Accepted, readable.Presentation?.Outcome);
        Assert.Equal(pkis.Organisation.Certificate.Thumbprint, server.ClientCertificates[^1]?.Thumbprint);
        Assert.Equal(SecomIdentityHint.RefusedHere, readable.Presentation!.HintFor(bridgeId, pkis.BridgePc, pkis.Trusted.Anchors));
    }

    [Fact]
    public async Task A_source_on_none_tells_a_certificate_only_service_needs_one_and_presents_nothing()
    {
        await using var server = await TlsServer.StartAsync(
            pkis.Server, pkis.Trusted.Intermediate, ClientCertificateMode.AtHandshake, pkis.Trusted.Issued);
        var (trust, indexer) = Create();
        trust.SetIdentity(pkis.Organisation);
        var source = new SecomSource(Guid.NewGuid(), null, server.Uri("/api/secom"), SecomFilter.All) { Identity = SecomIdentitySetting.None };

        var index = await indexer.IndexAsync(source, null, Ct);

        Assert.Equal(SecomDiagnosticCodes.NeedsCertificate, Assert.Single(index.Diagnostics).Code);
        Assert.Equal(SecomPresentationOutcome.NotPresented, index.Presentation?.Outcome);
        Assert.True(index.Presentation?.ServiceAsks);
        Assert.All(server.ClientCertificates, Assert.Null);
    }

    [Fact]
    public async Task An_open_service_is_read_without_presenting_anything()
    {
        await using var server = await TlsServer.StartAsync(pkis.Server, pkis.Trusted.Intermediate);
        var (trust, indexer) = Create();
        trust.SetIdentity(pkis.Organisation);
        var source = new SecomSource(Guid.NewGuid(), null, server.Uri("/api/secom"), SecomFilter.All);

        var index = await indexer.IndexAsync(source, null, Ct);

        Assert.Empty(index.Diagnostics);
        Assert.Equal(SecomPresentationOutcome.NotAsked, index.Presentation?.Outcome);
        Assert.False(index.Presentation?.ServiceAsks);
        Assert.Equal("Test MCP", index.Presentation?.ServerAnchor);
    }

    private (SecomServerTrust Trust, SecomSourceIndexer Indexer) Create()
    {
        var trust = new SecomServerTrust(pkis.Trusted.Anchors);
        return (trust, new SecomSourceIndexer(new HttpClient(trust.CreateHandler()), _temp.Path) { Trust = trust });
    }

    private static string Register(SecomServerTrust trust, SecomClientIdentity identity)
    {
        var id = SecomIdentitySetting.ReferenceIdOf(identity.Certificate.Thumbprint);
        trust.SetIdentity(id, identity);
        return id;
    }
}
