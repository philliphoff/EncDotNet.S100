using System.Globalization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

public sealed class S57UpdateApplicatorTests
{
    private const int Buoy = 17; // BOYLAT

    [Fact]
    public void Apply_AppliesUpdatesInSequence()
    {
        var cell = Cell(edition: 1, update: 0, Feature(1, 1));

        var result = S57UpdateApplicator.Apply(
            cell,
            [Cell(1, 1, Feature(2, 1)), Cell(1, 2, Feature(1, 2, S57UpdateInstruction.Delete))],
            out var report);

        Assert.True(report.Success);
        Assert.Equal((1, 0, 2), (report.Edition, report.BaseUpdateNumber, report.AppliedThroughUpdateNumber));
        Assert.Equal([2], result.FeatureRecords.Select(f => f.RecordName.RecordId));
    }

    [Fact]
    public void Apply_StopsAtAGap()
    {
        var cell = Cell(1, 0, Feature(1, 1));

        var result = S57UpdateApplicator.Apply(
            cell, [Cell(1, 1, Feature(2, 1)), Cell(1, 3, Feature(3, 1))], out var report);

        Assert.False(report.Success);
        Assert.Equal(1, report.AppliedThroughUpdateNumber);
        var gap = Assert.Single(report.Messages, m => m.Severity >= S57UpdateSeverity.Warning);
        Assert.Equal((S57UpdateSeverity.Warning, 3), (gap.Severity, gap.UpdateNumber));
        Assert.Equal(2, result.FeatureRecords.Count);
    }

    [Theory]
    [InlineData(1, "older edition")]
    [InlineData(3, "Load edition 3")]
    public void Apply_RefusesAnUpdateForAnotherEdition(int edition, string reason)
    {
        var cell = Cell(2, 0, Feature(1, 1));

        var result = S57UpdateApplicator.Apply(cell, [Cell(edition, 1, Feature(2, 1))], out var report);

        Assert.Same(cell, result);
        Assert.Equal(0, report.AppliedThroughUpdateNumber);
        var refusal = Assert.Single(report.Messages);
        Assert.Equal(S57UpdateSeverity.Error, refusal.Severity);
        Assert.Contains(reason, refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SkipsUpdatesAReIssueAlreadyHolds()
    {
        var reIssue = Cell(1, 3, Feature(1, 1));

        S57UpdateApplicator.Apply(reIssue, [Cell(1, 2, Feature(2, 1)), Cell(1, 4, Feature(3, 1))], out var report);

        Assert.True(report.Success);
        Assert.Equal((3, 4), (report.BaseUpdateNumber, report.AppliedThroughUpdateNumber));
        Assert.Contains(report.Messages, m => m is { Severity: S57UpdateSeverity.Info, UpdateNumber: 2 });
    }

    [Fact]
    public void Apply_ReportsACancellation()
    {
        var cell = Cell(1, 0, Feature(1, 1));

        var result = S57UpdateApplicator.Apply(cell, [Cell(0, 1), Cell(1, 2, Feature(2, 1))], out var report);

        Assert.True(report.IsCancelled);
        Assert.Equal(1, report.CancelledByUpdateNumber);
        Assert.False(report.Success);
        Assert.Same(cell, result);
    }

    [Theory]
    [InlineData(S57UpdateInstruction.Modify, 3)] // skips version 2
    [InlineData(S57UpdateInstruction.Modify, 1)] // already at version 1
    [InlineData(S57UpdateInstruction.Insert, 1)] // already there
    public void Apply_RefusesAnUpdateWhoseRecordsDontFollowOn(S57UpdateInstruction instruction, int version)
    {
        var cell = Cell(1, 0, Feature(1, 1));

        var result = S57UpdateApplicator.Apply(cell, [Cell(1, 1, Feature(1, version, instruction))], out var report);

        Assert.Same(cell, result);
        var refusal = Assert.Single(report.Messages);
        Assert.Equal((S57UpdateSeverity.Error, 1), (refusal.Severity, refusal.UpdateNumber));
        Assert.Contains("doesn't follow on", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RefusesAChangeToARecordThatIsntThere()
    {
        var cell = Cell(1, 0, Feature(1, 1));

        S57UpdateApplicator.Apply(cell, [Cell(1, 1, Feature(9, 2, S57UpdateInstruction.Delete))], out var report);

        Assert.Contains("isn't there", Assert.Single(report.Messages).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_AcceptsTwoChangesToOneRecordInAnUpdate()
    {
        // Producers modify a record twice in one update, one version apart.
        var cell = Cell(1, 0, Feature(1, 1));

        S57UpdateApplicator.Apply(
            cell,
            [Cell(1, 1, Feature(1, 2, S57UpdateInstruction.Modify), Feature(1, 3, S57UpdateInstruction.Modify))],
            out var report);

        Assert.True(report.Success, string.Join(" ", report.Messages.Select(m => m.Text)));
    }

    private static S57Document Cell(int edition, int update, params S57FeatureRecord[] features) => new()
    {
        DataSetIdentification = new S57DataSetIdentification
        {
            RecordName = new S57RecordName { RecordNameCode = S57RecordNameCodes.DataSetGeneralInfo, RecordId = 1 },
            DataSetName = "GB5X01SW.000",
            EditionNumber = edition.ToString(CultureInfo.InvariantCulture),
            UpdateNumber = update.ToString(CultureInfo.InvariantCulture),
        },
        FeatureRecords = features,
        VectorRecords = [],
    };

    private static S57FeatureRecord Feature(
        int id, int version, S57UpdateInstruction instruction = S57UpdateInstruction.Insert) => new()
        {
            RecordName = new S57RecordName { RecordNameCode = S57RecordNameCodes.Feature, RecordId = id, AgencyCode = 540, FeatureId = id },
            RecordVersion = version,
            UpdateInstruction = instruction,
            ObjectCode = (S57ObjectCode)Buoy,
            Attributes = [],
            NationalAttributes = [],
            SpatialPointers = [],
            FeaturePointers = [],
        };
}
