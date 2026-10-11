using System.Globalization;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels.Secom;

/// <summary>How a SECOM service reads with a source's identity: the as-built vocabulary (#880, #883).</summary>
internal enum SecomConnectionState
{
    /// <summary>It doesn't ask for a certificate.</summary>
    Readable,

    /// <summary>It accepted the identity presented (or the requests it signed).</summary>
    ReadableWithCertificate,

    /// <summary>It shares data only with a client certificate, and none was presented.</summary>
    NeedsCertificate,

    /// <summary>It answers only SECOM 2.0 requests signed with an MCP identity, and none signed them.</summary>
    NeedsSignedRequest,

    /// <summary>It refused the identity presented.</summary>
    CertificateRefused,

    /// <summary>It rejected the requests the identity signed.</summary>
    SignedRequestRefused,

    /// <summary>Its server certificate isn't trusted, so no identity was presented.</summary>
    ServerUntrusted,

    /// <summary>It couldn't be reached, or didn't answer as a SECOM service.</summary>
    NotReachable,
}

/// <summary>The tone of a state, tag or status block.</summary>
internal enum SecomTone
{
    Neutral,
    Success,
    Warning,
    Destructive,
}

/// <summary>Words, tones and the mappings into <see cref="SecomConnectionState"/>.</summary>
internal static class SecomConnection
{
    /// <summary>The state a registry probe found (with the identity it probed with, if any).</summary>
    public static SecomConnectionState FromProbe(SecomProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Reachability switch
        {
            SecomReachability.Open => SecomConnectionState.Readable,
            SecomReachability.OpenWithCertificate => SecomConnectionState.ReadableWithCertificate,
            SecomReachability.NeedsCertificate => SecomConnectionState.NeedsCertificate,
            SecomReachability.CertificateRefused => result.Signed ? SecomConnectionState.SignedRequestRefused : SecomConnectionState.CertificateRefused,
            // Probed with an identity that signed, yet still not readable: the signed request wasn't accepted.
            SecomReachability.NeedsSecom2Search => result.Identity is null ? SecomConnectionState.NeedsSignedRequest : SecomConnectionState.SignedRequestRefused,
            SecomReachability.UntrustedServer => SecomConnectionState.ServerUntrusted,
            _ => SecomConnectionState.NotReachable,
        };
    }

    /// <summary>
    /// The state a source's last read found, from its presentation record;
    /// <see langword="null"/> before it was read.
    /// </summary>
    public static SecomConnectionState? FromPresentation(SecomPresentation? presentation)
    {
        if (presentation is null)
            return null;
        return presentation.Code switch
        {
            SecomDiagnosticCodes.NeedsCertificate => SecomConnectionState.NeedsCertificate,
            SecomDiagnosticCodes.NeedsSignedRequest => SecomConnectionState.NeedsSignedRequest,
            SecomDiagnosticCodes.CertificateRefused => SecomConnectionState.CertificateRefused,
            SecomDiagnosticCodes.SignedRequestRefused => SecomConnectionState.SignedRequestRefused,
            SecomDiagnosticCodes.ServerUntrusted => SecomConnectionState.ServerUntrusted,
            _ => presentation.Outcome switch
            {
                SecomPresentationOutcome.Accepted => SecomConnectionState.ReadableWithCertificate,
                SecomPresentationOutcome.NotReached => SecomConnectionState.NotReachable,
                _ => SecomConnectionState.Readable,
            },
        };
    }

    /// <summary>The vocabulary word: "Readable", "Needs a certificate", …</summary>
    public static string Word(SecomConnectionState state) => state switch
    {
        SecomConnectionState.Readable => Strings.Secom_State_Readable,
        SecomConnectionState.ReadableWithCertificate => Strings.Library_Reachability_OpenWithCertificate,
        SecomConnectionState.NeedsCertificate => Strings.Library_Reachability_NeedsCertificate,
        SecomConnectionState.NeedsSignedRequest => Strings.Library_Reachability_NeedsSecom2Search,
        SecomConnectionState.CertificateRefused => Strings.Library_Reachability_CertificateRefused,
        SecomConnectionState.SignedRequestRefused => Strings.Secom_State_SignedRequestRefused,
        SecomConnectionState.ServerUntrusted => Strings.Library_Reachability_UntrustedServer,
        _ => Strings.Library_Reachability_Unreachable,
    };

    /// <summary>Success for readable; warning for a missing certificate or signature and an untrusted server; destructive for a refusal; neutral otherwise.</summary>
    public static SecomTone Tone(SecomConnectionState state) => state switch
    {
        SecomConnectionState.Readable or SecomConnectionState.ReadableWithCertificate => SecomTone.Success,
        SecomConnectionState.NeedsCertificate or SecomConnectionState.NeedsSignedRequest or SecomConnectionState.ServerUntrusted => SecomTone.Warning,
        SecomConnectionState.CertificateRefused or SecomConnectionState.SignedRequestRefused => SecomTone.Destructive,
        _ => SecomTone.Neutral,
    };

    /// <summary>True when the service can be read as things stand.</summary>
    public static bool IsReadable(SecomConnectionState state) =>
        state is SecomConnectionState.Readable or SecomConnectionState.ReadableWithCertificate;

    /// <summary>True when the service asks for a certificate or a signed request: the picker then shows hints.</summary>
    public static bool AsksForIdentity(SecomConnectionState state) =>
        state is SecomConnectionState.NeedsCertificate or SecomConnectionState.NeedsSignedRequest
            or SecomConnectionState.CertificateRefused or SecomConnectionState.SignedRequestRefused;

    /// <summary>Why a server isn't trusted, for the "Reason:" line; <see langword="null"/> when trusted or unknown.</summary>
    public static string? UntrustedReason(SecomServerTrustOutcome? outcome, string? anchor) => outcome switch
    {
        SecomServerTrustOutcome.NotTrusted => Strings.Secom_Reason_NotTrusted,
        SecomServerTrustOutcome.WrongHost => Strings.Library_Reachability_WrongHostExplanation,
        SecomServerTrustOutcome.Expired => string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_ExpiredServerExplanationFormat, anchor ?? "?"),
        SecomServerTrustOutcome.Revoked => string.Format(CultureInfo.CurrentCulture, Strings.Library_Reachability_RevokedServerExplanationFormat, anchor ?? "?"),
        _ => null,
    };

    /// <summary>True when trusting an authority would fix an untrusted server (its chain reaches none); not for an expired, misnamed or revoked one.</summary>
    public static bool TrustCanFix(SecomServerTrustOutcome? outcome) => outcome is null or SecomServerTrustOutcome.NotTrusted;

    /// <summary>A provider's short name as the copy uses it ("KHRA"): upper case when it was given in lower case (an MRN part).</summary>
    public static string ProviderName(string provider) =>
        provider.Any(char.IsUpper) ? provider : provider.ToUpperInvariant();
}
