using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class PermitSignatureTests
{
    [Fact]
    public async Task AuthenticateAsync_ValidSignature_ReturnsAuthenticatedPermit()
    {
        using var fixture = new SignatureFixture();

        var result = await fixture.AuthenticateAsync();

        Assert.True(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.Ok, result.Verification.Outcome);
        Assert.NotNull(result.PermitFile);
        Assert.True(result.PermitFile.IsAuthenticated);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingSignature_ReturnsNotSigned()
    {
        using var fixture = new SignatureFixture();
        using var permitStream = new MemoryStream(fixture.PermitBytes);

        var result = await PermitSignatureVerifier.AuthenticateAsync(
            permitStream,
            signatureContent: null,
            "PERMIT.XML",
            fixture.TrustAnchors);

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.PermitFile);
        Assert.Equal(VerificationOutcome.NotSigned, result.Verification.Outcome);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedPermit_ReturnsInvalidSignature()
    {
        using var fixture = new SignatureFixture();
        var tampered = fixture.PermitBytes.ToArray();
        tampered[^2] ^= 0x01;

        var result = await fixture.AuthenticateAsync(tampered);

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.PermitFile);
        Assert.Equal(VerificationOutcome.SignatureInvalid, result.Verification.Outcome);
    }

    [Fact]
    public async Task AuthenticateAsync_UntrustedCertificate_ReturnsUntrusted()
    {
        using var fixture = new SignatureFixture();
        using var permitStream = new MemoryStream(fixture.PermitBytes);
        using var signatureStream = new MemoryStream(fixture.SignatureBytes);

        var result = await PermitSignatureVerifier.AuthenticateAsync(
            permitStream,
            signatureStream,
            "PERMIT.XML",
            new TrustAnchorOptions());

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.CertificateUntrusted, result.Verification.Outcome);
    }

    [Fact]
    public async Task AuthenticateAsync_UntrustedOverride_DoesNotAuthenticatePermit()
    {
        using var fixture = new SignatureFixture();
        using var permitStream = new MemoryStream(fixture.PermitBytes);
        using var signatureStream = new MemoryStream(fixture.SignatureBytes);

        var result = await PermitSignatureVerifier.AuthenticateAsync(
            permitStream,
            signatureStream,
            "PERMIT.XML",
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.CertificateUntrusted, result.Verification.Outcome);
    }

    [Fact]
    public async Task AuthenticateAsync_P256Certificate_ReturnsError()
    {
        using var fixture = new SignatureFixture(useP256: true);

        var result = await fixture.AuthenticateAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.Error, result.Verification.Outcome);
        Assert.Contains("P-384", result.Verification.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsync_P1363Signature_ReturnsInvalid()
    {
        using var fixture = new SignatureFixture(useP1363: true);

        var result = await fixture.AuthenticateAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.SignatureInvalid, result.Verification.Outcome);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingPermitExpiry_ReturnsError()
    {
        var permitBytes = SignatureFixture.CreatePermitBytes()
            .AsSpan()
            .ToArray();
        var permitText = Encoding.UTF8.GetString(permitBytes)
            .Replace(
                "<expiry>2026-12-31</expiry>",
                string.Empty,
                StringComparison.Ordinal);
        using var fixture = new SignatureFixture(
            permitBytes: Encoding.UTF8.GetBytes(permitText));

        var result = await fixture.AuthenticateAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.PermitFile);
        Assert.Equal(VerificationOutcome.Error, result.Verification.Outcome);
        Assert.Contains("expiry", result.Verification.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_OutOfRangeNegativeXmlSchemaOffset_ReturnsError()
    {
        var permitText = Encoding.UTF8.GetString(SignatureFixture.CreatePermitBytes())
            .Replace(
                "2026-12-31",
                "2026-12-31-15:00",
                StringComparison.Ordinal);
        using var fixture = new SignatureFixture(
            permitBytes: Encoding.UTF8.GetBytes(permitText));

        var result = await fixture.AuthenticateAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.Error, result.Verification.Outcome);
        Assert.Contains("expiry", result.Verification.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_XmlSchemaExpiryWithOffset_IsAccepted()
    {
        var permitText = Encoding.UTF8.GetString(SignatureFixture.CreatePermitBytes())
            .Replace(
                "2026-12-31",
                "2026-12-31+05:30",
                StringComparison.Ordinal);
        using var fixture = new SignatureFixture(
            permitBytes: Encoding.UTF8.GetBytes(permitText));

        var result = await fixture.AuthenticateAsync();

        Assert.True(result.IsAuthenticated, result.Verification.Detail);
    }

    [Fact]
    public async Task AuthenticateAsync_MalformedSignature_ReturnsError()
    {
        using var fixture = new SignatureFixture();
        using var permitStream = new MemoryStream(fixture.PermitBytes);
        using var signatureStream = new MemoryStream("<invalid"u8.ToArray());

        var result = await PermitSignatureVerifier.AuthenticateAsync(
            permitStream,
            signatureStream,
            "PERMIT.XML",
            fixture.TrustAnchors);

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.Error, result.Verification.Outcome);
        Assert.Contains("malformed", result.Verification.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_DifferentFileName_ReturnsError()
    {
        using var fixture = new SignatureFixture(signedFileName: "OTHER.XML");

        var result = await fixture.AuthenticateAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal(VerificationOutcome.Error, result.Verification.Outcome);
        Assert.Contains("not 'PERMIT.XML'", result.Verification.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PermitKeyProvider_UnauthenticatedPermit_Throws()
    {
        using var permitStream = new MemoryStream(SignatureFixture.CreatePermitBytes());
        var permit = PermitFile.Read(permitStream);
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
        };

        var exception = Assert.Throws<ArgumentException>(() => new PermitKeyProvider(
            permit,
            HardwareId.Parse("40384B45B54596201114FE9904220101"),
            catalogue));

        Assert.Contains("PERMIT.SIGN", exception.Message, StringComparison.Ordinal);
    }

    private sealed class SignatureFixture : IDisposable
    {
        private readonly ECDsa _ecdsa;
        private readonly X509Certificate2 _certificate;

        public SignatureFixture(
            string signedFileName = "PERMIT.XML",
            byte[]? permitBytes = null,
            bool useP256 = false,
            bool useP1363 = false)
        {
            _ecdsa = ECDsa.Create(
                useP256
                    ? ECCurve.NamedCurves.nistP256
                    : ECCurve.NamedCurves.nistP384);
            var request = new CertificateRequest(
                "CN=Test Data Server",
                _ecdsa,
                HashAlgorithmName.SHA384);
            _certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            PermitBytes = permitBytes ?? CreatePermitBytes();
            var signature = _ecdsa.SignHash(
                SHA384.HashData(PermitBytes),
                useP1363
                    ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                    : DSASignatureFormat.Rfc3279DerSequence);
            var certificateId = "urn:test:data-server";
            var xml = $"""
                <StandaloneDigitalSignature xmlns="http://www.iho.int/s100/se/5.1">
                    <filename>{signedFileName}</filename>
                    <certificates>
                        <schemeAdministrator id="TEST"/>
                        <certificate id="{certificateId}" issuer="TEST">{Convert.ToBase64String(_certificate.RawData)}</certificate>
                    </certificates>
                    <digitalSignature id="permit-signature" certificateRef="{certificateId}">{Convert.ToBase64String(signature)}</digitalSignature>
                </StandaloneDigitalSignature>
                """;
            SignatureBytes = Encoding.UTF8.GetBytes(xml);
            TrustAnchors = new TrustAnchorOptions { TrustedRoots = [_certificate] };
        }

        public byte[] PermitBytes { get; }

        public byte[] SignatureBytes { get; }

        public TrustAnchorOptions TrustAnchors { get; }

        public async Task<PermitAuthenticationResult> AuthenticateAsync(byte[]? permitBytes = null)
        {
            using var permitStream = new MemoryStream(permitBytes ?? PermitBytes);
            using var signatureStream = new MemoryStream(SignatureBytes);
            return await PermitSignatureVerifier.AuthenticateAsync(
                permitStream,
                signatureStream,
                "PERMIT.XML",
                TrustAnchors);
        }

        public static byte[] CreatePermitBytes() =>
            """
            <Permit xmlns="http://www.iho.int/s100/se/5.1">
                <header>
                    <issueDate>2026-01-01</issueDate>
                    <dataServerName>Test</dataServerName>
                    <dataServerIdentifier>TS</dataServerIdentifier>
                    <version>1.0.0</version>
                    <userpermit>AD1DAD797C966EC9F6A55B66ED98281599B3C7B1859868</userpermit>
                </header>
                <products>
                    <product id="S-101">
                        <datasetPermit>
                            <filename>101AA00000000001</filename>
                            <editionNumber>1</editionNumber>
                            <expiry>2026-12-31</expiry>
                            <encryptedKey>2E16E07E451FF1854156634DA3DD3FB8</encryptedKey>
                        </datasetPermit>
                    </product>
                </products>
            </Permit>
            """u8.ToArray();

        public void Dispose()
        {
            _certificate.Dispose();
            _ecdsa.Dispose();
        }
    }
}
