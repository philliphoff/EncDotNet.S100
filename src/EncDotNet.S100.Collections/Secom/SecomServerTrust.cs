using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>What <see cref="SecomServerTrust"/> decided about a server's TLS certificate.</summary>
public enum SecomServerTrustOutcome
{
    /// <summary>The operating system trusts the certificate; nothing more was needed.</summary>
    SystemTrusted = 0,

    /// <summary>The certificate chains to a SECOM trust anchor and names the host.</summary>
    AnchorTrusted,

    /// <summary>The certificate chains to no trusted root.</summary>
    NotTrusted,

    /// <summary>The certificate, or a CA above it, is outside its validity period.</summary>
    Expired,

    /// <summary>The certificate does not name the host that was asked for.</summary>
    WrongHost,

    /// <summary>The certificate, or a CA above it, is listed on its issuer's CRL (#833).</summary>
    Revoked,
}

/// <summary>The decision about one host's TLS certificate.</summary>
/// <param name="Outcome">What was decided.</param>
/// <param name="Anchor">The trust anchor the certificate chains to, when it chains to one.</param>
public sealed record SecomServerTrustResult(SecomServerTrustOutcome Outcome, string? Anchor = null)
{
    /// <summary>True when the connection was allowed.</summary>
    public bool Allowed => Outcome is SecomServerTrustOutcome.SystemTrusted or SecomServerTrustOutcome.AnchorTrusted;

    /// <summary>
    /// For an <see cref="SecomServerTrustOutcome.AnchorTrusted"/> certificate,
    /// whether its revocation was checked (#833): <see cref="SecomRevocationStatus.NotRevoked"/>,
    /// or <see cref="SecomRevocationStatus.NotChecked"/> when no checker was
    /// given or no current CRL could be had (the connection is still allowed).
    /// A revoked certificate is refused as <see cref="SecomServerTrustOutcome.Revoked"/>.
    /// The system's own trust decides <see cref="SecomServerTrustOutcome.SystemTrusted"/>
    /// certificates, so this is <see cref="SecomRevocationStatus.NotChecked"/> for them.
    /// </summary>
    public SecomRevocationStatus Revocation { get; init; }
}

/// <summary>
/// Trusts TLS server certificates issued under <see cref="SecomTrustAnchors"/>
/// for SECOM requests (issue #829). Many SECOM services present Maritime
/// Connectivity Platform (MCP) server certificates, whose roots are not in
/// operating-system trust stores, so the handshake fails before SECOM is asked.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="CreateHandler"/> only for SECOM traffic: it widens trust for
/// the client it is given to and nothing else. A connection is allowed when
/// the operating system trusts it, or when it rejects it <em>because of the
/// chain</em> and the chain reaches a trust anchor, with every certificate in
/// its validity period, and the certificate names the host.
/// </para>
/// <para>
/// The host name is never skipped. For an anchor-trusted certificate it is
/// checked here, the same way on every platform: against the DNS names in the
/// subject alternative name (a wildcard covers one left-most label), else IP
/// addresses, and — only when the certificate lists no DNS names — the subject
/// common name. MCP device certificates (AMSA's, for one) carry the host only in
/// the common name, with the MRN as the sole alternative name. A certificate the
/// operating system trusts but that names another host is refused.
/// </para>
/// <para>
/// Given a <see cref="SecomRevocation"/>, an anchor-trusted chain is also
/// checked against its CAs' CRLs (#833): a revoked certificate is refused, and
/// one whose revocation cannot be checked is allowed with
/// <see cref="SecomServerTrustResult.Revocation"/> saying so. Certificates the
/// operating system trusts are left to its own revocation policy.
/// </para>
/// <para>
/// The handlers also present this client's MCP identity, when one is set with
/// <see cref="SetIdentity"/>, to SECOM services that ask for a client
/// certificate (mutual TLS, #832). The identity can change at any time: the
/// next request through each handler connects with the new one.
/// </para>
/// </remarks>
public sealed class SecomServerTrust
{
    private readonly ConcurrentDictionary<string, SecomServerTrustResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;
    private SecomClientIdentity? _identity;
    private int _identityVersion;

