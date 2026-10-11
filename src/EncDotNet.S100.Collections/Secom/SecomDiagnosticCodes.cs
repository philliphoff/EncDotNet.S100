using System.Net;
using System.Security.Authentication;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// Codes on the <see cref="IndexDiagnostic"/>s of a SECOM source that could not
/// be listed (#880): why, in terms a host can explain and act on, such as by
/// leading to where an MCP identity or a trusted authority is set.
/// </summary>
public static class SecomDiagnosticCodes
{
    /// <summary>The service shares data only with a client certificate, and no identity is set.</summary>
    public const string NeedsCertificate = "secom.needs-certificate";

    /// <summary>The service lists only through SECOM 2.0's signed requests, and no identity is set to sign them.</summary>
    public const string NeedsSignedRequest = "secom.needs-signed-request";

    /// <summary>The source's identity was presented as a client certificate, and the service refused it.</summary>
    public const string CertificateRefused = "secom.certificate-refused";

    /// <summary>The source's identity signed the SECOM 2.0 requests, and the service rejected them (#883).</summary>
    public const string SignedRequestRefused = "secom.signed-request-refused";

    /// <summary>The service's TLS certificate is not trusted.</summary>
    public const string ServerUntrusted = "secom.server-untrusted";

    /// <summary>
    /// True for the codes a change of identity may resolve: a source with one
    /// is worth reading again when its identity is set, replaced or cleared.
    /// </summary>
    public static bool IsIdentityMatter(string? code) =>
        code is NeedsCertificate or NeedsSignedRequest or CertificateRefused or SignedRequestRefused;

    /// <summary>
    /// The code for a failure to read a SECOM service, or <see langword="null"/>
    /// when it is not one of these (an unreachable host, a bad answer).
    /// </summary>
    /// <param name="failure">What reading the service threw.</param>
    /// <param name="identity">
    /// The identity the source presents (<see langword="null"/> for none). It
    /// counts as presented when the server asked for a client certificate, or
    /// when no handshake was seen to say (<paramref name="handshake"/> is null).
    /// </param>
    /// <param name="signed">True when the failing requests were SECOM 2.0 requests signed with the identity.</param>
    /// <param name="serverTrust">What was decided about the server's TLS certificate, when known.</param>
    /// <param name="handshake">What the TLS handshake with the identity showed, when one was seen.</param>
    public static string? Classify(
        Exception failure,
        SecomClientIdentity? identity,
        bool signed = false,
        SecomServerTrustResult? serverTrust = null,
        SecomHandshake? handshake = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var presented = identity is not null && (handshake?.CertificateRequested ?? true);
        if (failure is SecomIdentityRequiredException)
            return NeedsSignedRequest;
        if (failure is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            return signed ? SignedRequestRefused : presented ? CertificateRefused : NeedsCertificate;

        // The server's certificate was accepted and it asked for a client
        // certificate, yet the connection failed: the handshake was refused
        // for want of an (acceptable) client certificate.
        // Where the request isn't seen (Windows: no handshake recorded), a failed
        // connection that presented an identity to a trusted server reads as its refusal.
        if (failure is HttpRequestException { StatusCode: null } && serverTrust is { Allowed: true }
            && (handshake?.CertificateRequested ?? identity is not null))
            return identity is not null ? CertificateRefused : NeedsCertificate;

        for (var inner = failure; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
                return ServerUntrusted;
        }

        return null;
    }
}
