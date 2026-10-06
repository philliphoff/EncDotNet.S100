using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>The Library's pending-work counter (#790).</summary>
public sealed class LibraryActivityTrackerTests
{
    [Fact]
    public void Scopes_count_work_and_datasets_until_disposed()
    {
        var tracker = new LibraryActivityTracker();

        var download = tracker.Begin(15);
        var update = tracker.Begin(0);
        Assert.Equal((2, 15), (tracker.Active, tracker.PendingDatasets));

        download.Dispose();
        Assert.Equal((1, 0), (tracker.Active, tracker.PendingDatasets));

        update.Dispose();
        Assert.Equal((0, 0), (tracker.Active, tracker.PendingDatasets));
    }

    [Fact]
    public void Disposing_a_scope_twice_ends_it_once()
    {
        var tracker = new LibraryActivityTracker();
        using var other = tracker.Begin(1);
        var scope = tracker.Begin(3);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal((1, 1), (tracker.Active, tracker.PendingDatasets));
    }

    [Fact]
    public void A_negative_count_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LibraryActivityTracker().Begin(-1));
}
