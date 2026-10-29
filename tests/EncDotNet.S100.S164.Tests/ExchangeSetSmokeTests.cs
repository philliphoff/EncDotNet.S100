using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// Opens every exchange set in the pinned S-164 release, reads every dataset it
/// lists and checks every signature it carries. These aren't S-164 tests
/// themselves; they show the data is usable before the per-part tests rely on
/// it (issue #1120, slice 2).
/// </summary>
public sealed class ExchangeSetSmokeTests
{
    /// <summary>
    /// Exchange sets that deliver only feature and portrayal catalogues.
    /// </summary>
    private static readonly HashSet<string> CatalogueOnly = ["InitialCatalogues", "PowerUpCatalogues"];

    /// <summary>
    /// Exchange sets that deliver only S-101 updates, to apply over a base cell
    /// from another set (PowerUp or GoodBaseCells). Applying updates across
    /// exchange sets isn't supported yet (#1129), so these are only checked
    /// for their contents here.
    /// </summary>
    private static readonly HashSet<string> UpdateOnly =
    [
        "SequentialUpdate1", "SequentialUpdate2", "SequentialUpdate3", "SequentialUpdate4", "SequentialUpdate5",
        "InvalidSequence001", "InvalidSequence002", "InvalidSequence003",
        "NewUpdate", "OldUpdate",
    ];

    public static TheoryData<string> ExchangeSets() => S164TestData.ExchangeSetNames();

    [Theory]
    [MemberData(nameof(ExchangeSets))]
    public async Task EveryListedDatasetOpens(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = await S164TestData.GetExchangeSetAsync(name, ct);

        await using var exchangeSet = await S100ExchangeSet.OpenAsync(zip, ct);

        if (CatalogueOnly.Contains(name))
        {
            Assert.Empty(exchangeSet.Datasets);
            return;
        }

        Assert.NotEmpty(exchangeSet.Datasets);
        if (UpdateOnly.Contains(name))
        {
            Assert.All(exchangeSet.Datasets, entry =>
                Assert.Matches(@"\.(?!000)\d{3}$", entry.Metadata.RelativePath));
            return;
        }

        foreach (var entry in exchangeSet.Datasets)
        {
            using var dataset = await entry.OpenAsync(ct);
            Assert.False(string.IsNullOrEmpty(dataset.Spec.Name), entry.Metadata.RelativePath);
        }
    }

    [Theory]
    [MemberData(nameof(ExchangeSets))]
    public async Task EverySignatureVerifies(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = await S164TestData.GetExchangeSetAsync(name, ct);

        await using var exchangeSet = await S100ExchangeSet.OpenAsync(zip, ct);
        // Every S-164 archive holds its exchange set under S100_ROOT.
        using var source = ZipAssetSource.Create(zip, "S100_ROOT/");

        // The S-164 scheme administrator certificate that issued the v2.0.0
        // data producer certificate isn't published, so the chain can't be
        // anchored; this checks each signature against the certificate the
        // catalogue carries.
        var result = await new ExchangeSetVerifier().VerifyAsync(
            source,
            exchangeSet.Catalogue,
            new TrustAnchorOptions { AllowUntrustedCertificates = true },
            ct);

        Assert.True(
            result.AllValid,
            string.Join(Environment.NewLine, result.FileResults
                .Where(r => r.Outcome != VerificationOutcome.Ok)
                .Select(r => $"{r.FileName}: {r.Outcome} {r.Detail}")));
    }
}
