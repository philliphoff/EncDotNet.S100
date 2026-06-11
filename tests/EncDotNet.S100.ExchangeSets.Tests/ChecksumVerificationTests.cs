using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Tests for the checksum/integrity dimension of
/// <see cref="ExchangeSetVerifier"/>, exercised against synthetic catalogues
/// so an unsigned exchange set can be integrity-checked
/// (S-100 Edition 5.2.1 Part 15 §15-8.10).
/// </summary>
public class ChecksumVerificationTests
{
    private static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string HashMrn(byte[] content) =>
        $"urn:mrn:iho:s100:hash:sha256:{Sha256Hex(content)}";

    [Fact]
    public async Task Unsigned_NoDeclaredHash_ReportsNoChecksum_WithComputedDigest()
    {
        var content = "hello"u8.ToArray();

        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata { FileName = "test.000" },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.NotSigned, file.Outcome);
        Assert.Equal(VerificationOutcome.NoChecksum, file.ChecksumOutcome);
        Assert.Equal(Sha256Hex(content), file.ComputedSha256);
        Assert.True(result.IntegrityVerified);
        Assert.False(result.HasChecksumMismatches);
        Assert.False(result.HasMissingFiles);
    }

    [Fact]
    public async Task Unsigned_MatchingDeclaredHash_ReportsChecksumOk()
    {
        var content = "integrity"u8.ToArray();

        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata
                {
                    FileName = "test.000",
                    ExpectedHash = Parse(HashMrn(content)),
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.Ok, file.ChecksumOutcome);
        Assert.True(result.IntegrityVerified);
    }

    [Fact]
    public async Task Unsigned_DeclaredHashMismatch_ReportsChecksumMismatch()
    {
        var content = "actual content"u8.ToArray();

        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata
                {
                    FileName = "test.000",
                    ExpectedHash = Parse(HashMrn("different content"u8.ToArray())),
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.ChecksumMismatch, file.ChecksumOutcome);
        Assert.Equal(Sha256Hex(content), file.ComputedSha256);
        Assert.True(result.HasChecksumMismatches);
        Assert.False(result.IntegrityVerified);
    }

    [Fact]
    public async Task MissingFile_ReportsFileMissing_InBothDimensions()
    {
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata { FileName = "missing.000" },
            ],
        };

        var result = await new ExchangeSetVerifier().VerifyAsync(
            new InMemoryAssetSource(), catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.FileMissing, file.Outcome);
        Assert.Equal(VerificationOutcome.FileMissing, file.ChecksumOutcome);
        Assert.Null(file.ComputedSha256);
        Assert.True(result.HasMissingFiles);
        Assert.False(result.IntegrityVerified);
    }

    [Fact]
    public async Task SignedUnencryptedFile_HasIndependentSignatureAndChecksumOutcomes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=TestSA", ecdsa, HashAlgorithmName.SHA256);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var content = "signed content"u8.ToArray();
        var signature = ecdsa.SignHash(SHA256.HashData(content));
        const string certId = "urn:test:cert1";

        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            Certificates = new CertificateBlock
            {
                Certificates = [new CertificateEntry { Id = certId, Issuer = "TestSA", Value = cert.RawData }],
            },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata
                {
                    FileName = "test.000",
                    DigitalSignatureReference = "ECDSA",
                    DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.ECDSA,
                    DigitalSignatureValue = new DigitalSignatureValue
                    {
                        Id = "SIG1",
                        CertificateRef = certId,
                        Value = signature,
                    },
                    // No declared hash: signature is valid, checksum dimension
                    // has nothing to compare against.
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.Ok, file.Outcome);
        Assert.Equal(VerificationOutcome.NoChecksum, file.ChecksumOutcome);
        Assert.Equal(Sha256Hex(content), file.ComputedSha256);
        Assert.False(result.IsUnsigned);
        Assert.True(result.IntegrityVerified);
    }

    [Fact]
    public async Task Unsigned_IntactSet_AllValidIsFalse_ButIntegrityVerifiedIsTrue()
    {
        // Documents the deliberate decision (aligned with the S-57 sibling):
        // a missing checksum is NOT a failure. AllValid is a strict
        // signature-only predicate (false when unsigned); IntegrityVerified is
        // the integrity verdict and treats NoChecksum as non-failing.
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata { FileName = "a.000" },
                new DatasetDiscoveryMetadata { FileName = "b.000" },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("a.000", "alpha"u8.ToArray());
        source.AddFile("b.000", "bravo"u8.ToArray());

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.All(result.FileResults, f => Assert.Equal(VerificationOutcome.NoChecksum, f.ChecksumOutcome));
        Assert.True(result.IsUnsigned);
        Assert.False(result.AllValid);
        Assert.True(result.IntegrityVerified);
    }

    private static CryptographicHash Parse(string mrn)
    {
        Assert.True(CryptographicHash.TryParse(mrn, out var hash));
        return hash;
    }
}
