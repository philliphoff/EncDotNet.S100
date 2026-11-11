using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.ExchangeSets.Protection;
using EncDotNet.S100.Testing.Scenarios.Protection;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// Protected scenarios (#943, absorbing #768) read through the real Part 15
/// consumer side: <see cref="PermitSignatureVerifier"/>,
/// <see cref="PermitKeyProvider"/>, <c>WithDecryption</c> and
/// <see cref="ExchangeSetVerifier"/>. Each test pins today's outcome.
/// </summary>
public sealed class ProtectedScenarioTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-protected-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Basic_set_authenticates_decrypts_and_verifies()
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.Basic());
        var permits = await AuthenticateAsync(protectedSet);
        Assert.True(permits.IsAuthenticated, permits.Verification.Detail);

        using var source = FileSystemAssetSource.Create(protectedSet.Scenario.Root);
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(source, cancellationToken: TestContext.Current.CancellationToken);
        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, exchangeSet.Catalogue);
        Assert.Equal(PermitEvaluationOutcome.Allowed, keys.Evaluate("101ZZ00SPIT3.000").Outcome);
        Assert.Equal(PermitEvaluationOutcome.NotProtected, keys.Evaluate("102ZZ00SPIT.h5").Outcome);

        await using var decrypted = exchangeSet.WithDecryption(keys);
        foreach (var entry in decrypted.Datasets)
        {
            using var dataset = await entry.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(entry.Metadata.ProductSpecification!.ProductIdentifier, dataset.Spec.Name);
        }

        var verification = await new ExchangeSetVerifier(new Part15SignatureContentResolver(keys))
            .VerifyAsync(source, exchangeSet.Catalogue, protectedSet.Scheme.TrustAnchors, TestContext.Current.CancellationToken);
        var cell = Assert.Single(verification.FileResults, f => f.FileName.EndsWith("101ZZ00SPIT3.000", StringComparison.Ordinal));
        Assert.Equal(VerificationOutcome.Ok, cell.Outcome);
    }

    [Fact]
    public async Task The_encrypted_cell_decrypts_to_the_plain_scenario_bytes()
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.Basic());
        var plain = ScenarioGenerator.Generate(ProtectedScenarios.Small() with { Name = "Protected-Basic" }, Path.Combine(_root, "plain"));
        var permits = await AuthenticateAsync(protectedSet);
        var catalogue = ExchangeCatalogueReader.Read(protectedSet.Scenario.CataloguePath);
        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, catalogue);

        Assert.True(keys.TryGetCellKey("101ZZ00SPIT3.000", out var cellKey));
        var encrypted = File.ReadAllBytes(protectedSet.Scenario.Datasets.Single(d => d.ProductIdentifier == "S-101").FullPath);
        var expected = File.ReadAllBytes(plain.Datasets.Single(d => d.ProductIdentifier == "S-101").FullPath);
        Assert.Equal(expected, S100Cipher.DecryptDataset(encrypted, cellKey));

        // The unprotected S-102 surface is written as is.
        Assert.Equal(
            File.ReadAllBytes(plain.Datasets.Single(d => d.ProductIdentifier == "S-102").FullPath),
            File.ReadAllBytes(protectedSet.Scenario.Datasets.Single(d => d.ProductIdentifier == "S-102").FullPath));
    }

    [Fact]
    public async Task Mixed_set_gives_one_outcome_per_dataset()
    {
        using var protectedSet = Generate(ProtectedScenarios.MixedData(), ProtectedScenarios.Mixed());
        var permits = await AuthenticateAsync(protectedSet);
        var catalogue = ExchangeCatalogueReader.Read(protectedSet.Scenario.CataloguePath);
        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, catalogue);

        var outcomes = catalogue.DatasetDiscoveryMetadata.ToDictionary(
            d => Path.GetFileNameWithoutExtension(d.RelativePath),
            d => keys.Evaluate(d.RelativePath).Outcome);

        Assert.Equal(
            new Dictionary<string, PermitEvaluationOutcome>
            {
                ["101ZZ00MIXSW"] = PermitEvaluationOutcome.Allowed,
                ["101ZZ00MIXSE"] = PermitEvaluationOutcome.PermitNotFound,
                ["101ZZ00MIXNW"] = PermitEvaluationOutcome.EditionMismatch,
                ["101ZZ00MIXNE"] = PermitEvaluationOutcome.IssuedAfterExpiry,
                ["101ZZ00SPIT3"] = PermitEvaluationOutcome.Allowed,
                ["102ZZ00SPIT"] = PermitEvaluationOutcome.IssueDateMismatch,
                ["104ZZ00SPIT_STN"] = PermitEvaluationOutcome.NotProtected,
            },
            outcomes);

        // The expiring permit runs out 30 days after the issue date.
        Assert.True(permits.PermitFile!.TryGetPermit("101ZZ00SPIT3.000", out var expiring));
        Assert.Equal(new DateOnly(2026, 3, 30), expiring!.Expiry);
    }

    [Fact]
    public async Task A_refused_permit_throws_DatasetPermitException_on_open()
    {
        using var protectedSet = Generate(ProtectedScenarios.MixedData(), ProtectedScenarios.Mixed());
        var permits = await AuthenticateAsync(protectedSet);
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(protectedSet.Scenario.Root, cancellationToken: TestContext.Current.CancellationToken);
        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, exchangeSet.Catalogue);
        await using var decrypted = exchangeSet.WithDecryption(keys);

        var entry = decrypted.Datasets.Single(d => d.Metadata.FileName.EndsWith("101ZZ00MIXNW.000", StringComparison.Ordinal));
        // The dataset is read lazily: the refusal comes on first use.
        var error = await Assert.ThrowsAsync<DatasetPermitException>(async () =>
        {
            using var dataset = await entry.OpenAsync(TestContext.Current.CancellationToken);
            _ = dataset.Spec;
        });
        Assert.Equal(PermitEvaluationOutcome.EditionMismatch, error.Evaluation.Outcome);
    }

    [Theory]
    [InlineData(PermitSigning.Trusted, VerificationOutcome.Ok)]
    [InlineData(PermitSigning.UntrustedSchemeAdministrator, VerificationOutcome.CertificateUntrusted)]
    [InlineData(PermitSigning.ExpiredCertificate, VerificationOutcome.CertificateExpired)]
    [InlineData(PermitSigning.Unsigned, VerificationOutcome.NotSigned)]
    [InlineData(PermitSigning.Tampered, VerificationOutcome.SignatureInvalid)]
    public async Task Permit_signature_variants_give_their_verification_outcome(PermitSigning signing, VerificationOutcome expected)
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.Signed(signing));

        var permits = await AuthenticateAsync(protectedSet);

        Assert.Equal(expected, permits.Verification.Outcome);
        Assert.Equal(expected == VerificationOutcome.Ok, permits.IsAuthenticated);
    }

    [Fact]
    public async Task A_key_wrapped_for_another_hardware_id_passes_the_permit_but_fails_to_decrypt()
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.WrongHardwareId());
        var permits = await AuthenticateAsync(protectedSet);
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(protectedSet.Scenario.Root, cancellationToken: TestContext.Current.CancellationToken);
        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, exchangeSet.Catalogue);
        Assert.Equal(PermitEvaluationOutcome.Allowed, keys.Evaluate("101ZZ00SPIT3.000").Outcome);

        await using var decrypted = exchangeSet.WithDecryption(keys);
        var entry = decrypted.Datasets.Single(d => d.Metadata.FileName.EndsWith(".000", StringComparison.Ordinal));
        var error = await Assert.ThrowsAsync<DatasetDecryptionException>(async () =>
        {
            using var dataset = await entry.OpenAsync(TestContext.Current.CancellationToken);
            _ = dataset.Spec;
        });
        Assert.EndsWith("101ZZ00SPIT3.000", error.DatasetPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_protected_update_without_its_base_is_refused()
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.UpdateWithoutBase());
        var permits = await AuthenticateAsync(protectedSet);
        var catalogue = ExchangeCatalogueReader.Read(protectedSet.Scenario.CataloguePath);
        var update = Assert.Single(catalogue.DatasetDiscoveryMetadata, d => d.RelativePath.EndsWith(".001", StringComparison.Ordinal));
        Assert.Equal(1, update.UpdateNumber);

        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, catalogue);

        Assert.Equal(PermitEvaluationOutcome.BaseDatasetMissing, keys.Evaluate(update.RelativePath).Outcome);
    }

    [Fact]
    public async Task Signatures_without_dataStatus_verify_on_plain_data_and_are_malformed_on_protected_data()
    {
        // #754: the reader infers "unencrypted" only when the record is neither
        // protected nor compressed; otherwise the verifier rejects the shape.
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.SignaturesWithoutDataStatus());
        var permits = await AuthenticateAsync(protectedSet);
        using var source = FileSystemAssetSource.Create(protectedSet.Scenario.Root);
        var catalogue = ExchangeCatalogueReader.Read(protectedSet.Scenario.CataloguePath);
        var keys = new PermitKeyProvider(permits.PermitFile!, protectedSet.Scheme.HardwareId, catalogue);

        Assert.All(catalogue.DatasetDiscoveryMetadata, d => Assert.Single(d.DigitalSignatures));
        Assert.Null(catalogue.DatasetDiscoveryMetadata.Single(d => d.DataProtection).DigitalSignatures[0].DataStatus);
        Assert.Equal(SignatureDataStatus.Unencrypted, catalogue.DatasetDiscoveryMetadata.Single(d => !d.DataProtection).DigitalSignatures[0].DataStatus);

        var verification = await new ExchangeSetVerifier(new Part15SignatureContentResolver(keys))
            .VerifyAsync(source, catalogue, protectedSet.Scheme.TrustAnchors, TestContext.Current.CancellationToken);

        var plain = Assert.Single(verification.FileResults, f => f.FileName.EndsWith(".h5", StringComparison.Ordinal));
        Assert.Equal(VerificationOutcome.Ok, plain.Outcome);
        var cell = Assert.Single(verification.FileResults, f => f.FileName.EndsWith(".000", StringComparison.Ordinal));
        var signature = Assert.Single(cell.SignatureResults);
        Assert.Equal(VerificationOutcome.Error, signature.Outcome);
        Assert.Equal(SignatureFailureReason.MalformedSignature, signature.FailureReason);
    }

    [Fact]
    public async Task Permit_only_output_keeps_the_permit_apart_from_the_data()
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.PermitOnly());

        Assert.False(File.Exists(Path.Combine(protectedSet.Scenario.Root, PermitFileWriter.PermitFileName)));
        Assert.Equal(Path.Combine(protectedSet.Scenario.Root, "TEST-ONLY", "separate-permit"), protectedSet.PermitDirectory);
        var permits = await AuthenticateAsync(protectedSet);
        Assert.True(permits.IsAuthenticated);
    }

    [Fact]
    public void The_test_only_files_recover_the_hardware_id_from_the_user_permit()
    {
        using var protectedSet = Generate(ProtectedScenarios.Small(), ProtectedScenarios.Basic());
        var testOnly = Path.Combine(protectedSet.Scenario.Root, ProtectedScenarioGenerator.TestOnlyFolder);

        var hardwareId = HardwareId.Parse(File.ReadAllText(Path.Combine(testOnly, "hardware-id.txt")).Trim());
        var userPermit = UserPermit.Parse(File.ReadAllText(Path.Combine(testOnly, "user-permit.txt")).Trim());
        var manufacturerKey = Convert.FromHexString(File.ReadLines(Path.Combine(testOnly, "manufacturer.txt")).Single(l => l.StartsWith("M_KEY ", StringComparison.Ordinal))[6..]);
        using var schemeAdministrator = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(testOnly, "scheme-administrator.crt")));

        Assert.Equal(hardwareId, userPermit.DecryptHardwareId(manufacturerKey));
        Assert.Equal(SyntheticScheme.ManufacturerId, userPermit.ManufacturerId);
        Assert.Equal(protectedSet.Scheme.SchemeAdministrator.RawData, schemeAdministrator.RawData);
        Assert.Contains("TEST ONLY", schemeAdministrator.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_sets_are_byte_identical_on_a_second_run()
    {
        // Keys, certificates, RFC 6979 signatures, cell keys, prefix blocks and
        // IVs all come from the seed.
        using var first = Generate(ProtectedScenarios.MixedData(), ProtectedScenarios.Mixed(), "a");
        using var second = Generate(ProtectedScenarios.MixedData(), ProtectedScenarios.Mixed(), "b");

        var a = Hashes(first.Scenario.Root);
        Assert.Equal(a, Hashes(second.Scenario.Root));
        Assert.Contains("PERMIT.SIGN", a.Keys);
        Assert.Contains("TEST-ONLY/scheme-administrator.crt", a.Keys);
    }

    [Fact]
    public void Deterministic_keys_sign_verifiably_and_repeatably()
    {
        using var key = DeterministicEcdsa.Derive(1, "test");
        using var again = DeterministicEcdsa.Derive(1, "test");
        using var other = DeterministicEcdsa.Derive(2, "test");
        var hash = SHA384.HashData("synthetic"u8);

        var signature = key.SignHash(hash);

        Assert.Equal(96, signature.Length);
        Assert.Equal(signature, again.SignHash(hash));
        Assert.True(key.VerifyHash(hash, signature));
        using var platform = ECDsa.Create(key.ExportParameters(false));
        Assert.True(platform.VerifyHash(hash, signature));
        Assert.True(platform.VerifyHash(hash, key.SignHash(hash, DSASignatureFormat.Rfc3279DerSequence), DSASignatureFormat.Rfc3279DerSequence));
        Assert.False(other.VerifyHash(hash, signature));
    }

    private ProtectedScenario Generate(ScenarioDefinition data, ProtectionSpec protection, string? folder = null) =>
        ProtectedScenarioGenerator.Generate(data, protection, Path.Combine(_root, folder ?? protection.Name));

    private static async Task<PermitAuthenticationResult> AuthenticateAsync(ProtectedScenario protectedSet)
    {
        await using var permitXml = File.OpenRead(protectedSet.PermitPath);
        await using var permitSign = File.Exists(protectedSet.PermitSignaturePath) ? File.OpenRead(protectedSet.PermitSignaturePath) : null;
        return await PermitSignatureVerifier.AuthenticateAsync(
            permitXml, permitSign, PermitFileWriter.PermitFileName, protectedSet.Scheme.TrustAnchors, TestContext.Current.CancellationToken);
    }

    private static SortedDictionary<string, string> Hashes(string root)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            hashes[Path.GetRelativePath(root, file).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return hashes;
    }
}
