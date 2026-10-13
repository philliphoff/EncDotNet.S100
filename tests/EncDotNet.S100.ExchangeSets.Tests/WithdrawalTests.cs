namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Cancellation records that ship no file (#919): AMSA's S-124 exchange sets
/// list every withdrawn warning as a <c>cancellation</c> record.
/// </summary>
public class WithdrawalTests
{
    [Theory]
    [InlineData("cancellation", "S-124/DATASET_FILES/124AU01NA9726.gml", true)]
    [InlineData(" Cancellation ", "warnings.gml", true)]
    [InlineData("cancellation", "101GB00ABCDE.001", false)]
    [InlineData("newDataset", "warnings.gml", false)]
    [InlineData(null, "warnings.gml", false)]
    public void IsWithdrawal_IsACancellationThatIsNotASequentialFile(string? purpose, string fileName, bool expected)
    {
        var dataset = Dataset(fileName, purpose);

        Assert.Equal(expected, dataset.IsWithdrawal);
    }

    [Fact]
    public void CurrentDatasets_LeaveOutWithdrawals()
    {
        var catalogue = Catalogue(
            Dataset("current.gml", "newDataset"),
            Dataset("withdrawn.gml", "cancellation"),
            Dataset("101GB00ABCDE.001", "cancellation"));

        Assert.Equal(
            ["current.gml", "101GB00ABCDE.001"],
            catalogue.CurrentDatasets.Select(d => d.FileName));
    }

    [Fact]
    public async Task VerifyAsync_ReportsAWithdrawalAsWithdrawn()
    {
        var catalogue = Catalogue(
            Dataset("current.gml", "newDataset"),
            Dataset("withdrawn.gml", "cancellation"));
        using var source = new InMemoryAssetSource();
        source.AddFile("current.gml", [1]);

        var result = await Verify(source, catalogue);

        Assert.Equal(
            [VerificationOutcome.NotSigned, VerificationOutcome.Withdrawn],
            result.FileResults.Select(f => f.Outcome));
        Assert.Equal(VerificationOutcome.Withdrawn, result.FileResults[1].ChecksumOutcome);
        Assert.False(result.HasMissingFiles);
        Assert.True(result.IntegrityVerified);
        Assert.True(result.IsUnsigned);
    }

    [Fact]
    public async Task VerifyAsync_StillReportsOtherMissingFiles()
    {
        var catalogue = Catalogue(
            Dataset("missing.gml", "newDataset"),
            Dataset("101GB00ABCDE.001", "cancellation"));
        using var source = new InMemoryAssetSource();

        var result = await Verify(source, catalogue);

        Assert.All(result.FileResults, f => Assert.Equal(VerificationOutcome.FileMissing, f.Outcome));
        Assert.False(result.IntegrityVerified);
    }

    [Fact]
    public async Task VerifyAsync_DoesNotVerifyAWithdrawalWhoseFileIsPresent()
    {
        // AUSCOAST lists a current record and a cancellation for the same file;
        // the cancellation's signature covers the withdrawn content.
        var catalogue = Catalogue(
            Dataset("warning.gml", "newDataset"),
            Dataset("warning.gml", "cancellation"));
        using var source = new InMemoryAssetSource();
        source.AddFile("warning.gml", [1]);

        var result = await Verify(source, catalogue);

        Assert.Equal(
            [VerificationOutcome.NotSigned, VerificationOutcome.Withdrawn],
            result.FileResults.Select(f => f.Outcome));
    }

    [Fact]
    public void AllValid_LeavesOutWithdrawals()
    {
        var result = new ExchangeSetVerificationResult
        {
            FileResults =
            [
                new FileVerificationResult { FileName = "current.gml", Outcome = VerificationOutcome.Ok },
                new FileVerificationResult { FileName = "withdrawn.gml", Outcome = VerificationOutcome.Withdrawn },
            ],
        };

        Assert.True(result.AllValid);
        Assert.False(result.IsUnsigned);
    }

    private static Task<ExchangeSetVerificationResult> Verify(InMemoryAssetSource source, ExchangeCatalogue catalogue) =>
        new ExchangeSetVerifier().VerifyAsync(
            source,
            catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true });

    private static ExchangeCatalogue Catalogue(params DatasetDiscoveryMetadata[] datasets) =>
        new()
        {
            Identifier = new ExchangeCatalogueIdentifier { Identifier = "TEST", DateTime = "2026-01-01" },
            DatasetDiscoveryMetadata = datasets,
        };

    private static DatasetDiscoveryMetadata Dataset(string fileName, string? purpose) =>
        new() { FileName = fileName, Purpose = purpose };
}