    /// <summary>Creates a validator trusting <paramref name="anchors"/>.</summary>
    /// <param name="anchors">The anchors server certificates may chain to; by default <see cref="SecomTrustAnchors.BuiltIn"/>.</param>
    /// <param name="timeProvider">The clock validity periods are judged by.</param>
    /// <param name="revocation">Checks anchor-trusted chains for revocation; <see langword="null"/> skips the check.</param>
    public SecomServerTrust(SecomTrustAnchors? anchors = null, TimeProvider? timeProvider = null, SecomRevocation? revocation = null)
    {
        Anchors = anchors ?? SecomTrustAnchors.BuiltIn;
        _time = timeProvider ?? TimeProvider.System;
        Revocation = revocation;
    }

    /// <summary>The anchors server certificates may chain to.</summary>
    public SecomTrustAnchors Anchors { get; }

    /// <summary>Checks anchor-trusted chains for revocation, when set.</summary>
    public SecomRevocation? Revocation { get; }

    /// <summary>The MCP identity presented to SECOM services that ask for a client certificate, if one is set.</summary>
    public SecomClientIdentity? Identity => Volatile.Read(ref _identity);

    /// <summary>Raised after <see cref="SetIdentity"/> changes the identity.</summary>
    public event EventHandler? IdentityChanged;

    /// <summary>
    /// Sets (or, with <see langword="null"/>, clears) the identity presented
    /// to SECOM services. Handlers connect with it from their next request;
    /// connections made with the previous identity are no longer used. The
    /// previous identity is not disposed: its owner may still hold it.
    /// </summary>
    public void SetIdentity(SecomClientIdentity? identity)
    {
        Volatile.Write(ref _identity, identity);
        Interlocked.Increment(ref _identityVersion);
        IdentityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A handler for an <see cref="HttpClient"/> used only for SECOM requests:
    /// it validates server certificates with <see cref="Validate"/> and, unless
    /// <paramref name="presentIdentity"/> is false, presents <see cref="Identity"/>
    /// to a service that asks for a client certificate.
    /// </summary>
    /// <param name="presentIdentity">False for a handler that never presents an identity (anonymous probes).</param>
    public HttpMessageHandler CreateHandler(bool presentIdentity = true) => new IdentityHandler(this, presentIdentity);

    /// <summary>
    /// The last decision about <paramref name="host"/>'s certificate, if a
    /// connection has been made to it through a handler from this instance.
    /// </summary>
    public SecomServerTrustResult? ResultFor(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _results.TryGetValue(host, out var result) ? result : null;
    }

    /// <summary>Forgets the last decision about <paramref name="host"/>, so a probe sees only its own.</summary>
    internal void Forget(string host) => _results.TryRemove(host, out _);

    /// <summary>Decides whether to allow a TLS connection to <paramref name="host"/>, and records the decision.</summary>
    /// <param name="host">The host the request was made to.</param>
    /// <param name="certificate">The server's certificate.</param>
    /// <param name="chain">The chain the platform built, holding the certificates the server presented.</param>
    /// <param name="errors">What the platform found wrong.</param>
    public SecomServerTrustResult Validate(string host, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        ArgumentNullException.ThrowIfNull(host);
        var result = Decide(host, certificate, chain, errors);
        _results[host] = result;
        return result;
    }

    /// <summary>
    /// Sends through an <see cref="HttpClientHandler"/> made for the current
    /// identity, replacing it when the identity changes. Replaced handlers are
    /// kept until this one is disposed, so requests still running on them
    /// finish; their idle connections close on their own.
    /// </summary>
    private sealed class IdentityHandler(SecomServerTrust trust, bool presentIdentity) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<HttpMessageInvoker> _retired = [];
        private HttpMessageInvoker? _current;
        private int _version = -1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Current().SendAsync(request, cancellationToken);

        private HttpMessageInvoker Current()
        {
            var version = Volatile.Read(ref trust._identityVersion);
            lock (_gate)
            {
                if (_current is not null && (!presentIdentity || _version == version))
                    return _current;

                if (_current is not null)
                    _retired.Add(_current);
                _version = version;
                return _current = new HttpMessageInvoker(Create(presentIdentity ? trust.Identity : null), disposeHandler: true);
            }
        }

        private HttpClientHandler Create(SecomClientIdentity? identity)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    request.RequestUri is { } uri && trust.Validate(uri.IdnHost, certificate, chain, errors).Allowed,
            };
            if (identity is not null)
            {
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ClientCertificates.Add(identity.Certificate);
            }

