using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Tests for the Part 15 producer side: signing, standalone signatures, and
/// permit issuing. Each writer is checked by reading its output back through
/// the consumer-side reader or verifier.
/// </summary>
public sealed class Part15ProducerTests : IDisposable
{
    private const string ExampleHardwareId = "40384B45B54596201114FE9904220101";

    private readonly TestScheme _scheme = new();

    public void Dispose() => _scheme.Dispose();

    [Fact]
    public async Task WriteSigned_permit_authenticates_and_unwraps_the_cell_key()
    {
        var cellKey = RandomNumberGenerator.GetBytes(S100Cipher.KeyLength);
        var hardwareId = HardwareId.Parse(ExampleHardwareId);
        var permit = PermitFile.Create(
        [
            new PermitGroup(
                new PermitHeader
                {
                    IssueDate = new DateOnly(2026, 3, 1),
                    DataServerName = "Test Data Server",
                    DataServerIdentifier = "TS",
                    Version = "1.0.0",
                },
                new Dictionary<string, IReadOnlyList<DataPermit>>
                {
                    ["S-101"] = [DataPermit.Create("101AA00000000001", cellKey, hardwareId, new DateOnly(2099, 12, 31), editionNumber: 1)],
                }),
        ]);

        using var signer = _scheme.CreateSigner();
        using var permitXml = new MemoryStream();
        using var permitSign = new MemoryStream();
        PermitFileWriter.WriteSigned(permit, signer, permitXml, permitSign);
        permitXml.Position = 0;
        permitSign.Position = 0;

        var authentication = await PermitSignatureVerifier.AuthenticateAsync(
            permitXml, permitSign, PermitFileWriter.PermitFileName, _scheme.TrustAnchors, TestContext.Current.CancellationToken);

        Assert.True(authentication.IsAuthenticated, authentication.Verification.Detail);
        var keys = new PermitKeyProvider(authentication.PermitFile!, hardwareId, Catalogue("101AA00000000001.000", edition: 1));
        Assert.True(keys.TryGetCellKey("101AA00000000001.000", out var unwrapped));
        Assert.Equal(cellKey, unwrapped);
    }

    [Fact]
    public async Task Producer_signatures_verify_against_the_scheme_through_the_issued_permit()
    {
        const string fileName = "101AA00000000001.000";
        var plaintext = "protected cell"u8.ToArray();
        var cellKey = RandomNumberGenerator.GetBytes(S100Cipher.KeyLength);
        var encrypted = S100Cipher.EncryptDataset(plaintext, cellKey);
        var hardwareId = HardwareId.Parse(ExampleHardwareId);

        using var signer = _scheme.CreateSigner();
        var onPlaintext = signer.SignData(plaintext, "plain");
        DigitalSignatureValue[] signatures = [onPlaintext, signer.SignSignature(onPlaintext, "endorsement")];
        var catalogue = new ExchangeCatalogue
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2026-03-01" },
            Certificates = signer.CreateCertificateBlock(),
            DatasetDiscoveryMetadata =
            [
                new DatasetDiscoveryMetadata
                {
                    FileName = fileName,
                    DataProtection = true,
                    EditionNumber = 1,
                    IssueDate = "2026-03-01",
                    DigitalSignatureReference = "ECDSA-384-SHA2",
                    DigitalSignatureAlgorithm = DigitalSignatureAlgorithm.ECDSA384SHA2,
                    DigitalSignatureValue = onPlaintext,
                    DigitalSignatures = signatures,
                },
            ],
        };

        using var permitXml = new MemoryStream();
        using var permitSign = new MemoryStream();
        PermitFileWriter.WriteSigned(
            PermitFile.Create(
            [
                new PermitGroup(
                    new PermitHeader { IssueDate = new DateOnly(2026, 3, 1) },
                    new Dictionary<string, IReadOnlyList<DataPermit>>
                    {
                        ["S-101"] = [DataPermit.Create("101AA00000000001", cellKey, hardwareId, new DateOnly(2099, 12, 31), editionNumber: 1)],
                    }),
            ]),
            signer,
            permitXml,
            permitSign);
        permitXml.Position = 0;
        permitSign.Position = 0;
        var authentication = await PermitSignatureVerifier.AuthenticateAsync(
            permitXml, permitSign, PermitFileWriter.PermitFileName, _scheme.TrustAnchors, TestContext.Current.CancellationToken);
        var keys = new PermitKeyProvider(authentication.PermitFile!, hardwareId, catalogue);

        using var source = new InMemoryAssetSource();
        source.AddFile(fileName, encrypted);
        var result = await new ExchangeSetVerifier(new Part15SignatureContentResolver(keys))
            .VerifyAsync(source, catalogue, _scheme.TrustAnchors, TestContext.Current.CancellationToken);

