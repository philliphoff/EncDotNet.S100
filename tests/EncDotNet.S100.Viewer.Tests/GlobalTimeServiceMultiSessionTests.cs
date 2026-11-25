using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="GlobalTimeService"/> attached to several map sessions (split
/// map, #1731): the timeline reads the primary, every map takes the view time,
/// and detaching the primary hands over to the next session.
/// </summary>
public sealed class GlobalTimeServiceMultiSessionTests
{
    private static readonly DateTime Start = new(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);

    private static MapsuiMapTimeSnapshot Range(DateTime current) => new()
    {
        Minimum = Start,
        Maximum = Start.AddDays(1),
        Current = current,
        Samples = [Start, Start.AddDays(1)],
    };

    [Fact]
    public void A_second_session_attaches_and_takes_the_clock_and_the_view_time()
    {
        var clock = new FakeTimeProvider();
        var service = new GlobalTimeService { Clock = clock };
        var first = new FakeMapDatasetSession();
        var second = new FakeMapDatasetSession();

        service.AttachTo(first);
        service.AttachTo(second);
        service.AttachTo(second);
        first.RaiseTimeRangeChanged(Range(Start));

        Assert.Same(clock, first.Clock);
        Assert.Same(clock, second.Clock);
        Assert.Equal(Start, service.CurrentTime);

        var time = Start.AddHours(3);
        service.SetCurrentTime(time);

        Assert.Equal(time, first.CurrentTime);
        Assert.Equal(time, second.CurrentTime);
    }

    [Fact]
    public void The_timeline_reads_the_primary_only()
    {
        var service = new GlobalTimeService();
        var first = new FakeMapDatasetSession();
        var second = new FakeMapDatasetSession();
        service.AttachTo(first);
        service.AttachTo(second);
        var ranges = 0;
        service.RangeChanged += () => ranges++;

        second.RaiseTimeRangeChanged(Range(Start.AddHours(5)));

        Assert.Equal(0, ranges);
        Assert.False(service.IsActive);
    }

    [Fact]
    public void Detaching_the_primary_hands_the_timeline_to_the_next_session()
    {
        var service = new GlobalTimeService();
        var first = new FakeMapDatasetSession();
        var second = new FakeMapDatasetSession { TimeSnapshot = Range(Start.AddHours(2)) };
        service.AttachTo(first);
        service.AttachTo(second);
        DateTime? announced = null;
        service.CurrentTimeChanged += time => announced = time;

        Assert.True(service.Detach(first));

        Assert.False(first.HasSubscribers);
        Assert.Equal(Start.AddHours(2), service.CurrentTime);
        Assert.Equal(Start.AddHours(2), announced);
        second.RaiseTimeRangeChanged(Range(Start.AddHours(4)));
        Assert.Equal(Start.AddHours(4), service.CurrentTime);

        // The view time no longer reaches the detached session.
        service.SetCurrentTime(Start.AddHours(6));
        Assert.Null(first.CurrentTime);
        Assert.Equal(Start.AddHours(6), second.CurrentTime);

        Assert.True(service.Detach(second));
        Assert.False(service.IsActive);
        Assert.False(service.Detach(second));
    }
}
