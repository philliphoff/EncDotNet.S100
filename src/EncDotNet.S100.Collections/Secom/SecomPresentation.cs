using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>What happened to the identity a SECOM source presented, or why it presented none (#883).</summary>
public enum SecomPresentationOutcome
{
    /// <summary>The service didn't ask for a certificate or a signed request.</summary>
    NotAsked = 0,

    /// <summary>The identity was presented (or signed the requests) and the service answered.</summary>
    Accepted,

    /// <summary>The service asked for a certificate or a signed request, and none was presented.</summary>
    NotPresented,

    /// <summary>The identity was presented (or signed the requests) and the service refused it.</summary>
    Refused,

    /// <summary>The connection stopped before any identity could be presented: the server's certificate is not trusted.</summary>
    Stopped,

    /// <summary>The service could not be reached, or did not answer as a SECOM service.</summary>
    NotReached,
}

/// <summary>A hint the identity picker shows beside an identity, when the service asks for one (#883).</summary>
public enum SecomIdentityHint
{
    /// <summary>No hint: the service doesn't ask, or nothing is known.</summary>
    None = 0,

    /// <summary>The identity's issuer chains to a CA the service named as acceptable (or, failing a list, the service's MCP instance).</summary>
    IssuerAccepted,

    /// <summary>This source already presented the identity and the service refused it.</summary>
    RefusedHere,

    /// <summary>The identity is from an issuer (an MCP instance) the service doesn't accept; it can't be chosen.</summary>
    IssuerNotAccepted,
}

/// <summary>An identity a SECOM source presented and the service refused, remembered for that source.</summary>
/// <param name="IdentityId">The identity's reference id.</param>
/// <param name="At">When it was refused.</param>
/// <param name="Signed">True when it was refused as the signer of a SECOM 2.0 request, not as a client certificate.</param>
public sealed record SecomRefusal(string IdentityId, DateTimeOffset At, bool Signed = false);

/// <summary>
/// What a SECOM source presented when it was last read, and the outcome
/// (#883), recorded on its <see cref="SourceIndex.Presentation"/>.
/// </summary>
/// <param name="Setting">The source's <see cref="SecomSource.Identity"/> setting when it was read.</param>
/// <param name="IdentityId">The identity the setting resolved to, or <see langword="null"/> for none (including a default that isn't set).</param>
/// <param name="Presented">
/// The MRN (or subject) of the identity presented as a client certificate or
/// as the signer of SECOM 2.0 requests, or <see langword="null"/> when none was.
/// </param>
/// <param name="CertificateRequested">
/// True when the server asked for a client certificate during the TLS
/// handshake; <see langword="null"/> when it isn't known (no handshake was seen).
/// </param>
/// <param name="AcceptableIssuers">The distinguished names of the CAs the server said it accepts client certificates from.</param>
/// <param name="Signed">True when the requests were SECOM 2.0 requests signed with the identity.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="At">When the source was read.</param>
public sealed record SecomPresentation(
    string Setting,
    string? IdentityId,
    string? Presented,
    bool? CertificateRequested,
    IReadOnlyList<string> AcceptableIssuers,
    bool Signed,
    SecomPresentationOutcome Outcome,
    DateTimeOffset At)
{
    /// <summary>The <see cref="SecomDiagnosticCodes"/> code of the failure, when the read failed with one.</summary>
    public string? Code { get; init; }

    /// <summary>The trust anchor the server's TLS certificate chains to (e.g. "MCP MCC"), when it chains to one.</summary>
    public string? ServerAnchor { get; init; }

    /// <summary>The identities this source presented and the service refused, oldest first.</summary>
    public IReadOnlyList<SecomRefusal> Refusals
    {
        get => _refusals;
        init => _refusals = value ?? [];
    }

    private readonly IReadOnlyList<SecomRefusal> _refusals = [];

    /// <summary>
    /// True when the service asks for a client certificate or a signed
    /// request: only then does the picker show <see cref="SecomIdentityHint"/>s.
    /// </summary>
    public bool ServiceAsks =>
        CertificateRequested == true
        || Signed
        || Presented is not null
        || Code is SecomDiagnosticCodes.NeedsCertificate or SecomDiagnosticCodes.NeedsSignedRequest
            or SecomDiagnosticCodes.CertificateRefused or SecomDiagnosticCodes.SignedRequestRefused;

    /// <summary>
    /// The hint for <paramref name="identity"/> (registered as <paramref name="id"/>)
    /// on this source; <see cref="SecomIdentityHint.None"/> unless <see cref="ServiceAsks"/>.
    /// See <see cref="SecomIdentityHints.Judge"/>.
    /// </summary>
    public SecomIdentityHint HintFor(string id, SecomClientIdentity identity, SecomTrustAnchors anchors) =>
        ServiceAsks
            ? SecomIdentityHints.Judge(id, identity, anchors, AcceptableIssuers, ServerAnchor, Refusals.Select(r => r.IdentityId))
            : SecomIdentityHint.None;
}

/// <summary>Judges which identities a SECOM service may accept, for the picker's hints (#883).</summary>
/// <remarks>
/// <para>
/// A service that asks for a client certificate during the TLS handshake
/// usually names the CAs it accepts. An identity whose chain (found through
/// the trust anchors) holds one of them is <see cref="SecomIdentityHint.IssuerAccepted"/>;
/// any other is <see cref="SecomIdentityHint.IssuerNotAccepted"/>.
/// </para>
/// <para>
/// SECOM 2.0 signed-request services (KHRA, KRISO), and some TLS servers,
/// name no CAs. Then the fallback is the MCP instance: an identity issued
/// under the same trust anchor as the server's own certificate (the same
/// MCP instance, e.g. "MCP MCC") is accepted, one under another anchor is
/// not, and when the server's certificate chains to no anchor (a public CA)
/// nothing is suggested. When the service's MRN is known, the MRNs' instance
/// parts (<c>urn:mrn:mcp:&lt;type&gt;:&lt;instance&gt;:…</c>) are compared too.
/// </para>
/// <para>
/// A refusal remembered for the source wins over both: it's
/// <see cref="SecomIdentityHint.RefusedHere"/>. Nothing is ever chosen from a
/// hint: the user picks.
/// </para>
/// </remarks>
public static class SecomIdentityHints
{
    /// <summary>The hint for one identity.</summary>
    /// <param name="id">The identity's reference id.</param>
    /// <param name="identity">The identity.</param>
    /// <param name="anchors">The trust anchors its chain is found through.</param>
    /// <param name="acceptableIssuers">The CAs the server named, by distinguished name.</param>
    /// <param name="serverAnchor">The anchor the server's own certificate chains to, if any.</param>
    /// <param name="refusedIds">The identities the source already presented and saw refused.</param>
    /// <param name="serviceMrn">The service's MRN, when known (from the service registry).</param>
    public static SecomIdentityHint Judge(
        string id,
        SecomClientIdentity identity,
        SecomTrustAnchors anchors,
        IReadOnlyList<string> acceptableIssuers,
        string? serverAnchor,
        IEnumerable<string> refusedIds,
        string? serviceMrn = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(acceptableIssuers);
        if (refusedIds.Any(r => string.Equals(r, id, StringComparison.OrdinalIgnoreCase)))
            return SecomIdentityHint.RefusedHere;

        if (acceptableIssuers.Count > 0)
            return ChainHoldsAny(identity, anchors, acceptableIssuers) ? SecomIdentityHint.IssuerAccepted : SecomIdentityHint.IssuerNotAccepted;

        if (Instance(serviceMrn) is { } serviceInstance && Instance(identity.Mrn) is { } identityInstance)
        {
            return string.Equals(serviceInstance, identityInstance, StringComparison.OrdinalIgnoreCase)
                ? SecomIdentityHint.IssuerAccepted
                : SecomIdentityHint.IssuerNotAccepted;
        }

        if (serverAnchor is null)
            return SecomIdentityHint.None;
        return string.Equals(serverAnchor, identity.Anchor, StringComparison.Ordinal)
            ? SecomIdentityHint.IssuerAccepted
            : SecomIdentityHint.IssuerNotAccepted;
    }

    /// <summary>The MCP instance part of an MRN (<c>urn:mrn:mcp:vessel:mcc:…</c> → <c>mcc</c>), or <see langword="null"/>.</summary>
    public static string? Instance(string? mrn)
    {
        if (mrn is null || !mrn.StartsWith("urn:mrn:mcp:", StringComparison.OrdinalIgnoreCase))
            return null;
        var parts = mrn.Split(':');
        return parts.Length > 4 && parts[4].Length > 0 ? parts[4] : null;
    }

    private static bool ChainHoldsAny(SecomClientIdentity identity, SecomTrustAnchors anchors, IReadOnlyList<string> acceptableIssuers)
    {
        var accepted = new HashSet<string>(acceptableIssuers.Select(Canonical), StringComparer.OrdinalIgnoreCase);
        IEnumerable<X500DistinguishedName> issuers = [identity.Certificate.IssuerName];
        if (anchors.FindAnchor(identity.Certificate, [], statedThumbprint: null, out var chain) is not null && chain is not null)
            issuers = issuers.Concat(chain.Skip(1).Select(c => c.SubjectName));
        return issuers.Any(name => accepted.Contains(Canonical(name.Name)));
    }

    /// <summary>A distinguished name with spacing after separators removed, for comparison.</summary>
    private static string Canonical(string name) =>
        string.Join(',', name.Split(',').Select(part => part.Trim()));
}
