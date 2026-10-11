using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// SECOM sources in the Library when an identity is missing, refused or not
/// trusted (#880): coded diagnostics, the status line, and reading the
/// affected sources again when the identity changes.
/// </summary>
public sealed class SecomIdentityLibraryTests : IDisposable
{
    private readonly LibraryContext _context = new();

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("identity-required", false, SecomDiagnosticCodes.NeedsSignedRequest)]
    [InlineData("401", false, SecomDiagnosticCodes.NeedsCertificate)]
    [InlineData("403", true, SecomDiagnosticCodes.CertificateRefused)]
    [InlineData("tls", false, SecomDiagnosticCodes.ServerUntrusted)]
    [InlineData("404", false, null)]
    [InlineData("unreachable", false, null)]
    public void Failures_are_classified_by_what_an_identity_could_change(string failure, bool identitySet, string? expected)
    {
        Exception ex = failure switch
        {
            "identity-required" => new SecomIdentityRequiredException("needs an identity"),
            "401" => new HttpRequestException("401", null, HttpStatusCode.Unauthorized),
            "403" => new HttpRequestException("403", null, HttpStatusCode.Forbidden),
            "tls" => new HttpRequestException("TLS", new AuthenticationException("untrusted")),
            "404" => new HttpRequestException("404", null, HttpStatusCode.NotFound),
            _ => new HttpRequestException("No such host."),
        };

        Assert.Equal(expected, SecomDiagnosticCodes.Classify(ex, identitySet));
        Assert.Equal(expected is not null && expected != SecomDiagnosticCodes.ServerUntrusted, SecomDiagnosticCodes.IsIdentityMatter(expected));
    }

    [Theory]
    [InlineData("needs-certificate", false, SecomDiagnosticCodes.NeedsCertificate, "Needs a certificate")]
    [InlineData("needs-certificate", true, SecomDiagnosticCodes.CertificateRefused, "Your certificate was refused")]
    [InlineData("post-only", false, SecomDiagnosticCodes.NeedsSignedRequest, "Needs a signed request")]
    public async Task A_source_that_cannot_be_read_says_why_in_plain_words(string service, bool identity, string code, string line)
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var id = identity ? Identity() : null;
        trust.SetIdentity(id);
        using var library = _context.CreateLibrary(Indexer(new Service(service), trust));
        library.Initialize();

        library.AddCollection("SECOM", [new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.test/api/secom/"), SecomFilter.All)]);
        await library.WhenIdle();

        var collection = Assert.Single(library.Collections);
        var source = Assert.Single(collection.Sources);
        Assert.Equal(code, LibraryNodeText.SecomCode(source.Index!));
        var (text, kind) = LibraryNodeText.Status(new LibraryNodeStatusInput(collection, collection.Sources));
        Assert.Equal(line, text);
        Assert.Equal(LibraryNodeStatusKind.Error, kind);
    }

    [Fact]
    public async Task Setting_an_identity_reads_again_only_the_sources_it_could_change()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        var locked = new Service("needs-certificate");
        var open = new Service("open");
        var handler = new Router(new Dictionary<string, Service>
        {
            ["locked.test"] = locked,
            ["open.test"] = open,
        });
        using var library = _context.CreateLibrary(Indexer(handler, trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);

        library.AddCollection("Locked", [new SecomSource(Guid.NewGuid(), null, new Uri("https://locked.test/api/secom/"), SecomFilter.All)]);
        library.AddCollection("Open", [new SecomSource(Guid.NewGuid(), null, new Uri("https://open.test/api/secom/"), SecomFilter.All)]);
        await library.WhenIdle();
        Assert.Single(refresh.Affected(SecomDiagnosticCodes.IsIdentityMatter));
        var (lockedReads, openReads) = (locked.SummaryReads, open.SummaryReads);

        using var identity = Identity();
        locked.Accepts = true;
        trust.SetIdentity(identity);
        await library.WhenIdle();

        Assert.True(locked.SummaryReads > lockedReads);
        Assert.Equal(openReads, open.SummaryReads);
        Assert.Empty(refresh.Affected(SecomDiagnosticCodes.IsIdentityMatter));
        var source = library.Collections.Single(c => c.Definition.Name == "Locked").Sources[0];
        Assert.Null(LibraryNodeText.SecomCode(source.Index!));
    }

    [Fact]
    public async Task Changing_the_trusted_authorities_reads_again_the_sources_with_an_untrusted_server()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        var service = new Service("untrusted");
        using var library = _context.CreateLibrary(Indexer(service, trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);

        library.AddCollection("Untrusted", [new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.test/api/secom/"), SecomFilter.All)]);
        await library.WhenIdle();
        var collection = Assert.Single(library.Collections);
        Assert.Equal(SecomDiagnosticCodes.ServerUntrusted, LibraryNodeText.SecomCode(collection.Sources[0].Index!));
        Assert.Equal("Server certificate not trusted", LibraryNodeText.Status(new LibraryNodeStatusInput(collection, collection.Sources)).Line);

        // An identity change is no help here; trusting its authority is.
        using var identity = Identity();
        var reads = service.SummaryReads;
        trust.SetIdentity(identity);
        await library.WhenIdle();
        Assert.Equal(reads, service.SummaryReads);

        service.Accepts = true;
        trust.Anchors = SecomTrustAnchors.BuiltIn;
        await library.WhenIdle();
        Assert.True(service.SummaryReads > reads);
        Assert.Null(LibraryNodeText.SecomCode(library.Collections[0].Sources[0].Index!));
    }

    private static CollectionIndexer Indexer(HttpMessageHandler handler, SecomServerTrust trust) =>
        new([new SecomSourceIndexer(new HttpClient(handler)) { Signer = trust.CreateSigner }]);

    private static SecomClientIdentity Identity()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Test vessel", key, HashAlgorithmName.SHA256);
        return SecomClientIdentity.FromCertificate(
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), SecomTrustAnchors.None);
    }

    /// <summary>Sends each request to the service for its host.</summary>
    private sealed class Router(Dictionary<string, Service> services) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            services[request.RequestUri!.Host].Send(request);
    }

    /// <summary>
    /// A SECOM service: "open" lists one object; "needs-certificate" refuses
    /// the summary (403) until <see cref="Accepts"/>; "post-only" has no GET
    /// summary, only SECOM 2.0's signed POST form; "untrusted" fails the TLS
    /// handshake until <see cref="Accepts"/>.
    /// </summary>
    private sealed class Service(string kind) : HttpMessageHandler
    {
        private const string Capability = """{"capability":[{"containerType":0,"dataProductType":"S124","implementedInterfaces":{"get":true,"getSummary":true}}]}""";
        private const string Summary = """{"summaryObject":[{"dataReference":"ref-0001","containerType":0,"dataProductType":"S124","info_identifier":"NW-1"}],"pagination":{"totalItems":1,"maxItemsPerPage":250}}""";

        public bool Accepts { get; set; }

        public int SummaryReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Send(request);

        public Task<HttpResponseMessage> Send(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capability", StringComparison.Ordinal))
            {
                if (kind == "untrusted" && !Accepts)
                    throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException("untrusted"));
                return Respond(HttpStatusCode.OK, Capability);
            }
            if (path.Contains("/object/summary", StringComparison.Ordinal) || path.EndsWith("/search/summary", StringComparison.Ordinal))
            {
                SummaryReads++;
                return kind switch
                {
                    "open" => Respond(HttpStatusCode.OK, Summary),
                    "needs-certificate" => Accepts ? Respond(HttpStatusCode.OK, Summary) : Respond(HttpStatusCode.Forbidden, string.Empty),
                    "untrusted" => Accepts ? Respond(HttpStatusCode.OK, Summary)
                        : throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException("untrusted")),
                    _ when request.Method == HttpMethod.Post => Respond(HttpStatusCode.BadRequest, "{}"),
                    _ => Respond(HttpStatusCode.NotFound, string.Empty),
                };
            }

            return Respond(HttpStatusCode.NotFound, string.Empty);
        }

        private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
