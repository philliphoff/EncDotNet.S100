using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1298 (V28): a dataset stale a while after it was issued
/// (<see cref="TimeSemantics.StaleAfterIssue"/>, a cyclone advisory's 7 hours)
/// is aged from its issue time at the map time, or at the host's clock when
/// the map time is later, so looking ahead through its forecast doesn't make
/// it older.
/// </summary>
public sealed class MapsuiIssueStalenessTests
{
    private static readonly DateTime Issued = new(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(6.9, null, null)]
    [InlineData(7.4, null, 7)]
    [InlineData(9.0, null, 9)]
    // Looking 48 h ahead while it's 2 h old: current.
    [InlineData(48, 2.0, null)]
    // Looking 48 h ahead while it's 9 h old: 9 h old.
    [InlineData(48, 9.0, 9)]
    // Looking back before now: aged at the map time.
    [InlineData(8, 20.0, 8)]
    public void An_issued_dataset_is_aged_at_the_map_time_or_the_clock_when_thats_earlier(double mapHours, double? nowHours, int? expected)
    {
        var now = nowHours is { } n ? Issued.AddHours(n) : (DateTime?)null;

        var age = MapsuiTimeSupersession.StaleAgeFromIssue(Issued, Issued.AddHours(mapHours), now, TimeSpan.FromHours(7));

        Assert.Equal(expected is { } h ? TimeSpan.FromHours(h) : null, age);
        Assert.Null(MapsuiTimeSupersession.StaleAgeFromIssue(null, Issued.AddHours(mapHours), now, TimeSpan.FromHours(7)));
        Assert.Null(MapsuiTimeSupersession.StaleAgeFromIssue(Issued, Issued.AddHours(mapHours), now, null));
    }

    [Fact]
    public async Task The_session_passes_the_age_since_the_issue_at_its_clock()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var clock = new Clock(new DateTimeOffset(Issued.AddHours(2)));
        using var session = new MapsuiDatasetLayerSession(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()))
        {
            Clock = clock,
        };
        var id = new MapDatasetId("storm");
        var storm = new StubProcessor(id.Value)
        {
            AvailableTimes = [.. Enumerable.Range(0, 11).Select(i => Issued.AddHours(-3 + (12 * i)))],
            IssueTime = Issued,
            TimeSemantics = new TimeSemantics(TimeStepKind.Forecast, TimeStepSelection.Nearest, TimeSpan.FromHours(12), StaleAfterIssue: TimeSpan.FromHours(7)),
        };
        Assert.True(owner.TryRegister(id, storm));
        session.SetDataset(new MapDataset(id, id.Value, new DatasetMetadata { Spec = new SpecRef("TC", new SpecVersion(1, 0, 0)) }, true, true));

        // Two hours after the advisory, looking two days ahead: current.
        session.SetCurrentTime(Issued.AddHours(48));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Null(storm.LastContext!.TimeStepAge);

        // Nine hours after it, still looking ahead: stale, 9 h old, in the render and the snapshot.
        clock.Advance(TimeSpan.FromHours(7));
        session.SetCurrentTime(Issued.AddHours(60));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromHours(9), storm.LastContext!.TimeStepAge);
        var timed = session.GetTimeSnapshot().Datasets.Single(d => d.DatasetId == id.Value);
        Assert.Equal(TimeSpan.FromHours(7), timed.StaleAfterIssue);
        Assert.Equal(Issued, timed.IssueTime);
        Assert.Equal(TimeSpan.FromHours(9), timed.StaleAgeAt(Issued.AddHours(60), clock.GetUtcNow().UtcDateTime));
        Assert.Null(timed.StaleAgeAt(Issued.AddHours(3), clock.GetUtcNow().UtcDateTime));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
