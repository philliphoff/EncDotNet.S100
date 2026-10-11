using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
/// for SECOM requests (issue #829), and presents each request's own MCP
/// identity to services that ask for a client certificate (#832, #883).
/// Many SECOM services present Maritime Connectivity Platform (MCP) server
/// certificates, whose roots are not in operating-system trust stores, so the
/// handshake fails before SECOM is asked.
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
/// Identities (#883): the host registers the identities it holds with
/// <see cref="SetIdentity(string, SecomClientIdentity?)"/>, and names one
/// <see cref="DefaultIdentityId"/>. Each SECOM source's
/// <see cref="SecomSource.Identity"/> setting is resolved with
/// <see cref="Resolve"/> when the source is read, and every request is tagged
/// with the identity it presents (<see cref="PresentIdentity"/>). The handlers
/// keep one connection pool per identity, and present the request's identity
/// only, and only when the service asks for a client certificate. An untagged
/// request presents none. Nothing here ever tries another identity when one
/// is refused: each attempt would disclose an MRN to the provider.
/// </para>
/// </remarks>
public sealed class SecomServerTrust
{
    /// <summary>The request option <see cref="PresentIdentity"/> sets: the identity a request presents.</summary>
    public static readonly HttpRequestOptionsKey<SecomClientIdentity?> IdentityOption = new("EncDotNet.S100.Secom.Identity");

