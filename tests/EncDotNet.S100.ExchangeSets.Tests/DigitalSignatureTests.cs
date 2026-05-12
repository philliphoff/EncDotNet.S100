using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class DigitalSignatureTests
{
    private static string GetCatalogPath([CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", "S101", "CATALOG.XML");
    }

    private static ExchangeCatalogue ReadTestCatalogue()
    {
        return ExchangeCatalogueReader.Read(GetCatalogPath());
    }

    // ── Model parsing tests ──────────────────────────────────────────

    [Fact]
    public void Certificates_AreParsed()
    {
        var catalogue = ReadTestCatalogue();

        Assert.NotNull(catalogue.Certificates);
        Assert.Equal("urn:mrn:iho:s62:iic:2C:key1", catalogue.Certificates.SchemeAdministratorId);
        Assert.Single(catalogue.Certificates.Certificates);
    }

    [Fact]
    public void Certificate_HasExpectedFields()
    {
        var catalogue = ReadTestCatalogue();
        var cert = catalogue.Certificates!.Certificates[0];

        Assert.Equal("urn:mrn:iho:s62:iic:2C:key1", cert.Id);
        Assert.Equal("IHO Scretariat", cert.Issuer);
        Assert.NotEmpty(cert.Value);
    }

    [Fact]
    public void Certificate_IsValidX509()
    {
        var catalogue = ReadTestCatalogue();
        var certEntry = catalogue.Certificates!.Certificates[0];

        // Should parse as a valid X.509 certificate
#if NET10_0_OR_GREATER
        using var cert = X509CertificateLoader.LoadCertificate(certEntry.Value);
#else
        using var cert = new X509Certificate2(certEntry.Value);
#endif
        Assert.NotNull(cert.Subject);
        Assert.NotNull(cert.Issuer);
    }

    [Fact]
    public void FirstDataset_HasDigitalSignatureValue()
    {
        var catalogue = ReadTestCatalogue();
        var dataset = catalogue.DatasetDiscoveryMetadata[0];

        Assert.NotNull(dataset.DigitalSignatureValue);
        Assert.Equal("SIG101AA0000DS0009", dataset.DigitalSignatureValue.Id);
        Assert.Equal("urn:mrn:iho:s62:iic:2C:key1", dataset.DigitalSignatureValue.CertificateRef);
        Assert.NotEmpty(dataset.DigitalSignatureValue.Value);
    }

    [Fact]
    public void FirstDataset_HasDSAAlgorithm()
    {
        var catalogue = ReadTestCatalogue();
        var dataset = catalogue.DatasetDiscoveryMetadata[0];

        Assert.Equal(DigitalSignatureAlgorithm.DSA, dataset.DigitalSignatureAlgorithm);
    }

    [Fact]
    public void AllDatasets_HaveSignatures()
    {
        var catalogue = ReadTestCatalogue();

        Assert.All(catalogue.DatasetDiscoveryMetadata, dataset =>
        {
            Assert.NotNull(dataset.DigitalSignatureValue);
            Assert.NotEmpty(dataset.DigitalSignatureValue.Id);
            Assert.NotEmpty(dataset.DigitalSignatureValue.CertificateRef);
            Assert.NotEmpty(dataset.DigitalSignatureValue.Value);
            Assert.Equal(DigitalSignatureAlgorithm.DSA, dataset.DigitalSignatureAlgorithm);
        });
    }

    // ── Verification tests (synthetic) ───────────────────────────────

    [Fact]
    public async Task VerifyAsync_UnsignedExchangeSet_ReturnsNotSigned()
    {
        // Create a catalogue with no signatures
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata
                {
                    FileName = "test.000",
                    DigitalSignatureReference = null,
                    DigitalSignatureValue = null,
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", "hello"u8.ToArray());

        var verifier = new ExchangeSetVerifier();
        var result = await verifier.VerifyAsync(source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.True(result.IsUnsigned);
        Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.NotSigned, result.FileResults[0].Outcome);
    }

    [Fact]
    public async Task VerifyAsync_ValidEcdsaSignature_ReturnsOk()
    {
        // Generate a test ECDSA P-256 key pair and self-signed certificate
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=TestSA", ecdsa, HashAlgorithmName.SHA256);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        // Create test file content and sign it
        var fileContent = "test file content"u8.ToArray();
        var hash = SHA256.HashData(fileContent);
        var signature = ecdsa.SignHash(hash);

        var certBytes = cert.RawData;
        var certId = "urn:test:cert1";

        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            Certificates = new CertificateBlock
            {
                SchemeAdministratorId = certId,
                Certificates = [new CertificateEntry { Id = certId, Issuer = "TestSA", Value = certBytes }],
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
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", fileContent);

        var verifier = new ExchangeSetVerifier();
        var trustAnchors = new TrustAnchorOptions
        {
            TrustedRoots = [cert],
            AllowUntrustedCertificates = false,
        };

        var result = await verifier.VerifyAsync(source, catalogue, trustAnchors);

        Assert.True(result.AllValid);
        Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.Ok, result.FileResults[0].Outcome);
    }

    [Fact]
    public async Task VerifyAsync_TamperedFile_ReturnsSignatureInvalid()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=TestSA", ecdsa, HashAlgorithmName.SHA256);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        // Sign original content
        var originalContent = "original content"u8.ToArray();
        var hash = SHA256.HashData(originalContent);
        var signature = ecdsa.SignHash(hash);

        var certId = "urn:test:cert1";

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
                },
            ],
        };

        // Tamper: serve different content
        var source = new InMemoryAssetSource();
        source.AddFile("test.000", "tampered content"u8.ToArray());

        var verifier = new ExchangeSetVerifier();
        var result = await verifier.VerifyAsync(source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.True(result.HasInvalidSignatures);
        Assert.Equal(VerificationOutcome.SignatureInvalid, result.FileResults[0].Outcome);
    }

    [Fact]
    public async Task VerifyAsync_UntrustedCertificate_ReturnsUntrusted()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=UntrustedSA", ecdsa, HashAlgorithmName.SHA256);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var fileContent = "test"u8.ToArray();
        var hash = SHA256.HashData(fileContent);
        var signature = ecdsa.SignHash(hash);

        var certId = "urn:test:untrusted";

        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            Certificates = new CertificateBlock
            {
                Certificates = [new CertificateEntry { Id = certId, Issuer = "UntrustedSA", Value = cert.RawData }],
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
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", fileContent);

        // Verify with a different trust root
        using var otherEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherReq = new CertificateRequest("CN=OtherSA", otherEcdsa, HashAlgorithmName.SHA256);
        using var otherCert = otherReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var verifier = new ExchangeSetVerifier();
        var result = await verifier.VerifyAsync(source, catalogue, new TrustAnchorOptions
        {
            TrustedRoots = [otherCert],
        });

        Assert.Equal(VerificationOutcome.CertificateUntrusted, result.FileResults[0].Outcome);
    }

    [Fact]
    public async Task VerifyAsync_MissingFile_ReturnsFileMissing()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=TestSA", ecdsa, HashAlgorithmName.SHA256);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var certId = "urn:test:cert1";

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
                    FileName = "missing.000",
                    DigitalSignatureReference = "ECDSA",
                    DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.ECDSA,
                    DigitalSignatureValue = new DigitalSignatureValue
                    {
                        Id = "SIG1",
                        CertificateRef = certId,
                        Value = [1, 2, 3],
                    },
                },
            ],
        };

        // Empty source — file doesn't exist
        var source = new InMemoryAssetSource();

        var verifier = new ExchangeSetVerifier();
        var result = await verifier.VerifyAsync(source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(VerificationOutcome.FileMissing, result.FileResults[0].Outcome);
    }

    [Fact]
    public async Task VerifyAsync_CertificateNotInCatalogue_ReturnsCertificateNotFound()
    {
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2024-01-01" },
            Certificates = new CertificateBlock { Certificates = [] },
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
                        CertificateRef = "urn:test:nonexistent",
                        Value = [1, 2, 3],
                    },
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("test.000", "content"u8.ToArray());

        var verifier = new ExchangeSetVerifier();
        var result = await verifier.VerifyAsync(source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(VerificationOutcome.CertificateNotFound, result.FileResults[0].Outcome);
    }

    [Fact]
    public async Task VerifyAsync_MultipleFiles_MixedResults()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=TestSA", ecdsa, HashAlgorithmName.SHA256);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var file1Content = "file1"u8.ToArray();
        var file1Hash = SHA256.HashData(file1Content);
        var file1Sig = ecdsa.SignHash(file1Hash);

        var certId = "urn:test:cert1";

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
                    FileName = "file1.000",
                    DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.ECDSA,
                    DigitalSignatureValue = new DigitalSignatureValue { Id = "SIG1", CertificateRef = certId, Value = file1Sig },
                },
                new DatasetDiscoveryMetadata
                {
                    FileName = "file2.000",
                    // Not signed
                },
            ],
        };

        var source = new InMemoryAssetSource();
        source.AddFile("file1.000", file1Content);
        source.AddFile("file2.000", "file2"u8.ToArray());

        var verifier = new ExchangeSetVerifier();
        var result = await verifier.VerifyAsync(source, catalogue, new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(2, result.FileResults.Count);
        Assert.Equal(VerificationOutcome.Ok, result.FileResults[0].Outcome);
        Assert.Equal(VerificationOutcome.NotSigned, result.FileResults[1].Outcome);
        Assert.False(result.AllValid);
        Assert.False(result.IsUnsigned);
    }

    // ── In-memory asset source for testing ───────────────────────────

    private sealed class InMemoryAssetSource : IAssetSource
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path, byte[] content)
        {
            _files[path] = content;
        }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            if (_files.TryGetValue(relativePath, out var content))
            {
                return Task.FromResult<Stream>(new MemoryStream(content));
            }

            throw new FileNotFoundException($"File not found: {relativePath}", relativePath);
        }

        public void Dispose() { }
    }
}
