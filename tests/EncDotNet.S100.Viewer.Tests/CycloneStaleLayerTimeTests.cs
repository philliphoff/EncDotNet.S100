using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1298 (V28, §7): a storm's Datasets row gets the Stale tag 7 hours
/// after its advisory, its age taken at the view time or at now when the view
/// time is ahead, so stepping through the forecast doesn't age it.
/// </summary>
public sealed class CycloneStaleLayerTimeTests
{
    // Synoptic 18Z, issued 21Z; forecast points every 12 h to +48 h.
    private static readonly DateTime Synoptic = new(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Issued = Synoptic.AddHours(3);

    private static MapsuiMapTimedDataset Zelda()
    {
        DateTime[] points = [Synoptic, Synoptic.AddHours(12), Synoptic.AddHours(24), Synoptic.AddHours(36), Synoptic.AddHours(48)];
        return new MapsuiMapTimedDataset("al142026_5day_017.zip", Synoptic.AddHours(-24), points[^1])
        {
            DatasetId = "zelda",
            ProductSpec = "TC",
            Samples = points,
            Selection = MapsuiTimeSelectionKind.Continuous,
            Tolerance = TimeSpan.FromHours(24),
            IssueTime = Issued,
            StaleAfterIssue = TimeSpan.FromHours(7),
            Coverage = [new MapsuiMapTimeSegment(Synoptic.AddHours(-24), points[^1])],
        };
    }

    private static LayerTime At(DateTime view, DateTime now) =>
        LayerTimes.Describe(Zelda(), view, drawing: false, TimeFormat.Utc, TimeZoneInfo.Utc, now: now);

    [Fact]
    public void Looking_ahead_two_hours_after_the_advisory_its_current()
    {
        var time = At(Issued.AddHours(24), Issued.AddHours(2));

        Assert.False(time.IsStale);
    }

    [Fact]
    public void Nine_hours_after_the_advisory_its_stale_whatever_the_view_time_ahead()
    {
        var live = At(Issued.AddHours(9), Issued.AddHours(9));
        var ahead = At(Issued.AddHours(30), Issued.AddHours(9));
        var beyond = At(Synoptic.AddHours(60), Issued.AddHours(9));

        Assert.Equal(TimeSpan.FromHours(9), live.StaleAge);
        Assert.Equal(TimeSpan.FromHours(9), ahead.StaleAge);
        Assert.Equal(LayerTimeState.OutOfRange, beyond.State);
        Assert.Equal(TimeSpan.FromHours(9), beyond.StaleAge);
    }

    [Fact]
    public void Looking_back_at_the_best_track_its_aged_at_the_view_time()
    {
        Assert.False(At(Synoptic.AddHours(-12), Issued.AddHours(20)).IsStale);
    }
}
