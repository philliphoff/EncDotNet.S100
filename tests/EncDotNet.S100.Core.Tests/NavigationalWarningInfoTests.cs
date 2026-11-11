using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Core.Tests;

public class NavigationalWarningInfoTests
{
    private static DateTimeOffset Oct(int day, int hour = 0) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    private static NavigationalWarningInfo With(params NavigationalWarningValidity[] validity) =>
        new() { Validity = validity };

    [Fact]
    public void StatusAt_NoValidity_IsInForceUntilCancelled()
    {
        var warning = new NavigationalWarningInfo { CancellationDate = Oct(16, 1) };

        Assert.Equal(NavigationalWarningStatus.InForce, warning.StatusAt(Oct(16)));
        Assert.Equal(NavigationalWarningStatus.Ended, warning.StatusAt(Oct(16, 1)));
    }

    [Fact]
    public void StatusAt_FollowsTheValidityRange()
    {
        var warning = With(new NavigationalWarningValidity(Oct(10), Oct(30)));

        Assert.Equal(NavigationalWarningStatus.NotYetInForce, warning.StatusAt(Oct(9)));
        Assert.Equal(NavigationalWarningStatus.InForce, warning.StatusAt(Oct(10)));
        Assert.Equal(NavigationalWarningStatus.InForce, warning.StatusAt(Oct(30)));
        Assert.Equal(NavigationalWarningStatus.Ended, warning.StatusAt(Oct(30, 1)));
    }

    [Fact]
    public void StatusAt_BetweenRanges_IsNotYetInForce()
    {
        var warning = With(
            new NavigationalWarningValidity(Oct(1), Oct(5)),
            new NavigationalWarningValidity(Oct(20), null));

        Assert.Equal(NavigationalWarningStatus.NotYetInForce, warning.StatusAt(Oct(10)));
        Assert.Equal(NavigationalWarningStatus.InForce, warning.StatusAt(Oct(25)));
    }

    [Fact]
    public void Label_JoinsSeriesAndNumber()
    {
        Assert.Equal("NAVAREA X 106/26", new NavigationalWarningInfo { Series = "NAVAREA X", Number = "106/26" }.Label);
        Assert.Equal("106/26", new NavigationalWarningInfo { Number = "106/26" }.Label);
        Assert.Null(new NavigationalWarningInfo { Series = "NAVAREA X" }.Label);
    }
}
