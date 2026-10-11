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
    [InlineData("identity-required", false, false, SecomDiagnosticCodes.NeedsSignedRequest)]
    [InlineData("401", false, false, SecomDiagnosticCodes.NeedsCertificate)]
    [InlineData("403", true, false, SecomDiagnosticCodes.CertificateRefused)]
    [InlineData("403", true, true, SecomDiagnosticCodes.SignedRequestRefused)]
    [InlineData("tls", false, false, SecomDiagnosticCodes.ServerUntrusted)]
    [InlineData("404", false, false, null)]
    [InlineData("unreachable", false, false, null)]
    public void Failures_are_classified_by_what_an_identity_could_change(string failure, bool identitySet, bool signed, string? expected)
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
        using var identity = identitySet ? Identity() : null;

        Assert.Equal(expected, SecomDiagnosticCodes.Classify(ex, identity, signed));
        Assert.Equal(expected is not null && expected != SecomDiagnosticCodes.ServerUntrusted, SecomDiagnosticCodes.IsIdentityMatter(expected));
    }

    [Fact]
    public void A_refused_handshake_is_told_from_an_untrusted_server_by_what_the_trust_saw()
    {
        var failure = new HttpRequestException("The SSL connection could not be established.", new IOException("alert"));
        var trusted = new SecomServerTrustResult(SecomServerTrustOutcome.AnchorTrusted, "Test MCP");
        var asked = new SecomHandshake("secom.test", true, [], DateTimeOffset.UtcNow);
        using var identity = Identity();

        Assert.Equal(SecomDiagnosticCodes.NeedsCertificate, SecomDiagnosticCodes.Classify(failure, null, serverTrust: trusted, handshake: asked));
        Assert.Equal(SecomDiagnosticCodes.CertificateRefused, SecomDiagnosticCodes.Classify(failure, identity, serverTrust: trusted, handshake: asked));
        Assert.Null(SecomDiagnosticCodes.Classify(failure, identity, serverTrust: trusted, handshake: asked with { CertificateRequested = false }));
    }

    [Theory]
    [InlineData("needs-certificate", SecomIdentitySetting.None, SecomDiagnosticCodes.NeedsCertificate, "Needs a certificate")]
    [InlineData("needs-certificate", SecomIdentitySetting.Default, SecomDiagnosticCodes.CertificateRefused, "Your certificate was refused")]
    [InlineData("post-only", SecomIdentitySetting.None, SecomDiagnosticCodes.NeedsSignedRequest, "Needs a signed request")]
    [InlineData("post-only", SecomIdentitySetting.Default, SecomDiagnosticCodes.SignedRequestRefused, "Your certificate was refused")]
    public async Task A_source_that_cannot_be_read_says_why_in_plain_words(string service, string setting, string code, string line)
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var id = Identity();
        trust.SetIdentity(id);
        using var library = _context.CreateLibrary(Indexer(new Service(service), trust));
        library.Initialize();

        library.AddCollection("SECOM", [Source("secom.test", setting)]);
        await library.WhenIdle();

        var collection = Assert.Single(library.Collections);
        var source = Assert.Single(collection.Sources);
        Assert.Equal(code, LibraryNodeText.SecomCode(source.Index!));
        var (text, kind) = LibraryNodeText.Status(new LibraryNodeStatusInput(collection, collection.Sources));
        Assert.Equal(line, text);
        Assert.Equal(LibraryNodeStatusKind.Error, kind);
    }

    [Fact]
    public async Task A_source_on_none_presents_nothing_even_with_a_default_set()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var identity = Identity();
        trust.SetIdentity(identity);
        var service = new Service("needs-certificate") { Accepted = identity.Certificate.Thumbprint };
        using var library = _context.CreateLibrary(Indexer(service, trust));
        library.Initialize();

        library.AddCollection("SECOM", [Source("secom.test", SecomIdentitySetting.None)]);
        await library.WhenIdle();

        var source = library.Collections[0].Sources[0];
        Assert.Equal(SecomDiagnosticCodes.NeedsCertificate, LibraryNodeText.SecomCode(source.Index!));
        Assert.Equal(SecomPresentationOutcome.NotPresented, source.Index!.Presentation?.Outcome);
        Assert.All(service.Presented, Assert.Null);
    }

    [Fact]
    public async Task A_refused_source_names_its_identity_and_switching_reads_only_that_source()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var bridge = Identity("Bridge PC");
        using var organisation = Identity("Organisation");
        var (bridgeId, organisationId) = (Register(trust, bridge), Register(trust, organisation));
        trust.DefaultIdentityId = bridgeId;
        var locked = new Service("needs-certificate") { Accepted = organisation.Certificate.Thumbprint };
        var open = new Service("open");
        using var library = _context.CreateLibrary(Indexer(new Router(new() { ["locked.test"] = locked, ["open.test"] = open }), trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);

        library.AddCollection("Locked", [Source("locked.test", bridgeId)]);
        library.AddCollection("Open", [Source("open.test", SecomIdentitySetting.Default)]);
        await library.WhenIdle();

        var refused = Locked(library);
        Assert.Equal(SecomDiagnosticCodes.CertificateRefused, LibraryNodeText.SecomCode(refused.Index!));
        var presentation = refused.Index!.Presentation!;
        Assert.Equal((SecomPresentationOutcome.Refused, bridgeId, bridge.DisplayName), (presentation.Outcome, presentation.IdentityId, presentation.Presented));
        Assert.Equal(bridgeId, Assert.Single(presentation.Refusals).IdentityId);
        // One read, one identity: the refused one is never followed by another.
        Assert.Equal([bridge.Certificate.Thumbprint], locked.Presented.Select(c => c?.Certificate.Thumbprint).Distinct());

        var openReads = open.SummaryReads;
        var definition = (SecomSource)refused.Definition;
        library.UpdateSource(library.Collections[0].Id, definition with { Identity = organisationId });
        await library.WhenIdle();

        var readable = Locked(library);
        Assert.Null(LibraryNodeText.SecomCode(readable.Index!));
        Assert.Equal(SecomPresentationOutcome.Accepted, readable.Index!.Presentation?.Outcome);
        // The refusal of the other identity is still remembered for this source.
        Assert.Equal(SecomIdentityHint.RefusedHere, readable.Index.Presentation!.HintFor(bridgeId, bridge, SecomTrustAnchors.None));
        Assert.Equal(openReads, open.SummaryReads);
    }

    [Fact]
    public async Task Making_another_identity_the_default_reads_only_the_sources_on_default()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var first = Identity("First");
        using var second = Identity("Second");
        var (firstId, secondId) = (Register(trust, first), Register(trust, second));
        trust.DefaultIdentityId = firstId;
        var onDefault = new Service("open");
        var chosen = new Service("open");
        var anonymous = new Service("open");
        using var library = _context.CreateLibrary(Indexer(new Router(new()
        {
            ["default.test"] = onDefault,
            ["chosen.test"] = chosen,
            ["none.test"] = anonymous,
        }), trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);
        library.AddCollection("SECOM",
        [
            Source("default.test", SecomIdentitySetting.Default),
            Source("chosen.test", firstId),
            Source("none.test", SecomIdentitySetting.None),
        ]);
        await library.WhenIdle();
        var reads = (onDefault.SummaryReads, chosen.SummaryReads, anonymous.SummaryReads);

        trust.DefaultIdentityId = secondId;
        await library.WhenIdle();

        Assert.True(onDefault.SummaryReads > reads.Item1);
        Assert.Equal(second.Certificate.Thumbprint, onDefault.Presented[^1]?.Certificate.Thumbprint);
        Assert.Equal((reads.Item2, reads.Item3), (chosen.SummaryReads, anonymous.SummaryReads));
        Assert.Equal(secondId, library.Collections[0].Sources[0].Index!.Presentation?.IdentityId);
    }

    [Fact]
    public async Task Removing_an_identity_reads_the_sources_that_resolved_to_it_and_they_present_none()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var removed = Identity("Removed");
        using var kept = Identity("Kept");
        var (removedId, keptId) = (Register(trust, removed), Register(trust, kept));
        trust.DefaultIdentityId = removedId;
        var direct = new Service("needs-certificate") { Accepted = removed.Certificate.Thumbprint };
        var viaDefault = new Service("needs-certificate") { Accepted = removed.Certificate.Thumbprint };
        var other = new Service("open");
        using var library = _context.CreateLibrary(Indexer(new Router(new()
        {
            ["direct.test"] = direct,
            ["default.test"] = viaDefault,
            ["other.test"] = other,
        }), trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);
        library.AddCollection("SECOM", [Source("direct.test", removedId), Source("default.test", SecomIdentitySetting.Default), Source("other.test", keptId)]);
        await library.WhenIdle();
        Assert.Equal(2, SecomIdentityUsage.UsersOf(library.Collections, removedId, trust.DefaultIdentityId).Count);
        var otherReads = other.SummaryReads;

        // As the Remove confirm does: a new default first, then the direct users switch to none.
        trust.DefaultIdentityId = keptId;
        Assert.Single(SecomIdentityUsage.Reassign(library, removedId, SecomIdentitySetting.None));
        trust.SetIdentity(removedId, null);
        await library.WhenIdle();

        var sources = library.Collections[0].Sources;
        Assert.Equal(SecomIdentitySetting.None, ((SecomSource)sources[0].Definition).Identity);
        Assert.Equal(SecomDiagnosticCodes.NeedsCertificate, LibraryNodeText.SecomCode(sources[0].Index!));
        Assert.Equal(keptId, sources[1].Index!.Presentation?.IdentityId);
        Assert.Equal(otherReads, other.SummaryReads);
    }

    [Fact]
    public async Task A_missing_reference_presents_none_and_needs_a_certificate()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var identity = Identity();
        trust.SetIdentity(identity);
        var service = new Service("needs-certificate");
        using var library = _context.CreateLibrary(Indexer(service, trust));
        library.Initialize();

        library.AddCollection("SECOM", [Source("secom.test", "sc-ident:91d0b3c5")]);
        await library.WhenIdle();

        var source = library.Collections[0].Sources[0];
        Assert.Equal(SecomDiagnosticCodes.NeedsCertificate, LibraryNodeText.SecomCode(source.Index!));
        Assert.Equal("sc-ident:91d0b3c5", source.Index!.Presentation?.IdentityId);
        Assert.True(trust.Resolve("sc-ident:91d0b3c5").IsMissing);
        Assert.All(service.Presented, Assert.Null);
    }

    [Fact]
    public async Task A_source_added_before_any_identity_lists_once_one_is_imported_and_chosen()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        var service = new Service("needs-certificate");
        using var library = _context.CreateLibrary(Indexer(service, trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);

        // Add and wait: no identities yet, so the source has no datasets.
        library.AddCollection("SECOM", [Source("secom.test", SecomIdentitySetting.Default)]);
        await library.WhenIdle();
        Assert.Empty(library.Collections[0].Sources[0].Index!.Items);

        // The first import becomes the default; the source lists without anything else being done.
        using var identity = Identity();
        service.Accepted = identity.Certificate.Thumbprint;
        trust.DefaultIdentityId = Register(trust, identity);
        await library.WhenIdle();

        var source = library.Collections[0].Sources[0];
        Assert.Single(source.Index!.Items);
        Assert.Equal(SecomPresentationOutcome.Accepted, source.Index.Presentation?.Outcome);
    }

    [Fact]
    public async Task Changing_the_trusted_authorities_reads_again_the_sources_with_an_untrusted_server()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        var service = new Service("untrusted");
        using var library = _context.CreateLibrary(Indexer(service, trust));
        library.Initialize();
        using var refresh = new SecomTrustRefresh(library, trust);

        library.AddCollection("Untrusted", [Source("secom.test", SecomIdentitySetting.None)]);
        await library.WhenIdle();
        var collection = Assert.Single(library.Collections);
        Assert.Equal(SecomDiagnosticCodes.ServerUntrusted, LibraryNodeText.SecomCode(collection.Sources[0].Index!));
        Assert.Equal(SecomPresentationOutcome.Stopped, collection.Sources[0].Index!.Presentation?.Outcome);
        Assert.Equal("Server certificate not trusted", LibraryNodeText.Status(new LibraryNodeStatusInput(collection, collection.Sources)).Line);

        // An identity change is no help to a source on none; trusting its authority is.
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

    private static LibrarySource Locked(CollectionLibrary library) =>
        library.Collections.Single(c => c.Definition.Name == "Locked").Sources[0];

    private static SecomSource Source(string host, string identity) =>
        new(Guid.NewGuid(), null, new Uri($"https://{host}/api/secom/"), SecomFilter.All) { Identity = identity };

    private static string Register(SecomServerTrust trust, SecomClientIdentity identity)
    {
        var id = SecomIdentitySetting.ReferenceIdOf(identity.Certificate.Thumbprint);
        trust.SetIdentity(id, identity);
        return id;
    }

    private CollectionIndexer Indexer(HttpMessageHandler handler, SecomServerTrust trust) =>
        new([new SecomSourceIndexer(new HttpClient(handler), _context.Root) { Trust = trust }]);

    private static SecomClientIdentity Identity(string name = "Test vessel")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
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
    /// the summary (403) until <see cref="Accepts"/>, or when it is presented
    /// the <see cref="Accepted"/> identity; "post-only" has no GET summary, only
    /// SECOM 2.0's signed POST form, which it rejects; "untrusted" fails the TLS
    /// handshake until <see cref="Accepts"/>. It records the identity each
    /// summary request presented.
    /// </summary>
    private sealed class Service(string kind) : HttpMessageHandler
    {
        public string? Accepted { get; set; }

        public List<SecomClientIdentity?> Presented { get; } = [];

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
                request.Options.TryGetValue(SecomServerTrust.IdentityOption, out var presented);
                Presented.Add(presented);
                var accepts = Accepts || (Accepted is not null && presented?.Certificate.Thumbprint == Accepted);
                return kind switch
                {
                    "open" => Respond(HttpStatusCode.OK, Summary),
                    "needs-certificate" => accepts ? Respond(HttpStatusCode.OK, Summary) : Respond(HttpStatusCode.Forbidden, string.Empty),
                    "untrusted" => Accepts ? Respond(HttpStatusCode.OK, Summary)
                        : throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException("untrusted")),
                    _ when request.Method == HttpMethod.Post && request.Content is { Headers.ContentLength: > 2 } => Respond(HttpStatusCode.Forbidden, "{}"),
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
