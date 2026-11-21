using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1301 (V30): datasets sharing a supersession key draw one at a time
/// — the latest step at or before the map time, an analysis before a forecast
/// for the same time — and a step past its stale limit is rendered with its age.
/// </summary>
public sealed class MapsuiTimeSupersessionTests
{
    private static readonly DateTime T0 = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static MapsuiTimeCandidate Candidate(string id, DateTime? sample, TimeStepKind kind = TimeStepKind.Analysis, string? key = "fronts") =>
        new(id, key, kind, sample);

    [Fact]
    public void The_latest_step_takes_over_from_an_earlier_one()
    {
        var older = Candidate("12z", T0);
        var newer = Candidate("15z", T0.AddHours(3));

        Assert.True(MapsuiTimeSupersession.IsSuperseded(older, [older, newer]));
        Assert.False(MapsuiTimeSupersession.IsSuperseded(newer, [older, newer]));
    }

    [Fact]
    public void For_the_same_time_an_analysis_takes_over_from_a_forecast()
    {
        var forecast = Candidate("srp", T0, TimeStepKind.Forecast);
        var analysis = Candidate("sus", T0);

        Assert.True(MapsuiTimeSupersession.IsSuperseded(forecast, [forecast, analysis]));
        Assert.False(MapsuiTimeSupersession.IsSuperseded(analysis, [forecast, analysis]));
    }

    [Fact]
    public void Datasets_without_a_step_or_with_another_key_never_take_over()
    {
        var drawn = Candidate("12z", T0);

        Assert.False(MapsuiTimeSupersession.IsSuperseded(drawn, [drawn, Candidate("none", null), Candidate("other", T0.AddHours(3), key: "storms")]));
        Assert.False(MapsuiTimeSupersession.IsSuperseded(Candidate("free", T0, key: null), [Candidate("later", T0.AddHours(1), key: null)]));
    }

    [Fact]
    public void Two_of_the_same_kind_at_the_same_time_resolve_to_one()
    {
        var a = Candidate("a", T0);
        var b = Candidate("b", T0);

        Assert.NotEqual(MapsuiTimeSupersession.IsSuperseded(a, [a, b]), MapsuiTimeSupersession.IsSuperseded(b, [a, b]));
    }

    [Theory]
    [InlineData(5.9, null)]
    [InlineData(6.0, null)]
    [InlineData(6.4, 6)]
    [InlineData(6.5, 7)]
    [InlineData(9.25, 9)]
    public void A_step_is_stale_past_its_limit_with_its_age_to_the_nearest_hour(double hours, int? expected)
    {
        var age = MapsuiTimeSupersession.StaleAge(T0, T0.AddHours(hours), TimeSpan.FromHours(6));

        Assert.Equal(expected is { } h ? TimeSpan.FromHours(h) : null, age);
        Assert.Null(MapsuiTimeSupersession.StaleAge(T0, T0.AddHours(hours), null));
        Assert.Null(MapsuiTimeSupersession.StaleAge(null, T0.AddHours(hours), TimeSpan.FromHours(6)));
    }

    [Fact]
    public async Task The_session_draws_the_latest_analysis_hides_the_older_and_passes_the_stale_age()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = new MapsuiDatasetLayerSession(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));
        var semantics = new TimeSemantics(TimeStepKind.Analysis, TimeStepSelection.AtOrBefore, TimeSpan.FromHours(12), TimeSpan.FromHours(6), "fronts");
        var olderId = new MapDatasetId("older");
        var newerId = new MapDatasetId("newer");
        var older = new StubProcessor(olderId.Value) { AvailableTimes = [T0], TimeSemantics = semantics };
        var newer = new StubProcessor(newerId.Value) { AvailableTimes = [T0.AddHours(3)], TimeSemantics = semantics };
        Assert.True(owner.TryRegister(olderId, older));
        Assert.True(owner.TryRegister(newerId, newer));
        session.SetDataset(Dataset(olderId));
        session.SetDataset(Dataset(newerId));

        // Before the newer analysis: the older one draws, current.
        session.SetCurrentTime(T0.AddHours(2));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Equal(T0, session.GetDataset(olderId)!.Dataset.CurrentTime);
        Assert.Null(session.GetDataset(newerId)!.Dataset.CurrentTime);
        Assert.Null(older.LastContext!.TimeStepAge);

        // After it: the newer takes over and the older hides.
        session.SetCurrentTime(T0.AddHours(4));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Null(session.GetDataset(olderId)!.Dataset.CurrentTime);
        Assert.Empty(session.GetDataset(olderId)!.Layers);
        Assert.Equal(T0.AddHours(3), session.GetDataset(newerId)!.Dataset.CurrentTime);
        var snapshot = session.GetTimeSnapshot();
        var olderTimed = snapshot.Datasets.Single(d => d.DatasetId == olderId.Value);
        Assert.True(olderTimed.IsSupersededAt(T0.AddHours(4), snapshot.Datasets));
        Assert.Equal(T0, olderTimed.SampleAt(T0.AddHours(4)));

        // Seven hours on, the newer analysis is stale: re-rendered with its age, hour by hour.
        session.SetCurrentTime(T0.AddHours(10).AddMinutes(10));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromHours(7), newer.LastContext!.TimeStepAge);
        var renders = newer.RenderCount;
        session.SetCurrentTime(T0.AddHours(10).AddMinutes(20));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Equal(renders, newer.RenderCount);
        session.SetCurrentTime(T0.AddHours(11).AddMinutes(10));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromHours(8), newer.LastContext!.TimeStepAge);
        Assert.Equal(
            TimeSpan.FromHours(8),
            session.GetTimeSnapshot().Datasets.Single(d => d.DatasetId == newerId.Value).StaleAgeAt(T0.AddHours(11).AddMinutes(10)));

        // Past the 12-hour window: nothing.
        session.SetCurrentTime(T0.AddHours(15).AddMinutes(1));
        await session.RefreshTimeAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);
        Assert.Null(session.GetDataset(newerId)!.Dataset.CurrentTime);
    }

    private static MapDataset Dataset(MapDatasetId id) =>
        new(id, id.Value, new DatasetMetadata { Spec = new SpecRef("CODSUS", new SpecVersion(1, 0, 0)) }, true, true);
}
