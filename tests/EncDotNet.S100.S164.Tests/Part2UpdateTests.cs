using System.IO.Compression;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 2 update tests (SequentialUpdates, InvalidSequence,
/// NewerEditionUpdate, OlderEditionUpdate) on the S-101 cell
/// <c>10100AA_X01SW</c>. Each test takes the base cell from one exchange set
/// and its updates from others, as the test sequence delivers them, and
/// applies them with <see cref="S101UpdateApplicator"/>. Applying updates
/// across exchange sets in the apps waits on #1129, so these check the
/// library's verdict only.
/// </summary>
public sealed class Part2UpdateTests
{
    private const string Cell = "10100AA_X01SW";

    [Fact]
    public async Task BaseCell_ReadsEditionAndAssociations()
    {
        // S-101 2.0 writes DSED, FAUI and IUIN just before a repeating group
        // (DSED\\*DSTC, FAUI\\*NATC…), with one association per FASC or INAS field.
        var cell = await ReadAsync("GoodBaseCells", 0, TestContext.Current.CancellationToken);

        Assert.Equal(4, cell.Identification.EditionNumber);
        Assert.Equal(77, cell.Features.Sum(f => f.FeatureAssociations.Count));
        Assert.Equal(20, cell.Features.Sum(f => f.InformationAssociations.Count));
        Assert.All(cell.Features.SelectMany(f => f.FeatureAssociations),
            a => Assert.Equal(S101UpdateInstruction.Insert, a.UpdateInstruction));
    }

    [Fact]
    public async Task SequentialUpdates_ApplyInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var cell = await ReadAsync("PowerUp", 0, ct);
        List<S101Document> updates = [];
        for (var update = 1; update <= 5; update++)
        {
            updates.Add(await ReadAsync($"SequentialUpdate{update}", update, ct));
        }

        S101UpdateApplicator.Apply(cell, updates, out var report);

        Assert.True(report.Success, Describe(report));
        Assert.Equal(5, report.AppliedThroughUpdateNumber);
    }

    [Fact]
    public async Task InvalidSequence_StopsAtTheGap()
    {
        var ct = TestContext.Current.CancellationToken;
        var cell = await ReadAsync("PowerUp", 0, ct);
        S101Document[] updates =
        [
            await ReadAsync("InvalidSequence001", 1, ct),
            await ReadAsync("InvalidSequence002", 2, ct),
            await ReadAsync("InvalidSequence003", 4, ct),
        ];

        S101UpdateApplicator.Apply(cell, updates, out var report);

        Assert.Equal(2, report.AppliedThroughUpdateNumber);
        Assert.Contains(report.Messages, m => m.Severity == S101UpdateSeverity.Warning && m.UpdateNumber == 4);
    }

    [Fact]
    public async Task NewerEditionUpdate_IsRejected()
    {
        // PowerUp holds edition 3 of the cell; NewUpdate is update 1 of edition 4.
        var ct = TestContext.Current.CancellationToken;
        var cell = await ReadAsync("PowerUp", 0, ct);
        var update = await ReadAsync("NewUpdate", 1, ct);
        Assert.Equal((3, 4), (cell.Identification.EditionNumber, update.Identification.EditionNumber));

        var result = S101UpdateApplicator.Apply(cell, [update], out var report);

        Assert.Same(cell, result);
        Assert.Equal(0, report.AppliedThroughUpdateNumber);
        Assert.Contains(report.Messages, m => m.Severity == S101UpdateSeverity.Error && m.Text.Contains("Load edition 4"));
    }

    [Fact]
    public async Task OlderEditionUpdate_IsRejected()
    {
        // GoodBaseCells holds edition 4 of the cell; OldUpdate is update 1 of edition 3.
        var ct = TestContext.Current.CancellationToken;
        var cell = await ReadAsync("GoodBaseCells", 0, ct);
        var update = await ReadAsync("OldUpdate", 1, ct);

        var result = S101UpdateApplicator.Apply(cell, [update], out var report);

        Assert.Same(cell, result);
        Assert.Equal(0, report.AppliedThroughUpdateNumber);
        Assert.Contains(report.Messages, m => m.Severity == S101UpdateSeverity.Error && m.Text.Contains("older edition"));
    }

    [Fact]
    public async Task NewEditionUpdate_AppliesToTheNewEdition()
    {
        var ct = TestContext.Current.CancellationToken;
        var cell = await ReadAsync("GoodBaseCells", 0, ct);
        var update = await ReadAsync("NewUpdate", 1, ct);

        S101UpdateApplicator.Apply(cell, [update], out var report);

        Assert.True(report.Success, Describe(report));
        Assert.Equal(1, report.AppliedThroughUpdateNumber);
    }

    private static async Task<S101Document> ReadAsync(string exchangeSet, int updateNumber, CancellationToken ct)
    {
        var zip = await S164TestData.GetExchangeSetAsync(exchangeSet, ct);
        using var archive = ZipFile.OpenRead(zip);
        var entryName = $"S100_ROOT/S-101/DATASET_FILES/{Cell}.{updateNumber:D3}";
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"'{exchangeSet}' has no {entryName}.");
        using var buffer = new MemoryStream();
        await using (var stream = entry.Open())
        {
            await stream.CopyToAsync(buffer, ct);
        }

        buffer.Position = 0;
        return S101Dataset.Open(buffer).Document;
    }

    private static string Describe(S101UpdateReport report) =>
        string.Join(Environment.NewLine, report.Messages.Select(m => $"{m.Severity}: {m.Text}"));
}