        var file = Assert.Single(result.FileResults);
        Assert.Equal(2, file.SignatureResults.Count);
        Assert.All(file.SignatureResults, signature => Assert.True(signature.Outcome == VerificationOutcome.Ok, $"{signature.Id}: {signature.Outcome} {signature.Detail}"));
        Assert.True(result.AllValid);
    }

    [Fact]
    public async Task A_permit_signed_by_another_scheme_is_untrusted()
    {
        using var other = new TestScheme();
        using var signer = other.CreateSigner();
        using var permitXml = new MemoryStream();
        using var permitSign = new MemoryStream();
        PermitFileWriter.WriteSigned(PermitFile.Create([]), signer, permitXml, permitSign);
        permitXml.Position = 0;
        permitSign.Position = 0;

        var authentication = await PermitSignatureVerifier.AuthenticateAsync(
            permitXml, permitSign, PermitFileWriter.PermitFileName, _scheme.TrustAnchors, TestContext.Current.CancellationToken);

        Assert.Equal(VerificationOutcome.CertificateUntrusted, authentication.Verification.Outcome);
    }

    [Fact]
    public void Write_permit_round_trips_every_field()
    {
        var hardwareId = HardwareId.Parse(ExampleHardwareId);
        var key = new byte[S100Cipher.KeyLength];
        var permit = PermitFile.Create(
        [
            new PermitGroup(
                new PermitHeader
                {
                    IssueDate = new DateOnly(2026, 3, 1),
                    DataServerName = "Primar",
                    DataServerIdentifier = "PR",
                    Version = "1.0.0",
                    UserPermit = "AD1DAD797C966EC9F6A55B66ED98281599B3C7B1859868",
                },
                new Dictionary<string, IReadOnlyList<DataPermit>>
                {
                    ["S-101"] = [DataPermit.Create("101GB40079ABCDEF", key, hardwareId, new DateOnly(2027, 12, 31), editionNumber: 10)],
                    ["S-102"] = [DataPermit.Create("102NO329048208.h5", key, hardwareId, new DateOnly(2027, 6, 10), issueDate: new DateOnly(2026, 2, 1))],
                }),
            new PermitGroup(new PermitHeader { DataServerIdentifier = "PR" }, new Dictionary<string, IReadOnlyList<DataPermit>>()),
        ]);

        using var stream = new MemoryStream();
        PermitFileWriter.Write(permit, stream);
        stream.Position = 0;
        var read = PermitFile.Read(stream);

        Assert.Equal(2, read.Groups.Count);
        var header = read.Groups[0].Header;
        Assert.Equal(new DateOnly(2026, 3, 1), header.IssueDate);
        Assert.Equal("Primar", header.DataServerName);
        Assert.Equal("PR", header.DataServerIdentifier);
        Assert.Equal("1.0.0", header.Version);
        Assert.Equal("AD1DAD797C966EC9F6A55B66ED98281599B3C7B1859868", header.UserPermit);

        Assert.True(read.TryGetPermit("101GB40079ABCDEF.000", out var enc, "S-101"));
        Assert.Equal(10, enc!.EditionNumber);
        Assert.Null(enc.IssueDate);
        Assert.Equal(new DateOnly(2027, 12, 31), enc.Expiry);
        Assert.Equal(S100Cipher.EncryptBlock(key, hardwareId.Value), enc.EncryptedKey.ToArray());

        Assert.True(read.TryGetPermit("102NO329048208.h5", out var bathymetry, "S-102"));
        Assert.Null(bathymetry!.EditionNumber);
        Assert.Equal(new DateOnly(2026, 2, 1), bathymetry.IssueDate);
    }

    [Fact]
    public void Written_permit_uses_the_part15_namespace_and_element_order()
    {
        var permit = PermitFile.Create(
        [
            new PermitGroup(
                new PermitHeader { IssueDate = new DateOnly(2026, 3, 1) },
                new Dictionary<string, IReadOnlyList<DataPermit>>
                {
                    ["S-101"] = [DataPermit.Create("CELL", new byte[16], HardwareId.Parse(ExampleHardwareId), new DateOnly(2027, 1, 1), 2, new DateOnly(2026, 1, 1))],
                }),
        ]);

        using var stream = new MemoryStream();
        PermitFileWriter.Write(permit, stream);
        var xml = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("<Permit xmlns=\"http://www.iho.int/s100/se/5.1\">", xml, StringComparison.Ordinal);
        var order = new[] { "<filename>", "<editionNumber>", "<issueDate>2026-01-01", "<expiry>", "<encryptedKey>" }
            .Select(tag => xml.IndexOf(tag, StringComparison.Ordinal))
            .ToArray();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public void Standalone_signature_round_trips_through_the_reader()
    {
        using var signer = _scheme.CreateSigner();
        var signature = signer.SignStandalone("catalogue"u8, "CATALOG.XML", "catalogue-signature");

        using var stream = new MemoryStream();
        StandaloneDigitalSignatureWriter.Write(signature, stream);
        stream.Position = 0;
        var read = StandaloneDigitalSignatureReader.Read(stream);

        Assert.Equal("CATALOG.XML", read.FileName);
        Assert.Equal(TestScheme.SchemeAdministratorId, read.Certificates.SchemeAdministratorId);
        var certificate = Assert.Single(read.Certificates.Certificates);
        Assert.Equal(TestScheme.DataServerId, certificate.Id);
        Assert.Equal(TestScheme.SchemeAdministratorId, certificate.Issuer);
        Assert.Equal(_scheme.DataServer.RawData, certificate.Value);
        Assert.Equal("catalogue-signature", read.Signature.Id);
        Assert.Equal(TestScheme.DataServerId, read.Signature.CertificateRef);
        Assert.Equal(signature.Signature.Value, read.Signature.Value);
    }

    [Fact]
    public async Task SignData_produces_a_der_p384_signature_over_the_content()
    {
        using var signer = _scheme.CreateSigner();
        var content = "dataset bytes"u8.ToArray();

        var signature = signer.SignData(content, "s1");
        var streamed = await signer.SignDataAsync(new MemoryStream(content), "s2", SignatureDataStatus.Encrypted, TestContext.Current.CancellationToken);

        using var publicKey = _scheme.DataServer.GetECDsaPublicKey()!;
        Assert.Equal(DigitalSignatureKind.SignatureOnData, signature.Kind);
        Assert.Equal(SignatureDataStatus.Unencrypted, signature.DataStatus);
        Assert.Equal(TestScheme.DataServerId, signature.CertificateRef);
        Assert.True(publicKey.VerifyHash(SHA384.HashData(content), signature.Value, DSASignatureFormat.Rfc3279DerSequence));
        Assert.Equal(SignatureDataStatus.Encrypted, streamed.DataStatus);
        Assert.True(publicKey.VerifyHash(SHA384.HashData(content), streamed.Value, DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void SignSignature_countersigns_the_referenced_signature_bytes()
    {
        using var signer = _scheme.CreateSigner();
        var onData = signer.SignData("dataset"u8, "s1");

        var onSignature = signer.SignSignature(onData, "s2");

        using var publicKey = _scheme.DataServer.GetECDsaPublicKey()!;
        Assert.Equal(DigitalSignatureKind.SignatureOnSignature, onSignature.Kind);
        Assert.Equal("s1", onSignature.SignatureRef);
        Assert.Null(onSignature.DataStatus);
        Assert.True(publicKey.VerifyHash(SHA384.HashData(onData.Value), onSignature.Value, DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void Signer_requires_a_p384_private_key()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p256Certificate = new CertificateRequest("CN=P-256", p256, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var publicOnly = X509CertificateLoader.LoadCertificate(_scheme.DataServer.RawData);

        Assert.Throws<ArgumentException>(() => new Part15Signer(p256Certificate, "id", "SA"));
        Assert.Throws<ArgumentException>(() => new Part15Signer(publicOnly, "id", "SA"));
    }

    [Fact]
    public void DataPermit_Create_rejects_a_bad_cell_key_or_edition()
    {
        var hardwareId = HardwareId.Parse(ExampleHardwareId);
        var expiry = new DateOnly(2027, 1, 1);

        Assert.Throws<ArgumentException>(() => DataPermit.Create("CELL", new byte[15], hardwareId, expiry));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataPermit.Create("CELL", new byte[16], hardwareId, expiry, editionNumber: 0));
    }

    private static ExchangeCatalogue Catalogue(string fileName, int edition) => new()
    {
        Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2026-03-01" },
        DatasetDiscoveryMetadata =
        [
            new DatasetDiscoveryMetadata
            {
                FileName = fileName,
                DataProtection = true,
                EditionNumber = edition,
                IssueDate = "2026-03-01",
            },
        ],
    };

    /// <summary>A test Scheme Administrator and a data-server certificate it issued.</summary>
    private sealed class TestScheme : IDisposable
    {
        public const string SchemeAdministratorId = "TEST-SA";
        public const string DataServerId = "urn:test:data-server";

        private readonly X509Certificate2 _root;

        public TestScheme()
        {
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var rootRequest = new CertificateRequest("CN=Test Scheme Administrator", rootKey, HashAlgorithmName.SHA384);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, true));
            _root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var serverRequest = new CertificateRequest("CN=Test Data Server", serverKey, HashAlgorithmName.SHA384);
            serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var issued = serverRequest.Create(
                _root, _root.NotBefore.AddMinutes(1), _root.NotAfter.AddMinutes(-1), RandomNumberGenerator.GetBytes(16));
            DataServer = issued.CopyWithPrivateKey(serverKey);
            TrustAnchors = new TrustAnchorOptions { TrustedRoots = [_root] };
        }

        public X509Certificate2 DataServer { get; }

        public TrustAnchorOptions TrustAnchors { get; }

        public Part15Signer CreateSigner() => new(DataServer, DataServerId, SchemeAdministratorId);

        public void Dispose()
        {
            DataServer.Dispose();
            _root.Dispose();
        }
    }
}
