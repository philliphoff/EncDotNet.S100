using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class Part15DigitalSignatureTests
{
    [Fact]
    public void Read_ParsesAllSignatureFormsInOrder()
    {
        var catalogue = ReadCatalogue(
            """
            <S100XC:digitalSignatureValue>
              <S100SE:S100_SE_DigitalSignature id="legacy" certificateRef="cert">AQ==</S100SE:S100_SE_DigitalSignature>
            </S100XC:digitalSignatureValue>
            <S100XC:digitalSignatureValue>
              <S100SE:S100_SE_SignatureOnData id="data" certificateRef="cert" dataStatus="compressed">Ag==</S100SE:S100_SE_SignatureOnData>
            </S100XC:digitalSignatureValue>
            <S100XC:digitalSignatureValue>
              <S100SE:S100_SE_SignatureOnSignature id="chain" certificateRef="cert" signatureRef="data">Aw==</S100SE:S100_SE_SignatureOnSignature>
            </S100XC:digitalSignatureValue>
            """);

        var metadata = Assert.Single(catalogue.DatasetDiscoveryMetadata);
        Assert.Equal(3, metadata.DigitalSignatures.Count);
        Assert.Equal(DigitalSignatureAlgorithm.ECDSA384SHA2, metadata.DigitalSignatureAlgorithm);
        Assert.Single(catalogue.Certificates?.Certificates ?? []);
        Assert.Same(metadata.DigitalSignatures[0], metadata.DigitalSignatureValue);
        Assert.Equal(DigitalSignatureKind.Legacy, metadata.DigitalSignatures[0].Kind);
        Assert.Equal(DigitalSignatureKind.SignatureOnData, metadata.DigitalSignatures[1].Kind);
        Assert.Equal(SignatureDataStatus.Compressed, metadata.DigitalSignatures[1].DataStatus);
        Assert.Equal(DigitalSignatureKind.SignatureOnSignature, metadata.DigitalSignatures[2].Kind);
        Assert.Equal("data", metadata.DigitalSignatures[2].SignatureRef);
    }

    [Theory]
    [InlineData("""<S100SE:S100_SE_SignatureOnData id="s" certificateRef="cert">AQ==</S100SE:S100_SE_SignatureOnData>""")]
    [InlineData("""<S100SE:S100_SE_SignatureOnData id="s" certificateRef="cert" dataStatus="unknown">AQ==</S100SE:S100_SE_SignatureOnData>""")]
    [InlineData("""<S100SE:S100_SE_SignatureOnSignature id="s" certificateRef="cert">AQ==</S100SE:S100_SE_SignatureOnSignature>""")]
    [InlineData("""<S100SE:S100_SE_SignatureOnData id="s" certificateRef="cert" dataStatus="unencrypted">not-base64</S100SE:S100_SE_SignatureOnData>""")]
    public void Read_RejectsMalformedSignature(string signatureElement)
    {
        var exception = Assert.Throws<XmlException>(() => ReadCatalogue(
            $"<S100XC:digitalSignatureValue>{signatureElement}</S100XC:digitalSignatureValue>"));

        Assert.NotEmpty(exception.Message);
    }

    [Theory]
    [InlineData("unencrypted", SignatureDataStatus.Unencrypted)]
    [InlineData("compressed", SignatureDataStatus.Compressed)]
    [InlineData("encrypted", SignatureDataStatus.Encrypted)]
    public void Read_ParsesEveryDataStatus(
        string value,
        SignatureDataStatus expected)
    {
        var catalogue = ReadCatalogue(
            $"""
             <S100XC:digitalSignatureValue>
               <S100SE:S100_SE_SignatureOnData id="data" certificateRef="cert" dataStatus="{value}">AQ==</S100SE:S100_SE_SignatureOnData>
             </S100XC:digitalSignatureValue>
             """);

        Assert.Equal(
            expected,
            Assert.Single(catalogue.DatasetDiscoveryMetadata[0].DigitalSignatures).DataStatus);
    }

    [Fact]
    public async Task VerifyAsync_VerifiesAllDataRepresentations()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var certificate = CreateCertificate(ecdsa);
        var certificateId = "cert";
        var plaintext = "protected content"u8.ToArray();
        var compressed = CreateZip(plaintext);
        var cellKey = Enumerable.Range(1, S100Cipher.KeyLength).Select(value => (byte)value).ToArray();
        var encrypted = S100Cipher.EncryptDataset(compressed, cellKey);

        var signatures = new[]
        {
            CreateDataSignature("plain", SignatureDataStatus.Unencrypted, plaintext, ecdsa, certificateId),
            CreateDataSignature("zip", SignatureDataStatus.Compressed, compressed, ecdsa, certificateId),
            CreateDataSignature("cipher", SignatureDataStatus.Encrypted, encrypted, ecdsa, certificateId),
        };
        var catalogue = CreateCatalogue(
            signatures,
            certificate,
            certificateId,
            dataProtection: true,
            compressionFlag: true);
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", encrypted);
        var verifier = new ExchangeSetVerifier(
            new Part15SignatureContentResolver(new FixedKeyProvider(cellKey)));

        var result = await verifier.VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.Ok, file.Outcome);
        Assert.Equal(3, file.SignatureResults.Count);
        Assert.All(file.SignatureResults, signature =>
        {
            Assert.Equal(VerificationOutcome.Ok, signature.Outcome);
            Assert.Equal(SignatureFailureReason.None, signature.FailureReason);
        });
    }

    [Fact]
    public async Task VerifyAsync_VerifiesMultiLevelSignatureChainOverExactDerBytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var certificate = CreateCertificate(ecdsa);
        var content = "chain content"u8.ToArray();
        var dataSignature = CreateDataSignature(
            "data",
            SignatureDataStatus.Unencrypted,
            content,
            ecdsa,
            "cert");
        var firstChain = CreateSignatureOnSignature("chain1", dataSignature, ecdsa, "cert");
        var secondChain = CreateSignatureOnSignature("chain2", firstChain, ecdsa, "cert");
        var catalogue = CreateCatalogue(
            [secondChain, dataSignature, firstChain],
            certificate,
            "cert");
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var file = Assert.Single(result.FileResults);
        Assert.Equal(VerificationOutcome.Ok, file.Outcome);
        Assert.All(file.SignatureResults, signature =>
            Assert.Equal(VerificationOutcome.Ok, signature.Outcome));
    }

    [Fact]
    public async Task VerifyAsync_VerifiesEncryptedSignatureWithoutDatasetKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var certificate = CreateCertificate(ecdsa);
        var storedBytes = RandomNumberGenerator.GetBytes(64);
        var signature = CreateDataSignature(
            "cipher",
            SignatureDataStatus.Encrypted,
            storedBytes,
            ecdsa,
            "cert");
        var catalogue = CreateCatalogue(
            [signature],
            certificate,
            "cert",
            dataProtection: true,
            compressionFlag: true);
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", storedBytes);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(
            VerificationOutcome.Ok,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).Outcome);
    }

    [Fact]
    public async Task VerifyAsync_RejectsNonP384KeyForPart15Algorithm()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = CreateCertificate(ecdsa);
        var content = "wrong curve"u8.ToArray();
        var signature = CreateDataSignature(
            "data",
            SignatureDataStatus.Unencrypted,
            content,
            ecdsa,
            "cert");
        var catalogue = CreateCatalogue([signature], certificate, "cert");
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        var signatureResult = Assert.Single(Assert.Single(result.FileResults).SignatureResults);
        Assert.Equal(VerificationOutcome.Error, signatureResult.Outcome);
        Assert.Equal(SignatureFailureReason.CryptographicError, signatureResult.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_VerifiesLegacyElementWithPart15Algorithm()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var certificate = CreateCertificate(ecdsa);
        var content = "legacy element"u8.ToArray();
        var signature = new DigitalSignatureValue
        {
            Kind = DigitalSignatureKind.Legacy,
            Id = "legacy",
            CertificateRef = "cert",
            Value = ecdsa.SignHash(
                SHA384.HashData(content),
                DSASignatureFormat.Rfc3279DerSequence),
        };
        var catalogue = CreateCatalogue([signature], certificate, "cert");
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(
            VerificationOutcome.Ok,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).Outcome);
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingReference()
    {
        var signature = new DigitalSignatureValue
        {
            Kind = DigitalSignatureKind.SignatureOnSignature,
            Id = "chain",
            CertificateRef = "cert",
            SignatureRef = "missing",
            Value = [1],
        };

        var file = await VerifyStructuralFailureAsync([signature]);

        var result = Assert.Single(file.SignatureResults);
        Assert.Equal(VerificationOutcome.Error, file.Outcome);
        Assert.Equal(SignatureFailureReason.MissingReference, result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_ReportsDuplicateIdentifiers()
    {
        var signatures = new[]
        {
            CreateUnverifiedDataSignature("duplicate"),
            CreateUnverifiedDataSignature("duplicate"),
        };

        var file = await VerifyStructuralFailureAsync(signatures);

        Assert.Equal(2, file.SignatureResults.Count);
        Assert.All(file.SignatureResults, result =>
            Assert.Equal(SignatureFailureReason.DuplicateIdentifier, result.FailureReason));
    }

    [Fact]
    public async Task VerifyAsync_ReportsReferenceCycles()
    {
        var signatures = new[]
        {
            CreateUnverifiedChainSignature("first", "second"),
            CreateUnverifiedChainSignature("second", "first"),
        };

        var file = await VerifyStructuralFailureAsync(signatures);

        Assert.All(file.SignatureResults, result =>
            Assert.Equal(SignatureFailureReason.CyclicReference, result.FailureReason));
    }

    [Fact]
    public async Task VerifyAsync_ReportsUnsupportedAlgorithm()
    {
        var signature = CreateUnverifiedDataSignature("data");
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata
                {
                    FileName = "test.bin",
                    DigitalSignatureReference = "RSA",
                    DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.Unknown,
                    DigitalSignatures = [signature],
                },
            ],
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", [1]);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(
            SignatureFailureReason.UnsupportedAlgorithm,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_RejectsLegacyAlgorithmForModernSignatureForm()
    {
        var signature = CreateUnverifiedDataSignature("data");
        var metadata = CreateMetadata("test.bin", [signature]);
        metadata = new DatasetDiscoveryMetadata
        {
            FileName = metadata.FileName,
            DigitalSignatureReference = "ECDSA",
            DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.ECDSA,
            DigitalSignatures = metadata.DigitalSignatures,
        };
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
            DatasetDiscoveryMetadata = [metadata],
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", [1]);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(
            SignatureFailureReason.UnsupportedAlgorithm,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_ResolvesSchemeAdministratorFromTrustRoots()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var certificate = CreateCertificate(ecdsa);
        var content = "scheme administrator"u8.ToArray();
        var signature = CreateDataSignature(
            "sa",
            SignatureDataStatus.Unencrypted,
            content,
            ecdsa,
            "IHO");
        var catalogue = CreateCatalogue([signature], certificate, "unused");
        catalogue = new ExchangeCatalogue
        {
            Identifier = catalogue.Identifier,
            Certificates = new CertificateBlock
            {
                SchemeAdministratorId = "IHO",
                Certificates = [],
            },
            DatasetDiscoveryMetadata = catalogue.DatasetDiscoveryMetadata,
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { TrustedRoots = [certificate] });

        Assert.Equal(
            VerificationOutcome.Ok,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).Outcome);
    }

    [Fact]
    public async Task VerifyAsync_UsesEmbeddedIntermediateCertificate()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var root = CreateCertificateAuthority("CN=Root", rootKey);
        using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var intermediate = CreateIssuedCertificate(
            "CN=Intermediate",
            intermediateKey,
            root,
            isCertificateAuthority: true);
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var leaf = CreateIssuedCertificate(
            "CN=Leaf",
            leafKey,
            intermediate,
            isCertificateAuthority: false);
        var content = "intermediate chain"u8.ToArray();
        var signature = CreateDataSignature(
            "data",
            SignatureDataStatus.Unencrypted,
            content,
            leafKey,
            "leaf");
        var catalogue = CreateCatalogue([signature], leaf, "leaf");
        catalogue = new ExchangeCatalogue
        {
            Identifier = catalogue.Identifier,
            Certificates = new CertificateBlock
            {
                SchemeAdministratorId = "IHO",
                Certificates =
                [
                    new CertificateEntry
                    {
                        Id = "leaf",
                        Issuer = "intermediate",
                        Value = leaf.RawData,
                    },
                    new CertificateEntry
                    {
                        Id = "intermediate",
                        Issuer = "IHO",
                        Value = intermediate.RawData,
                    },
                ],
            },
            DatasetDiscoveryMetadata = catalogue.DatasetDiscoveryMetadata,
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", content);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { TrustedRoots = [root] });

        Assert.Equal(
            VerificationOutcome.Ok,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).Outcome);
    }

    [Fact]
    public async Task VerifyAsync_ReportsCrossResourceReference()
    {
        var first = CreateUnverifiedDataSignature("data");
        var second = CreateUnverifiedChainSignature("chain", "data");
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
            DatasetDiscoveryMetadata =
            [
                CreateMetadata("first.bin", [first]),
                CreateMetadata("second.bin", [second]),
            ],
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("first.bin", [1]);
        source.AddFile("second.bin", [2]);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(
            SignatureFailureReason.CrossResourceReference,
            Assert.Single(result.FileResults[1].SignatureResults).FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_ReportsUnavailablePlaintextWithoutKey()
    {
        var signature = CreateUnverifiedDataSignature("data");
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
            DatasetDiscoveryMetadata =
            [
                CreateMetadata(
                    "test.bin",
                    [signature],
                    dataProtection: true,
                    compressionFlag: true),
            ],
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", new byte[S100Cipher.BlockSize * 2]);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

        Assert.Equal(
            SignatureFailureReason.ContentUnavailable,
            Assert.Single(Assert.Single(result.FileResults).SignatureResults).FailureReason);
    }

    private static ExchangeCatalogue ReadCatalogue(string signatureElements)
    {
        var xml =
            $$"""
              <S100XC:S100_ExchangeCatalogue
                  xmlns:S100XC="http://www.iho.int/s100/xc/5.0"
                  xmlns:S100SE="http://www.iho.int/s100/se/5.2">
                <S100XC:identifier>
                  <S100XC:identifier>TEST</S100XC:identifier>
                  <S100XC:dateTime>2026-01-01T00:00:00Z</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:certificates>
                  <S100SE:schemeAdministrator id="IHO" />
                  <S100SE:certificate id="cert" issuer="IHO">AQ==</S100SE:certificate>
                </S100XC:certificates>
                <S100XC:datasetDiscoveryMetadata>
                  <S100XC:S100_DatasetDiscoveryMetadata>
                    <S100XC:fileName>test.bin</S100XC:fileName>
                    <S100XC:digitalSignatureReference>ECDSA-384-SHA2</S100XC:digitalSignatureReference>
                    {{signatureElements}}
                  </S100XC:S100_DatasetDiscoveryMetadata>
                </S100XC:datasetDiscoveryMetadata>
              </S100XC:S100_ExchangeCatalogue>
              """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return ExchangeCatalogueReader.Read(stream);
    }

    private static X509Certificate2 CreateCertificate(ECDsa ecdsa)
    {
        var request = new CertificateRequest("CN=Part15 Test", ecdsa, HashAlgorithmName.SHA384);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 CreateCertificateAuthority(
        string subject,
        ECDsa key)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature,
                critical: true));
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 CreateIssuedCertificate(
        string subject,
        ECDsa key,
        X509Certificate2 issuer,
        bool isCertificateAuthority)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                isCertificateAuthority,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                isCertificateAuthority
                    ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature
                    : X509KeyUsageFlags.DigitalSignature,
                critical: true));
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var issued = request.Create(
            issuer,
            new DateTimeOffset(issuer.NotBefore.ToUniversalTime()).AddMinutes(1),
            new DateTimeOffset(issuer.NotAfter.ToUniversalTime()).AddMinutes(-1),
            serialNumber);
        return issued.CopyWithPrivateKey(key);
    }

    private static DigitalSignatureValue CreateDataSignature(
        string id,
        SignatureDataStatus dataStatus,
        byte[] content,
        ECDsa ecdsa,
        string certificateId) =>
        new()
        {
            Kind = DigitalSignatureKind.SignatureOnData,
            Id = id,
            CertificateRef = certificateId,
            DataStatus = dataStatus,
            Value = ecdsa.SignHash(
                SHA384.HashData(content),
                DSASignatureFormat.Rfc3279DerSequence),
        };

    private static DigitalSignatureValue CreateSignatureOnSignature(
        string id,
        DigitalSignatureValue referenced,
        ECDsa ecdsa,
        string certificateId) =>
        new()
        {
            Kind = DigitalSignatureKind.SignatureOnSignature,
            Id = id,
            CertificateRef = certificateId,
            SignatureRef = referenced.Id,
            Value = ecdsa.SignHash(
                SHA384.HashData(referenced.Value),
                DSASignatureFormat.Rfc3279DerSequence),
        };

    private static ExchangeCatalogue CreateCatalogue(
        IReadOnlyList<DigitalSignatureValue> signatures,
        X509Certificate2 certificate,
        string certificateId,
        bool dataProtection = false,
        bool compressionFlag = false) =>
        new()
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
            Certificates = new CertificateBlock
            {
                Certificates =
                [
                    new CertificateEntry
                    {
                        Id = certificateId,
                        Issuer = "Part15 Test",
                        Value = certificate.RawData,
                    },
                ],
            },
            DatasetDiscoveryMetadata =
            [
                CreateMetadata(
                    "test.bin",
                    signatures,
                    dataProtection,
                    compressionFlag),
            ],
        };

    private static DatasetDiscoveryMetadata CreateMetadata(
        string fileName,
        IReadOnlyList<DigitalSignatureValue> signatures,
        bool dataProtection = false,
        bool compressionFlag = false) =>
        new()
        {
            FileName = fileName,
            DataProtection = dataProtection,
            CompressionFlag = compressionFlag,
            DigitalSignatureReference = "ECDSA-384-SHA2",
            DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.ECDSA384SHA2,
            DigitalSignatureValue = signatures.FirstOrDefault(),
            DigitalSignatures = signatures,
        };

    private static DigitalSignatureValue CreateUnverifiedDataSignature(string id) =>
        new()
        {
            Kind = DigitalSignatureKind.SignatureOnData,
            Id = id,
            CertificateRef = "cert",
            DataStatus = SignatureDataStatus.Unencrypted,
            Value = [1],
        };

    private static DigitalSignatureValue CreateUnverifiedChainSignature(string id, string signatureRef) =>
        new()
        {
            Kind = DigitalSignatureKind.SignatureOnSignature,
            Id = id,
            CertificateRef = "cert",
            SignatureRef = signatureRef,
            Value = [1],
        };

    private static async Task<FileVerificationResult> VerifyStructuralFailureAsync(
        IReadOnlyList<DigitalSignatureValue> signatures)
    {
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier
            {
                Identifier = "TEST",
                DateTime = "2026-01-01",
            },
            DatasetDiscoveryMetadata = [CreateMetadata("test.bin", signatures)],
        };
        using var source = new InMemoryAssetSource();
        source.AddFile("test.bin", [1]);

        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });
        return Assert.Single(result.FileResults);
    }

    private static byte[] CreateZip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("test.bin", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(content);
        }

        return output.ToArray();
    }

    private sealed class FixedKeyProvider(byte[] key) : IDatasetKeyProvider
    {
        public bool TryGetCellKey(string datasetFileName, out byte[]? cellKey)
        {
            cellKey = key.ToArray();
            return true;
        }
    }
}