            return handler;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_gate)
                {
                    _current?.Dispose();
                    _retired.ForEach(r => r.Dispose());
                    _retired.Clear();
                }
            }

            base.Dispose(disposing);
        }
    }

    private SecomServerTrustResult Decide(string host, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return new SecomServerTrustResult(SecomServerTrustOutcome.SystemTrusted);
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            return new SecomServerTrustResult(SecomServerTrustOutcome.NotTrusted);

        // A chain the system trusts, for another host: never widened.
        if (!errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            return new SecomServerTrustResult(SecomServerTrustOutcome.WrongHost);

        var presented = chain?.ChainElements.Select(e => e.Certificate).Skip(1) ?? [];
        var (anchor, expired) = Anchors.FindServerAnchor(certificate, presented, _time.GetUtcNow(), out var elements);
        if (anchor is null || elements is null)
            return new SecomServerTrustResult(SecomServerTrustOutcome.NotTrusted);
        if (expired)
            return new SecomServerTrustResult(SecomServerTrustOutcome.Expired, anchor);
        if (!NamesHost(certificate, host))
            return new SecomServerTrustResult(SecomServerTrustOutcome.WrongHost, anchor);

        // Last, so a refused certificate costs no CRL fetch.
        var revocation = Revocation?.Check(elements).Status ?? SecomRevocationStatus.NotChecked;
        return revocation == SecomRevocationStatus.Revoked
            ? new SecomServerTrustResult(SecomServerTrustOutcome.Revoked, anchor) { Revocation = revocation }
            : new SecomServerTrustResult(SecomServerTrustOutcome.AnchorTrusted, anchor) { Revocation = revocation };
    }

    /// <summary>
    /// Whether <paramref name="certificate"/> names <paramref name="host"/>:
    /// a DNS name in its subject alternative name (a wildcard covers one
    /// left-most label), an IP address there for an IP host, or — only when it
    /// lists no DNS names — its subject common name, matched exactly.
    /// </summary>
    public static bool NamesHost(X509Certificate2 certificate, string host)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(host);
        host = host.TrimEnd('.');
        var san = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        var dnsNames = san?.EnumerateDnsNames().ToArray() ?? [];

        if (IPAddress.TryParse(host, out var address))
            return san is not null && san.EnumerateIPAddresses().Any(address.Equals);
        if (dnsNames.Length > 0)
            return dnsNames.Any(name => DnsNameMatches(name, host));
        return CommonNames(certificate).Any(cn => string.Equals(cn.TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DnsNameMatches(string pattern, string host)
    {
        pattern = pattern.TrimEnd('.');
        if (!pattern.StartsWith("*.", StringComparison.Ordinal))
            return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);

        var dot = host.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && string.Equals(pattern[1..], host[dot..], StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CommonNames(X509Certificate2 certificate)
    {
        const string commonName = "2.5.4.3";
        foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            string? value;
            try
            {
                value = rdn.GetSingleElementType().Value == commonName ? rdn.GetSingleElementValue() : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or AsnContentException)
            {
                value = null;
            }

            if (value is { Length: > 0 })
                yield return value;
        }
    }
}