    private readonly ConcurrentDictionary<string, SecomServerTrustResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Key, string Host), SecomHandshake> _handshakes = new(HandshakeKeyComparer.Instance);
    private readonly ConditionalWeakTable<SslStream, HandshakeFacts> _streams = new();
    private readonly object _identitiesGate = new();
    private readonly TimeProvider _time;
    private Dictionary<string, SecomClientIdentity> _identities = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultIdentityId;
    private SecomTrustAnchors _anchors;
    private int _anchorsVersion;

    /// <summary>Creates a validator trusting <paramref name="anchors"/>.</summary>
    /// <param name="anchors">The anchors server certificates may chain to; by default <see cref="SecomTrustAnchors.BuiltIn"/>.</param>
    /// <param name="timeProvider">The clock validity periods are judged by.</param>
    /// <param name="revocation">Checks anchor-trusted chains for revocation; <see langword="null"/> skips the check.</param>
    public SecomServerTrust(SecomTrustAnchors? anchors = null, TimeProvider? timeProvider = null, SecomRevocation? revocation = null)
    {
        _anchors = anchors ?? SecomTrustAnchors.BuiltIn;
        _time = timeProvider ?? TimeProvider.System;
        Revocation = revocation;
    }

    /// <summary>
    /// The anchors server certificates may chain to. A host may replace them
    /// (e.g. when the user turns a built-in root off or adds one, #845):
    /// decisions recorded under the previous anchors are forgotten, and
    /// handlers make new connections from their next request.
    /// </summary>
    public SecomTrustAnchors Anchors
    {
        get => Volatile.Read(ref _anchors);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Volatile.Write(ref _anchors, value);
            _results.Clear();
            _handshakes.Clear();
            Interlocked.Increment(ref _anchorsVersion);
            AnchorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after <see cref="Anchors"/> is replaced.</summary>
    public event EventHandler? AnchorsChanged;

    /// <summary>Checks anchor-trusted chains for revocation, when set.</summary>
    public SecomRevocation? Revocation { get; }

    /// <summary>The identities registered with <see cref="SetIdentity(string, SecomClientIdentity?)"/>, by reference id.</summary>
    public IReadOnlyDictionary<string, SecomClientIdentity> Identities
    {
        get
        {
            lock (_identitiesGate)
                return _identities;
        }
    }

    /// <summary>
    /// The reference id of the default identity, which sources set to
    /// <see cref="SecomIdentitySetting.Default"/> present; <see langword="null"/>
    /// when there is none. It may name an identity that is not registered
    /// (yet): such sources then present none. Setting it raises
    /// <see cref="IdentitiesChanged"/> when it changes.
    /// </summary>
    public string? DefaultIdentityId
    {
        get => Volatile.Read(ref _defaultIdentityId);
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
            string? previous;
            lock (_identitiesGate)
            {
                previous = _defaultIdentityId;
                if (string.Equals(previous, normalized, StringComparison.Ordinal))
                    return;
                Volatile.Write(ref _defaultIdentityId, normalized);
            }

            IdentitiesChanged?.Invoke(this, new SecomIdentitiesChangedEventArgs(SecomIdentityChange.DefaultChanged, normalized, previous));
        }
    }

    /// <summary>The default identity, when one is set and registered.</summary>
    public SecomClientIdentity? Identity => Resolve(SecomIdentitySetting.Default).Identity;

    /// <summary>
    /// The SECOM <c>digitalSignatureReference</c> SECOM 2.0 request envelopes
    /// are signed with (#838), e.g. <c>ecdsa-384-sha2</c>; <see langword="null"/>
    /// (the default) follows the identity's key. A service checks a search
    /// envelope with the algorithm it is configured for, so this may need
    /// matching to it.
    /// </summary>
    public string? EnvelopeSignatureReference { get; set; }

    /// <summary>Raised when an identity is registered, replaced or removed, or the default changes.</summary>
    public event EventHandler<SecomIdentitiesChangedEventArgs>? IdentitiesChanged;

    /// <summary>
    /// Registers <paramref name="identity"/> under <paramref name="id"/>,
    /// replacing any identity registered there, or (with <see langword="null"/>)
    /// removes it. Requests tagged with a replaced or removed identity still
    /// connect with it; sources resolve to the new one from their next read.
    /// An identity removed or replaced is not disposed: its owner may still hold it.
    /// </summary>
    /// <param name="id">The reference id, e.g. <c>sc-ident:4f9c2a7e</c>.</param>
    /// <param name="identity">The identity, or <see langword="null"/> to remove it.</param>
    public void SetIdentity(string id, SecomClientIdentity? identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        id = id.Trim().ToLowerInvariant();
        SecomIdentityChange change;
        lock (_identitiesGate)
        {
            var had = _identities.TryGetValue(id, out var previous);
            if (identity is null && !had)
                return;
            if (identity is not null && ReferenceEquals(previous, identity))
                return;

            var copy = new Dictionary<string, SecomClientIdentity>(_identities, StringComparer.OrdinalIgnoreCase);
            if (identity is null)
            {
                copy.Remove(id);
                change = SecomIdentityChange.Removed;
            }
            else
            {
                copy[id] = identity;
                change = had ? SecomIdentityChange.Replaced : SecomIdentityChange.Added;
            }

            _identities = copy;
        }

        IdentitiesChanged?.Invoke(this, new SecomIdentitiesChangedEventArgs(change, id, DefaultIdentityId));
    }

    /// <summary>
    /// Makes <paramref name="identity"/> the default, registered under its
    /// <see cref="SecomIdentitySetting.ReferenceIdOf">reference id</see>, or
    /// (with <see langword="null"/>) leaves no default: for a host that holds
    /// one identity at a time (<c>s100 mcp serve</c>, <c>--secom-identity</c>).
    /// </summary>
    public void SetIdentity(SecomClientIdentity? identity)
    {
        if (identity is null)
        {
            DefaultIdentityId = null;
            return;
        }

        var id = SecomIdentitySetting.ReferenceIdOf(identity.Certificate.Thumbprint);
        SetIdentity(id, identity);
        DefaultIdentityId = id;
    }

    /// <summary>
    /// Resolves a source's <see cref="SecomSource.Identity"/> setting to the
    /// identity it presents now: <see cref="SecomIdentitySetting.Default"/> (or
    /// <see langword="null"/>) to the default identity, none when no default is
    /// set; <see cref="SecomIdentitySetting.None"/> to none; a reference id to
    /// that identity, or none when it is not registered. Anything else is none.
    /// </summary>
    public SecomResolvedIdentity Resolve(string? setting)
    {
        if (setting is null || string.Equals(setting, SecomIdentitySetting.Default, StringComparison.OrdinalIgnoreCase))
        {
            var id = DefaultIdentityId;
            return new SecomResolvedIdentity(SecomIdentitySetting.Default, id, id is null ? null : Identities.GetValueOrDefault(id));
        }

        if (SecomIdentitySetting.IsReference(setting))
        {
            var id = setting.ToLowerInvariant();
            return new SecomResolvedIdentity(id, id, Identities.GetValueOrDefault(id));
        }

        return SecomResolvedIdentity.None;
    }

    /// <summary>
    /// A signer for SECOM 2.0 request envelopes with <paramref name="identity"/>,
    /// or <see langword="null"/> for none.
    /// </summary>
    /// <exception cref="InvalidDataException">The identity cannot sign with <see cref="EnvelopeSignatureReference"/>.</exception>
    public SecomEnvelopeSigner? CreateSigner(SecomClientIdentity? identity) =>
        identity is null ? null : new SecomEnvelopeSigner(identity, Anchors, EnvelopeSignatureReference);

    /// <summary>A signer with the default identity (<see cref="Identity"/>), or <see langword="null"/> when there is none.</summary>
    /// <exception cref="InvalidDataException">The identity cannot sign with <see cref="EnvelopeSignatureReference"/>.</exception>
    public SecomEnvelopeSigner? CreateSigner() => CreateSigner(Identity);

    /// <summary>
    /// Tags <paramref name="request"/> with the identity it presents through a
    /// handler from <see cref="CreateHandler"/>: <see langword="null"/> for none.
    /// </summary>
    public static void PresentIdentity(HttpRequestMessage request, SecomClientIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(IdentityOption, identity);
    }

    /// <summary>Raised when <see cref="CheckIdentityRevocation"/> finds a registered identity revoked and removes it.</summary>
    public event EventHandler<SecomIdentityRevokedEventArgs>? IdentityRevoked;

    /// <summary>
    /// Checks every registered identity against its CAs' CRLs (#845). A revoked
    /// identity is removed at once (sources that resolved to it present none)
    /// and <see cref="IdentityRevoked"/> is raised.
    /// </summary>
    /// <param name="fetch">
    /// True to fetch CRLs that are missing or due; false to judge only from
    /// CRLs already held (<see cref="SecomRevocation.CacheOnly"/>).
    /// </param>
    /// <returns>
    /// The result for each identity checked, by reference id;
    /// <see cref="SecomRevocationStatus.NotChecked"/> for every identity when
    /// there is no <see cref="Revocation"/>, and for one that chains to none
    /// of <see cref="Anchors"/>.
    /// </returns>
    public IReadOnlyDictionary<string, SecomRevocationResult> CheckIdentityRevocation(bool fetch = true)
    {
        var results = new Dictionary<string, SecomRevocationResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, identity) in Identities)
        {
            if (Revocation is null)
            {
                results[id] = new SecomRevocationResult(SecomRevocationStatus.NotChecked);
                continue;
            }

            if (Anchors.FindAnchor(identity.Certificate, [], statedThumbprint: null, out var chain) is null || chain is null)
            {
                results[id] = new SecomRevocationResult(SecomRevocationStatus.NotChecked, "The identity chains to no trusted authority.");
                continue;
            }

            var result = (fetch ? Revocation : Revocation.CacheOnly).Check(chain);
            results[id] = result;
            if (result.Status == SecomRevocationStatus.Revoked && ReferenceEquals(Identities.GetValueOrDefault(id), identity))
            {
                SetIdentity(id, null);
                IdentityRevoked?.Invoke(this, new SecomIdentityRevokedEventArgs(identity, result) { Id = id });
            }
        }

        return results;
    }

    /// <summary>
    /// A handler for an <see cref="HttpClient"/> used only for SECOM requests:
    /// it validates server certificates with <see cref="Validate"/> and
    /// presents each request's identity (<see cref="PresentIdentity"/>) to a
    /// service that asks for a client certificate.
    /// </summary>
    public HttpMessageHandler CreateHandler() => new IdentityHandler(this);

    /// <summary>
    /// The last decision about <paramref name="host"/>'s certificate, if a
    /// connection has been made to it through a handler from this instance.
    /// </summary>
    public SecomServerTrustResult? ResultFor(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _results.TryGetValue(host, out var result) ? result : null;
    }

    /// <summary>
    /// What the last TLS handshake with <paramref name="host"/> that presented
    /// <paramref name="identity"/> (or, with <see langword="null"/>, none)
    /// showed: whether the server asked for a client certificate, and which
    /// issuers it said it accepts. <see langword="null"/> when no connection
    /// has been made that way through a handler from this instance.
    /// </summary>
    public SecomHandshake? HandshakeFor(string host, SecomClientIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _handshakes.TryGetValue((KeyOf(identity), host), out var handshake) ? handshake : null;
    }

    /// <summary>Forgets the last decision and handshakes for <paramref name="host"/>, so a probe sees only its own.</summary>
    internal void Forget(string host)
    {
        _results.TryRemove(host, out _);
        foreach (var key in _handshakes.Keys.Where(k => string.Equals(k.Host, host, StringComparison.OrdinalIgnoreCase)))
            _handshakes.TryRemove(key, out _);
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

    private static string KeyOf(SecomClientIdentity? identity) => identity?.Certificate.Thumbprint ?? string.Empty;

    /// <summary>
    /// Records what one TLS connection showed, merging what its two callbacks
    /// see (the server's certificate; the request for a client certificate),
    /// in whichever order the platform calls them.
    /// </summary>
    private void Observe(SslStream stream, string key, string host, string[]? acceptableIssuers)
    {
        var facts = _streams.GetValue(stream, static _ => new HandshakeFacts());
        lock (facts)
        {
            if (acceptableIssuers is not null)
            {
                facts.Requested = true;
                facts.Issuers = acceptableIssuers;
            }

            _handshakes[(key, host)] = new SecomHandshake(host, facts.Requested, facts.Issuers, _time.GetUtcNow());
        }
    }

    private sealed class HandshakeFacts
    {
        public bool Requested { get; set; }

        public IReadOnlyList<string> Issuers { get; set; } = [];
    }

    private sealed class HandshakeKeyComparer : IEqualityComparer<(string Key, string Host)>
    {
        public static readonly HandshakeKeyComparer Instance = new();

        public bool Equals((string Key, string Host) x, (string Key, string Host) y) =>
            string.Equals(x.Key, y.Key, StringComparison.Ordinal) && string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Key, string Host) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Key), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Host));
    }

    /// <summary>
    /// Sends each request through a <see cref="SocketsHttpHandler"/> made for
    /// the identity it is tagged with (none when untagged), so connections
    /// made with one identity are never reused with another. Handlers are
    /// replaced when the anchors change; replaced ones are kept until this one
    /// is disposed, so requests still running on them finish.
    /// </summary>
    private sealed class IdentityHandler(SecomServerTrust trust) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<HttpMessageInvoker> _retired = [];
        private readonly Dictionary<string, HttpMessageInvoker> _current = new(StringComparer.Ordinal);
        private int _anchorsVersion = -1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.TryGetValue(IdentityOption, out var identity);
            return InvokerFor(identity).SendAsync(request, cancellationToken);
        }

        private HttpMessageInvoker InvokerFor(SecomClientIdentity? identity)
        {
            var anchorsVersion = Volatile.Read(ref trust._anchorsVersion);
            var key = KeyOf(identity);
            lock (_gate)
            {
                if (_anchorsVersion != anchorsVersion)
                {
                    _retired.AddRange(_current.Values);
                    _current.Clear();
                    _anchorsVersion = anchorsVersion;
                }

                if (!_current.TryGetValue(key, out var invoker))
                    _current[key] = invoker = new HttpMessageInvoker(Create(identity, key), disposeHandler: true);
                return invoker;
            }
        }

        private static readonly HttpRequestOptionsKey<HashSet<string>> ConnectedKey = new("EncDotNet.S100.Secom.Connected");

        private SocketsHttpHandler Create(SecomClientIdentity? identity, string key)
        {
            var handler = new SocketsHttpHandler();
            if (identity is not null)
            {
                // SocketsHttpHandler retries a request on a new connection when one
                // closes before answering, as a server that refuses a client
                // certificate does: each retry would present the identity again.
                // One new connection per request and host; a stale pooled
                // connection still gets its one.
                handler.ConnectCallback = async (context, cancellationToken) =>
                {
                    var request = context.InitialRequestMessage;
                    if (!request.Options.TryGetValue(ConnectedKey, out var connected))
                        request.Options.Set(ConnectedKey, connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    lock (connected)
                    {
                        if (!connected.Add(context.DnsEndPoint.Host))
                        {
                            throw new HttpRequestException(
                                $"The connection to {context.DnsEndPoint.Host} closed before it answered; it is not made again, so as not to present the identity again.");
                        }
                    }

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                };
            }

            handler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            {
                if (sender is not SslStream stream)
                    return false;
                var host = stream.TargetHostName;
                // Schannel doesn't call back for a request it answers with the
                // identity offered up front; it does report whether that was sent.
                // With no identity, whether the server asked stays unknown there.
                if (!OperatingSystem.IsWindows())
                    trust.Observe(stream, key, host, acceptableIssuers: null);
                else if (identity is not null)
                    trust.Observe(stream, key, host, stream.LocalCertificate is null ? null : []);
                return trust.Validate(host, certificate as X509Certificate2, chain, errors).Allowed;
            };

            // Called once before the handshake on some platforms (no server
            // certificate yet), and when the server asks for a client
            // certificate: only then is the identity presented. Windows offers
            // it up front: without, Schannel resumes the host's anonymous
            // session rather than asking again. TLS sends it only on request.
            handler.SslOptions.LocalCertificateSelectionCallback = (sender, host, _, remote, issuers) =>
            {
                if (remote is null)
                    return OperatingSystem.IsWindows() ? identity?.Certificate! : null!;
                if (sender is SslStream stream)
                    trust.Observe(stream, key, host, issuers ?? []);
                return identity?.Certificate!;
            };
            return handler;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_gate)
                {
                    foreach (var invoker in _current.Values)
                        invoker.Dispose();
                    _current.Clear();
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

/// <summary>The identity <see cref="SecomServerTrust.CheckIdentityRevocation"/> found revoked and removed.</summary>
/// <param name="identity">The revoked identity; the caller still owns it.</param>
/// <param name="result">The revocation result, whose detail names the date.</param>
public sealed class SecomIdentityRevokedEventArgs(SecomClientIdentity identity, SecomRevocationResult result) : EventArgs
{
    /// <summary>The revoked identity.</summary>
    public SecomClientIdentity Identity { get; } = identity;

    /// <summary>The revocation result.</summary>
    public SecomRevocationResult Result { get; } = result;

    /// <summary>The reference id it was registered under, when known.</summary>
    public string? Id { get; init; }
}
