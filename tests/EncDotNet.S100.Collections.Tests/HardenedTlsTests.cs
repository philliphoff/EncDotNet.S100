using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Net;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Distrusted CA keys and soft-fail revocation layered over system trust
/// (eMudhra intermediate compromise, October 2026; Mozilla bug 2083449).
/// </summary>
public sealed class HardenedTlsTests
{
    /// <summary>
    /// The real "EM OV TLS CA - G3C" intermediate (issued by emSign Root TLS CA - G3),
    /// from Mozilla's impacted-intermediates.json. Its key is public knowledge.
    /// </summary>
    internal const string CompromisedIntermediatePem = """
        -----BEGIN CERTIFICATE-----
        MIIDaDCCAu6gAwIBAgIPKWkmwiejoVJjkrGUgqUEMAoGCCqGSM49BAMDMFYxCzAJ
        BgNVBAYTAklOMSUwIwYDVQQKDBxlTXVkaHJhIFRlY2hub2xvZ2llcyBMaW1pdGVk
        MSAwHgYDVQQDDBdlbVNpZ24gUm9vdCBUTFMgQ0EgLSBHMzAeFw0yNTA0MzAxNTM3
        NDFaFw00MDA0MjgxODMwMDBaMEAxCzAJBgNVBAYTAlVTMRQwEgYDVQQKDAtlTXVk
        aHJhIEluYzEbMBkGA1UEAwwSRU0gT1YgVExTIENBIC0gRzNDMHYwEAYHKoZIzj0C
        AQYFK4EEACIDYgAEE4JRzVKskpjByKrZdzId65fnCb0LTKCspV3IrVHc/J0ViaFZ
        fjpRIOHv1jHGPhg1ysrimGmmLhYx6KKBgatWYW3EXZGKvAnzqw5jz3kqpNztc4e+
        N7ulaVSfHAKycO1no4IBljCCAZIwHwYDVR0jBBgwFoAU4TqrIXzx401UrtRyt/2L
        WUIgkFQwHQYDVR0OBBYEFDTq6gzryQra+dDF4u2beMmHnhtsMB0GA1UdJQQWMBQG
        CCsGAQUFBwMBBggrBgEFBQcDAjBdBgNVHSAEVjBUMDkGCysGAQQBg44hAQABMCow
        KAYIKwYBBQUHAgEWHGh0dHA6Ly9yZXBvc2l0b3J5LmVtc2lnbi5jb20wDQYLKwYB
        BAGDjiEBAm4wCAYGZ4EMAQICMHUGCCsGAQUFBwEBBGkwZzAkBggrBgEFBQcwAYYY
        aHR0cDovL29jc3AtYy5lbXNpZ24uY29tMD8GCCsGAQUFBzAChjNodHRwOi8vcmVw
        b3NpdG9yeS5lbXNpZ24uY29tL2NlcnRzL0VNT1ZUTFNDQUczQy5wN2MwNwYDVR0f
        BDAwLjAsoCqgKIYmaHR0cDovL2NybC5lbXNpZ24uY29tLz9yb290dGxzY2FnMy5j
        cmwwDgYDVR0PAQH/BAQDAgEGMBIGA1UdEwEB/wQIMAYBAf8CAQAwCgYIKoZIzj0E
        AwMDaAAwZQIxAMSZt2xsPeumRdeSMSQ3CbzIZeQ/q7GDCWfeE1Pq+mhgA6gRM44c
        NVF3xJ+wsxJk+wIwFJbaDMokD8Ei6eNOiA8yUMYvfzQyngmrhZQMdPCK1SewrR79
        4rSb9vGqxZzelQth
        -----END CERTIFICATE-----
        """;

    private static X509Certificate2 Compromised() => X509Certificate2.CreateFromPem(CompromisedIntermediatePem);

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public void The_list_holds_the_eight_emudhra_keys_as_lower_case_sha256_hex()
    {
        Assert.Equal(8, DistrustedKeys.SpkiSha256.Count);
        Assert.All(DistrustedKeys.SpkiSha256, h => Assert.Matches("^[0-9a-f]{64}$", h));
    }

    [Fact]
    public void A_compromised_intermediate_is_distrusted_by_its_key()
    {
        using var certificate = Compromised();

        Assert.Equal("63ecefee431d49523bc1136c4e72d0206a477086caf606cd5cf025686c109cf8", DistrustedKeys.SpkiSha256Of(certificate));
        Assert.True(DistrustedKeys.IsDistrusted(certificate));
        Assert.Same(certificate, DistrustedKeys.FindDistrusted([null, certificate]));
    }

    [Fact]
    public void Another_key_is_not_distrusted()
    {
        using var certificate = SelfSigned();

        Assert.False(DistrustedKeys.IsDistrusted(certificate));
        Assert.Null(DistrustedKeys.FindDistrusted(certificate, chain: null));
    }

    [Fact]
    public void Validate_keeps_the_platforms_refusal()
    {
        using var certificate = SelfSigned();
        var noRevocation = new HardenedTlsOptions { CheckRevocation = false };

        Assert.True(HardenedTls.Validate(certificate, null, SslPolicyErrors.None, noRevocation));
        Assert.False(HardenedTls.Validate(certificate, null, SslPolicyErrors.RemoteCertificateChainErrors, noRevocation));
        Assert.False(HardenedTls.Validate(certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch, noRevocation));
        Assert.False(HardenedTls.Validate(null, null, SslPolicyErrors.None, noRevocation));
    }

    [Fact]
    public void Validate_refuses_a_distrusted_key_the_platform_accepted()
    {
        using var certificate = Compromised();

        Assert.False(HardenedTls.Validate(certificate, null, SslPolicyErrors.None, new HardenedTlsOptions { CheckRevocation = false }));
    }

    [Fact]
    public void The_revocation_check_reports_a_distrusted_key_as_revoked()
    {
        using var certificate = Compromised();

        Assert.Equal(TlsRevocationStatus.Revoked, HardenedTls.CheckRevocation(certificate, [], TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void A_chain_without_revocation_data_soft_fails()
    {
        using var certificate = SelfSigned();

        Assert.NotEqual(TlsRevocationStatus.Revoked, HardenedTls.CheckRevocation(certificate, [], TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void Secom_refuses_a_distrusted_key_even_when_the_system_trusts_it()
    {
        using var certificate = Compromised();
        var trust = new SecomServerTrust(SecomTrustAnchors.None) { SystemTls = new HardenedTlsOptions { CheckRevocation = false } };

        var result = trust.Validate("example.test", certificate, null, SslPolicyErrors.None);

        Assert.Equal(SecomServerTrustOutcome.Revoked, result.Outcome);
        Assert.Equal(SecomRevocationStatus.Revoked, result.Revocation);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Secom_still_allows_a_system_trusted_certificate()
    {
        using var certificate = SelfSigned();
        var trust = new SecomServerTrust(SecomTrustAnchors.None) { SystemTls = new HardenedTlsOptions { CheckRevocation = false } };

        var result = trust.Validate("localhost", certificate, null, SslPolicyErrors.None);

        Assert.Equal(SecomServerTrustOutcome.SystemTrusted, result.Outcome);
        Assert.True(result.Allowed);
    }
}
