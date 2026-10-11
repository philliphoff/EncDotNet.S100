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

    /// <summary>The identity in use was presented, and the service refused it.</summary>
    public const string CertificateRefused = "secom.certificate-refused";

    /// <summary>The service's TLS certificate is not trusted.</summary>
    public const string ServerUntrusted = "secom.server-untrusted";

    /// <summary>
    /// True for the codes a change of identity may resolve: a source with one
    /// is worth reading again when the identity is set, replaced or cleared.
    /// </summary>
    public static bool IsIdentityMatter(string? code) =>
        code is NeedsCertificate or NeedsSignedRequest or CertificateRefused;

    /// <summary>
    /// The code for a failure to read a SECOM service, or <see langword="null"/>
    /// when it is not one of these (an unreachable host, a bad answer).
    /// </summary>
    /// <param name="failure">What reading the service threw.</param>
    /// <param name="identitySet">Whether an identity was set to present or sign with.</param>
    public static string? Classify(Exception failure, bool identitySet)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure is SecomIdentityRequiredException)
            return NeedsSignedRequest;
        if (failure is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            return identitySet ? CertificateRefused : NeedsCertificate;
        for (var inner = failure; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
                return ServerUntrusted;
        }

        return null;
    }
}
