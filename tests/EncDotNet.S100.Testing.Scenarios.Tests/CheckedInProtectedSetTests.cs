using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.ExchangeSets.Protection;
using EncDotNet.S100.Testing.Scenarios.Protection;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// The checked-in <c>tests/datasets/ExchangeSets/Protected-*</c> sets (#768)
/// open with only what a test system has: the files in <c>TEST-ONLY/</c>,
/// and give the outcomes their <c>expected-outcomes.txt</c> lists.
/// </summary>
public sealed class CheckedInProtectedSetTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "ExchangeSets");

    public static TheoryData<string> Sets() => new(ProtectedScenarios.CheckedIn().Select(s => s.Protection.Name));

    [Theory]
    [MemberData(nameof(Sets))]
    public async Task Each_dataset_gives_its_expected_outcome(string name)
    {
        var folder = Path.Combine(Root, name);
        var testOnly = Path.Combine(folder, ProtectedScenarioGenerator.TestOnlyFolder);
        var expected = File.ReadAllLines(Path.Combine(testOnly, "expected-outcomes.txt"));
        var hardwareId = HardwareId.Parse(File.ReadAllText(Path.Combine(testOnly, "hardware-id.txt")).Trim());
        using var schemeAdministrator = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(testOnly, "scheme-administrator.crt")));
        var trust = new TrustAnchorOptions { TrustedRoots = [schemeAdministrator] };

        var permitFolder = File.Exists(Path.Combine(folder, PermitFileWriter.PermitFileName)) ? folder : Path.Combine(testOnly, "separate-permit");
        await using var permitXml = File.OpenRead(Path.Combine(permitFolder, PermitFileWriter.PermitFileName));
        await using var permitSign = File.OpenRead(Path.Combine(permitFolder, PermitFileWriter.SignatureFileName));
        var authentication = await PermitSignatureVerifier.AuthenticateAsync(permitXml, permitSign, PermitFileWriter.PermitFileName, trust, TestContext.Current.CancellationToken);

        var signing = expected[1][(expected[1].IndexOf("PERMIT.SIGN: ", StringComparison.Ordinal) + 13)..];
        Assert.Equal(signing, authentication.Verification.Outcome.ToString());
        if (!authentication.IsAuthenticated)
        {
            return;
        }

        var catalogue = ExchangeCatalogueReader.Read(Path.Combine(folder, "CATALOG.XML"));
        var keys = new PermitKeyProvider(authentication.PermitFile!, hardwareId, catalogue);
        var outcomes = expected.Skip(2).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1].Split(' ', ',')[0]);
        Assert.Equal(outcomes.Keys.Order(StringComparer.Ordinal), catalogue.DatasetDiscoveryMetadata.Select(d => d.RelativePath).Order(StringComparer.Ordinal));
        Assert.All(catalogue.DatasetDiscoveryMetadata, d => Assert.Equal(outcomes[d.RelativePath], keys.Evaluate(d.RelativePath).Outcome.ToString()));

        // Every allowed dataset opens, except where the key was wrapped for
        // another system.
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(folder, TestContext.Current.CancellationToken);
        await using var decrypted = exchangeSet.WithDecryption(keys);
        foreach (var entry in decrypted.Datasets.Where(d => outcomes[d.Metadata.RelativePath] is "Allowed" or "NotProtected"))
        {
            async Task OpenAsync()
            {
                using var dataset = await entry.OpenAsync(TestContext.Current.CancellationToken);
                Assert.Equal(entry.Metadata.ProductSpecification!.ProductIdentifier, dataset.Spec.Name);
            }

            if (name == "Protected-WrongHardwareId" && catalogue.DatasetDiscoveryMetadata.Single(d => d.RelativePath == entry.Metadata.RelativePath).DataProtection)
            {
                await Assert.ThrowsAsync<DatasetDecryptionException>(OpenAsync);
            }
            else
            {
                await OpenAsync();
            }
        }
    }

    // The permit, its signature and the TEST-ONLY files depend only on the
    // seed (no floating point), so they match a fresh run on any platform.
    // The datasets and catalogue sample the world model and may differ in
    // the last bit across platforms, so they're checked by the test above.
    [Theory]
    [MemberData(nameof(Sets))]
    public void The_permit_and_scheme_files_match_a_fresh_run(string name)
    {
        var (data, protection) = ProtectedScenarios.CheckedIn().Single(s => s.Protection.Name == name);
        var fresh = Path.Combine(Path.GetTempPath(), "s100-protected-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var protectedSet = ProtectedScenarioGenerator.Generate(data, protection, fresh);
            var committed = Path.Combine(Root, name);
            var files = Directory.EnumerateFiles(Path.Combine(fresh, ProtectedScenarioGenerator.TestOnlyFolder), "*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(fresh, "PERMIT.*"))
                .Select(f => Path.GetRelativePath(fresh, f))
                .ToList();

            Assert.Contains(files, f => f.EndsWith(PermitFileWriter.SignatureFileName, StringComparison.Ordinal));
            Assert.All(files, f => Assert.True(
                File.ReadAllBytes(Path.Combine(fresh, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(committed, f))),
                $"{f} differs from the checked-in copy; regenerate with S100_PROTECTED_FIXTURES_OUT."));
        }
        finally
        {
            Directory.Delete(fresh, recursive: true);
        }
    }
}
