using System.IO.Compression;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// S-64 §2 loading and updating tests on the S-57 cell <c>GB5X01SW</c>. Each
/// test takes the base cell from one folder and its updates from others, as
/// the manual delivers them, and folds them in with
/// <see cref="S57Dataset.Open(Stream, IReadOnlyList{Stream})"/>, which is what
/// <see cref="Datasets.Pipelines.S57DatasetProcessor"/> does. S-57 updates
/// aren't checked for edition, sequence or cancellation yet (#1241); those
/// tests are skipped on it.
/// </summary>
public sealed class S64Part2UpdateTests
{
    private const string PowerUp = "2.1.1 Power Up/ENC_ROOT/GB5X01SW.000";
    private const string GoodBase = "2.2.5 Good Base Cells/ENC_ROOT/GB5X01SW.000";
    private const string ReIssue = "2.2.6 Re-issue/GB5X01SW_RE-ISSUE/ENC_ROOT/GB5X01SW.000";
    private const string NewUpdate = "2.2.4 Loading of New Update/ENC_ROOT/GB5X01SW.001";
    private const string OldUpdate = "2.2.5 Old Update/ENC_ROOT/GB5X01SW.001";
    private const string Cancellation = "2.2.7 Cancellation/ENC_ROOT/GB4X0000.001";

    private static string Sequential(int update) => $"2.2.2 Loading of Updates/ENC_ROOT/GB5X01SW.00{update}";

    private static string InvalidSequence(int update) => $"2.2.3 Loading of Invalid Sequence/00{update}/ENC_ROOT/GB5X01SW.00{update}";

    [Fact]
    public async Task PowerUp_EveryCellMatchesItsCrc()
    {
        // 2.1.1: the cells load. Nothing in S-64 is signed, so only the CRCs
        // in CATALOG.031 are checked.
        var result = await VerifyAsync("2.1.1 Power Up/ENC_ROOT/");

        Assert.All(result.FileResults.Where(r => r.FileName.EndsWith(".000", StringComparison.Ordinal)), r =>
            Assert.True(r.ChecksumOutcome == VerificationOutcome.Ok, $"{r.FileName}: {r.ChecksumOutcome} {r.Detail}"));
    }

    [Fact]
    public async Task CorruptData_FailsItsCrc()
    {
        // 2.1.5: the cell doesn't match the CRC its catalogue gives.
        var result = await VerifyAsync("2.1.5 Loading Corrupt Data/ENC_ROOT/");

        var cell = Assert.Single(result.FileResults, r => r.FileName == "GB5X01NE.000");
        Assert.Equal(VerificationOutcome.ChecksumMismatch, cell.ChecksumOutcome);
    }

    [Fact]
    public async Task SequentialUpdates_ApplyInOrder()
    {
        // 2.2.2: updates 1 to 5 of edition 1 apply one after another; each
        // inserts or deletes features.
        var ct = TestContext.Current.CancellationToken;
        List<int> counts = [];
        for (var applied = 0; applied <= 5; applied++)
        {
            var dataset = await OpenAsync(PowerUp, [.. Enumerable.Range(1, applied).Select(Sequential)], ct);
            counts.Add(dataset.FeatureCount);
        }

        Assert.Equal([907, 914, 916, 915, 914, 915], counts);
    }

    [Fact]
    public async Task ReIssue_TakesUpdate4()
    {
        // 2.2.6: the re-issue holds edition 1 up to update 3, so update 4 goes
        // on it and gives the same cell as updates 1 to 4 on the original.
        var ct = TestContext.Current.CancellationToken;

        var reIssued = await OpenAsync(ReIssue, ["2.2.6 Re-issue/GB5X01SW_004/ENC_ROOT/GB5X01SW.004"], ct);
        var updated = await OpenAsync(PowerUp, [.. Enumerable.Range(1, 4).Select(Sequential)], ct);

        Assert.Equal("4", reIssued.Document.DataSetIdentification?.UpdateNumber?.Trim());
        Assert.Equal(updated.FeatureCount, reIssued.FeatureCount);
    }

    [Fact]
    public async Task NewEdition_TakesItsUpdate()
    {
        // 2.2.4, step 3: edition 2 of the cell takes update 1 of edition 2.
        var dataset = await OpenAsync(GoodBase, [NewUpdate], TestContext.Current.CancellationToken);

        Assert.True(dataset.FeatureCount > 0);
    }

    [Fact(Skip = "S-57 updates aren't checked for sequence (#1241).")]
    public async Task InvalidSequence_StopsAfterUpdate3()
    {
        // 2.2.3: updates 4 and 5 don't follow on from 1 to 3 and must be refused.
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            OpenAsync(PowerUp, [.. Enumerable.Range(1, 5).Select(InvalidSequence)], ct));
    }

    [Fact(Skip = "S-57 updates aren't checked for edition (#1241).")]
    public async Task NewerEditionUpdate_IsRefusedForItsEdition()
    {
        // 2.2.4, step 1: update 1 of edition 2 doesn't go on edition 1. It's
        // refused today, but only because a control field happens not to fit.
        var ct = TestContext.Current.CancellationToken;

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => OpenAsync(PowerUp, [NewUpdate], ct));
        Assert.Contains("edition", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "S-57 updates aren't checked for edition (#1241).")]
    public async Task OlderEditionUpdate_IsRefused()
    {
        // 2.2.5: update 1 of edition 1 doesn't go on edition 2.
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidDataException>(() => OpenAsync(GoodBase, [OldUpdate], ct));
    }

    [Fact]
    public async Task Cancellation_IsAnEditionZeroUpdate()
    {
        // 2.2.7: a cancellation is an update of edition 0. Nothing reports the
        // cell as cancelled yet (#1241); this checks the data the report will
        // rely on.
        using var source = await S64TestData.OpenSourceAsync(TestContext.Current.CancellationToken);
        await using var stream = await source.OpenAsync(Cancellation, TestContext.Current.CancellationToken);

        var update = EncDotNet.S57.S57DocumentReader.Read(stream);

        Assert.Equal("0", update.DataSetIdentification?.EditionNumber?.Trim());
        Assert.Empty(update.FeatureRecords);
    }

    private static async Task<S57Dataset> OpenAsync(string cell, IReadOnlyList<string> updates, CancellationToken cancellationToken)
    {
        using var source = await S64TestData.OpenSourceAsync(cancellationToken);
        List<Stream> streams = [];
        try
        {
            foreach (var path in updates)
            {
                streams.Add(await Buffered(source, path, cancellationToken));
            }

            await using var stream = await Buffered(source, cell, cancellationToken);
            return S57Dataset.Open(stream, streams);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static async Task<Stream> Buffered(IAssetSource source, string path, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await using (var stream = await source.OpenAsync(path, cancellationToken))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Verifies the exchange set in <paramref name="folder"/> of the archive,
    /// which <see cref="S57ExchangeSetVerification"/> reads from disk.
    /// </summary>
    private static async Task<ExchangeSetVerificationResult> VerifyAsync(string folder)
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = await S64TestData.GetArchiveAsync(ct);
        var root = Directory.CreateTempSubdirectory("s64-");
        try
        {
            var prefix = S64TestData.Manifest.Root + folder;
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    var target = Path.Combine(root.FullName, entry.FullName[prefix.Length..]);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
            }

            return await S57ExchangeSetVerification.VerifyAsync(root.FullName, cancellationToken: ct);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
