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
/// <see cref="Datasets.Pipelines.S57DatasetProcessor"/> does, and checks the
/// <see cref="S57UpdateReport"/> it leaves (#1241).
/// </summary>
public sealed class S64Part2UpdateTests
{
    private const string PowerUp = "2.1.1 Power Up/ENC_ROOT/GB5X01SW.000";
    private const string GoodBase = "2.2.5 Good Base Cells/ENC_ROOT/GB5X01SW.000";
    private const string ReIssue = "2.2.6 Re-issue/GB5X01SW_RE-ISSUE/ENC_ROOT/GB5X01SW.000";
    private const string NewUpdate = "2.2.4 Loading of New Update/ENC_ROOT/GB5X01SW.001";
    private const string OldUpdate = "2.2.5 Old Update/ENC_ROOT/GB5X01SW.001";
    private const string Cancellation = "2.2.7 Cancellation/ENC_ROOT/GB4X0000.001";
    private const string CancelledCell = "2.1.1 Power Up/ENC_ROOT/GB4X0000.000";

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
    public async Task CorruptUpdate_FailsItsCrc()
    {
        // 2.2.1: the exchange set holds only updates, and one doesn't match
        // the CRC its catalogue gives.
        var result = await VerifyAsync("2.2.1 Corrupt Update/ENC_ROOT/");

        var mismatched = result.FileResults.Where(r => r.ChecksumOutcome == VerificationOutcome.ChecksumMismatch).ToArray();
        Assert.NotEmpty(mismatched);
        Assert.All(mismatched, r => Assert.StartsWith("GB5X01SW.00", r.FileName, StringComparison.Ordinal));
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
            Assert.True(dataset.UpdateReport!.Success, Describe(dataset.UpdateReport));
            Assert.Equal(applied, dataset.UpdateReport.AppliedThroughUpdateNumber);
            counts.Add(dataset.FeatureCount);
        }

        Assert.Equal([907, 914, 916, 915, 914, 915], counts);
    }

    [Fact]
    public async Task InvalidSequence_IsRefusedWhereTheRecordsStopFollowingOn()
    {
        // 2.2.3: updates 3 and 4 carry the right numbers but each other's
        // records. Update 4's records change nothing update 3 touches, so the
        // one numbered 3 can't be told apart from a good update and applies;
        // the one numbered 4 then changes records already a version on, and is
        // refused with everything after it.
        var ct = TestContext.Current.CancellationToken;

        var dataset = await OpenAsync(PowerUp, [.. Enumerable.Range(1, 5).Select(InvalidSequence)], ct);

        var report = dataset.UpdateReport!;
        Assert.False(report.Success);
        Assert.Equal(3, report.AppliedThroughUpdateNumber);
        var refusal = Assert.Single(report.Messages, m => m.Severity >= S57UpdateSeverity.Warning);
        Assert.Equal((S57UpdateSeverity.Error, 4), (refusal.Severity, refusal.UpdateNumber));
        Assert.Contains("follow on", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingUpdate_StopsTheSequence()
    {
        // 2.2.3 loaded out of order: update 4 after update 2 leaves a gap.
        var ct = TestContext.Current.CancellationToken;

        var dataset = await OpenAsync(PowerUp, [Sequential(1), Sequential(2), Sequential(4), Sequential(5)], ct);

        var report = dataset.UpdateReport!;
        Assert.Equal(2, report.AppliedThroughUpdateNumber);
        var gap = Assert.Single(report.Messages, m => m.Severity >= S57UpdateSeverity.Warning);
        Assert.Equal((S57UpdateSeverity.Warning, 4), (gap.Severity, gap.UpdateNumber));
        Assert.Contains("update 3 is missing", gap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReIssue_TakesUpdate4()
    {
        // 2.2.6: the re-issue holds edition 1 up to update 3, so update 1 is
        // already in it and update 4 goes on it, giving the same cell as
        // updates 1 to 4 on the original.
        var ct = TestContext.Current.CancellationToken;

        var reIssued = await OpenAsync(
            ReIssue, ["2.2.6 Re-issue/GB5X01SW_001/ENC_ROOT/GB5X01SW.001", "2.2.6 Re-issue/GB5X01SW_004/ENC_ROOT/GB5X01SW.004"], ct);
        var updated = await OpenAsync(PowerUp, [.. Enumerable.Range(1, 4).Select(Sequential)], ct);

        Assert.True(reIssued.UpdateReport!.Success, Describe(reIssued.UpdateReport));
        Assert.Equal((3, 4), (reIssued.UpdateReport.BaseUpdateNumber, reIssued.UpdateReport.AppliedThroughUpdateNumber));
        Assert.Equal("4", reIssued.Document.DataSetIdentification?.UpdateNumber?.Trim());
        Assert.Equal(updated.FeatureCount, reIssued.FeatureCount);
    }

    [Fact]
    public async Task NewEdition_TakesItsUpdate()
    {
        // 2.2.4, step 3: edition 2 of the cell takes update 1 of edition 2.
        var dataset = await OpenAsync(GoodBase, [NewUpdate], TestContext.Current.CancellationToken);

        Assert.True(dataset.UpdateReport!.Success, Describe(dataset.UpdateReport));
        Assert.Equal((2, 1), (dataset.UpdateReport.Edition, dataset.UpdateReport.AppliedThroughUpdateNumber));
        Assert.True(dataset.FeatureCount > 0);
    }

    [Fact]
    public async Task NewerEditionUpdate_IsRefusedForItsEdition()
    {
        // 2.2.4, step 1: update 1 of edition 2 doesn't go on edition 1.
        var ct = TestContext.Current.CancellationToken;
        var cell = await OpenAsync(PowerUp, [], ct);

        var dataset = await OpenAsync(PowerUp, [NewUpdate], ct);

        AssertRefusedForEdition(dataset.UpdateReport!, 1);
        Assert.Equal(cell.FeatureCount, dataset.FeatureCount);
    }

    [Fact]
    public async Task OlderEditionUpdate_IsRefused()
    {
        // 2.2.5: update 1 of edition 1 doesn't go on edition 2.
        var ct = TestContext.Current.CancellationToken;

        var dataset = await OpenAsync(GoodBase, [OldUpdate], ct);

        AssertRefusedForEdition(dataset.UpdateReport!, 2);
    }

    [Fact]
    public async Task Cancellation_CancelsTheCell()
    {
        // 2.2.7: a cancellation is an update of edition 0 with no features.
        var dataset = await OpenAsync(CancelledCell, [Cancellation], TestContext.Current.CancellationToken);

        var report = dataset.UpdateReport!;
        Assert.True(report.IsCancelled, Describe(report));
        Assert.Equal(1, report.CancelledByUpdateNumber);
        Assert.False(report.Success);
    }

    private static void AssertRefusedForEdition(S57UpdateReport report, int edition)
    {
        Assert.Equal(edition, report.Edition);
        Assert.Equal(0, report.AppliedThroughUpdateNumber);
        var refusal = Assert.Single(report.Messages);
        Assert.Equal(S57UpdateSeverity.Error, refusal.Severity);
        Assert.Contains("edition", refusal.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(S57UpdateReport? report)
        => report is null ? "no report" : string.Join(" ", report.Messages.Select(m => $"[{m.Severity}] {m.Text}"));

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
