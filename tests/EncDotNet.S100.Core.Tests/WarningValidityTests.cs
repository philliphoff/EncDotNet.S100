using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Core.Tests;

/// <summary>Issue #1069: the in-force rule S-124 and CAP warnings share.</summary>
public sealed class WarningValidityTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 28, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 8, 28, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_range_includes_its_start_and_its_end()
    {
        NavigationalWarningValidity[] ranges = [new(Start, End)];

        Assert.Equal(NavigationalWarningStatus.NotYetInForce, WarningValidity.StatusAt(ranges, null, Start.AddTicks(-1)));
        Assert.Equal(NavigationalWarningStatus.InForce, WarningValidity.StatusAt(ranges, null, Start));
        Assert.Equal(NavigationalWarningStatus.InForce, WarningValidity.StatusAt(ranges, null, End));
        Assert.Equal(NavigationalWarningStatus.Ended, WarningValidity.StatusAt(ranges, null, End.AddTicks(1)));
    }

    [Fact]
    public void A_cancellation_ends_it_whatever_its_ranges_say()
    {
        NavigationalWarningValidity[] ranges = [new(Start, End)];

        Assert.Equal(NavigationalWarningStatus.Ended, WarningValidity.StatusAt(ranges, Start.AddHours(1), Start.AddHours(1)));
        Assert.Equal(NavigationalWarningStatus.InForce, WarningValidity.StatusAt(ranges, Start.AddHours(1), Start.AddMinutes(59)));
        Assert.Equal(NavigationalWarningStatus.InForce, WarningValidity.StatusAt([], null, Start));
    }

    [Fact]
    public void The_navigational_warning_rule_is_this_rule()
    {
        var warning = new NavigationalWarningInfo
        {
            Validity = [new(Start, End), new(End.AddHours(2), null)],
            CancellationDate = End.AddDays(2),
        };

        foreach (var time in WarningValidity.Changes(warning.Validity, warning.CancellationDate).Append(Start.AddTicks(-1)))
            Assert.Equal(WarningValidity.StatusAt(warning.Validity, warning.CancellationDate, time), warning.StatusAt(time));
    }

    [Fact]
    public void The_status_changes_only_at_the_changes()
    {
        NavigationalWarningValidity[] ranges = [new(Start, End), new(null, Start.AddMinutes(-30)), new(End.AddHours(1), null)];
        var changes = WarningValidity.Changes(ranges, End.AddHours(4)).Order().ToArray();

        Assert.Equal([Start.AddMinutes(-30).AddTicks(1), Start, End.AddTicks(1), End.AddHours(1), End.AddHours(4)], changes);
        for (var i = 0; i + 1 < changes.Length; i++)
        {
            Assert.Equal(
                WarningValidity.StatusAt(ranges, End.AddHours(4), changes[i]),
                WarningValidity.StatusAt(ranges, End.AddHours(4), changes[i + 1].AddTicks(-1)));
        }
    }
}
