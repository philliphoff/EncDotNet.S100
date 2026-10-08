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
}

/// <summary>The decision about one host's TLS certificate.</summary>
/// <param name="Outcome">What was decided.</param>
/// <param name="Anchor">The trust anchor the certificate chains to, when it chains to one.</param>
public sealed record SecomServerTrustResult(SecomServerTrustOutcome Outcome, string? Anchor = null)
{
    /// <summary>True when the connection was allowed.</summary>
    public bool Allowed => Outcome is SecomServerTrustOutcome.SystemTrusted or SecomServerTrustOutcome.AnchorTrusted;
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
/// operating system trusts but that names another host is refused. Revocation
/// is not checked.
/// </para>
/// </remarks>
public sealed class SecomServerTrust
{
    private readonly ConcurrentDictionary<string, SecomServerTrustResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>Creates a validator trusting <paramref name="anchors"/>.</summary>
    /// <param name="anchors">The anchors server certificates may chain to; by default <see cref="SecomTrustAnchors.BuiltIn"/>.</param>
    /// <param name="timeProvider">The clock validity periods are judged by.</param>
    public SecomServerTrust(SecomTrustAnchors? anchors = null, TimeProvider? timeProvider = null)
    {
        Anchors = anchors ?? SecomTrustAnchors.BuiltIn;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The anchors server certificates may chain to.</summary>
    public SecomTrustAnchors Anchors { get; }

    /// <summary>
    /// A handler for an <see cref="HttpClient"/> used only for SECOM requests,
    /// validating server certificates with <see cref="Validate"/>.
    /// </summary>
    public HttpClientHandler CreateHandler() => new()
    {
        ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
            request.RequestUri is { } uri && Validate(uri.IdnHost, certificate, chain, errors).Allowed,
    };

    /// <summary>
    /// The last decision about <paramref name="host"/>'s certificate, if a
    /// connection has been made to it through a handler from this instance.
    /// </summary>
    public SecomServerTrustResult? ResultFor(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _results.TryGetValue(host, out var result) ? result : null;
    }

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
        var (anchor, expired) = Anchors.FindServerAnchor(certificate, presented, _time.GetUtcNow());
        if (anchor is null)
            return new SecomServerTrustResult(SecomServerTrustOutcome.NotTrusted);
        if (expired)
            return new SecomServerTrustResult(SecomServerTrustOutcome.Expired, anchor);
        if (!NamesHost(certificate, host))
            return new SecomServerTrustResult(SecomServerTrustOutcome.WrongHost, anchor);
        return new SecomServerTrustResult(SecomServerTrustOutcome.AnchorTrusted, anchor);
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
