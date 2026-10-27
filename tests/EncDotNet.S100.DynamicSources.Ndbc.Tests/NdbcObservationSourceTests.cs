using System.Net;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

public sealed class NdbcObservationSourceTests : IAsyncLifetime
{
    // Massachusetts Bay to Buzzards Bay: holds 44013, BUZM3 and 44020 (which has no file).
    private static readonly BoundingBox NewEngland = new(40.5, -71.5, 43.0, -69.5);
    private static readonly BoundingBox SanFrancisco = new(37.0, -123.5, 38.5, -122.0);

    private readonly FakeNdbcHandler _handler = FakeNdbcHandler.WithFixtures();
    private readonly FakeTimeProvider _time;
    private readonly HttpClient _http;
    private readonly NdbcObservationSource _source;
    private readonly List<DynamicFeaturesChanged> _changes = [];

    public NdbcObservationSourceTests()
    {
        // Ten minutes after the buoy's newest row.
        _time = new FakeTimeProvider(NdbcFixtures.NewestTime(NdbcFixtures.Buoy) + TimeSpan.FromMinutes(10));
        _http = new HttpClient(_handler);
        _source = new NdbcObservationSource(_http, timeProvider: _time);
        _source.Changed += (_, e) => { lock (_changes) _changes.Add(e); };
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _source.DisposeAsync();
        _http.Dispose();
    }

    private static string StationPath(string id) => $"/data/realtime2/{id}.txt";

    [Fact]
    public void Metadata_puts_observations_on_other_chart_overlays_at_priority_100()
    {
        Assert.Equal("ndbc", _source.Id);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, _source.Metadata.Plane);
        Assert.Equal(100, _source.Metadata.WithinPlanePriority);
        Assert.Equal(NdbcObservationSource.FeatureKind, _source.Metadata.RendererKey);
    }

    [Fact]
    public async Task Nothing_is_fetched_without_an_area()
    {
        var result = await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NdbcPollOutcome.Gated, result.Outcome);
        Assert.Empty(_handler.Requests);
        Assert.Empty(_source.CurrentFeatures);
    }

    [Fact]
    public async Task Nothing_is_fetched_for_a_viewport_wider_than_the_span_gate()
    {
        _source.UpdateArea(new BoundingBox(20, -100, 50, -60));

        var result = await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NdbcPollOutcome.Gated, result.Outcome);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Nothing_is_fetched_while_inactive()
    {
        _source.IsActive = false;
        _source.UpdateArea(NewEngland);

        var result = await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NdbcPollOutcome.Gated, result.Outcome);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Only_meteorological_stations_in_the_viewport_are_fetched()
    {
        _source.UpdateArea(NewEngland);

        var result = await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NdbcPollOutcome.Polled, result.Outcome);
        Assert.Equal(1, _handler.CountRequests("/activestations.xml"));
        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Cman)));
        Assert.Equal(1, _handler.CountRequests(StationPath("44020")));
        Assert.Equal(0, _handler.CountRequests(StationPath("46026")));
        Assert.Equal(0, _handler.CountRequests(StationPath("44401")));
        Assert.Equal(2, result.Fetched);
        Assert.Equal(1, result.Failed);

        var ids = _source.CurrentFeatures.Select(f => f.Id).ToArray();
        Assert.Equal(["ndbc:44013", "ndbc:BUZM3"], ids);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task The_margin_reaches_stations_just_outside_the_viewport()
    {
        // 44013 is at 42.346 N 70.651 W: 0.3° east of this viewport's east edge.
        _source.UpdateArea(new BoundingBox(42.0, -71.5, 42.6, -70.95));

        await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
    }

    [Fact]
    public async Task Requests_ask_for_the_head_of_the_file()
    {
        _source.UpdateArea(NewEngland);

        await _source.PollAsync(TestContext.Current.CancellationToken);

        var request = _handler.Requests.First(r => r.RequestUri!.AbsolutePath == StationPath(NdbcFixtures.Buoy));
        var range = Assert.Single(request.Headers.Range!.Ranges);
        Assert.Equal(0, range.From);
        Assert.Equal(16 * 1024 - 1, range.To);
    }

    [Fact]
    public async Task A_feature_carries_the_latest_observation()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);

        var buoy = _source.CurrentFeatures.Single(f => f.Id == "ndbc:44013");
        var observation = NdbcObservationSource.GetObservation(buoy)!;

        Assert.Equal(NdbcObservationSource.FeatureKind, buoy.Kind);
        Assert.Equal(NdbcFixtures.NewestTime(NdbcFixtures.Buoy), buoy.LastUpdated);
        Assert.Equal(observation.Time, buoy.LastUpdated);
        Assert.Equal(42.346, buoy.Coordinates[0].Latitude, 3);
        Assert.NotNull(observation.WindSpeed);
        Assert.NotNull(observation.WaveHeight);
        Assert.False(NdbcObservationSource.IsStale(buoy));
        Assert.Equal("44013", buoy.Attributes[NdbcObservationSource.StationIdAttribute]);
        Assert.Equal("buoy", buoy.Attributes[NdbcObservationSource.StationTypeAttribute]);
    }

    [Fact]
    public async Task Stations_are_polled_again_after_the_poll_interval()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);

        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));

        _time.Advance(TimeSpan.FromMinutes(9));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));

        _time.Advance(TimeSpan.FromMinutes(1));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
        Assert.Equal(1, _handler.CountRequests("/activestations.xml"));
    }

    [Fact]
    public async Task A_failing_station_backs_off_exponentially()
    {
        _source.UpdateArea(NewEngland);
        var path = StationPath("44020");

        await _source.PollAsync(TestContext.Current.CancellationToken); // fails: next try in 10 min
        Assert.IsType<HttpRequestException>(_source.LastError);

        _time.Advance(TimeSpan.FromMinutes(10));
        await _source.PollAsync(TestContext.Current.CancellationToken); // fails again: next try in 20 min
        Assert.Equal(2, _handler.CountRequests(path));

        _time.Advance(TimeSpan.FromMinutes(10));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _handler.CountRequests(path));

        _time.Advance(TimeSpan.FromMinutes(10));
        await _source.PollAsync(TestContext.Current.CancellationToken); // 20 min later: retried, next in 40
        Assert.Equal(3, _handler.CountRequests(path));

        _time.Advance(TimeSpan.FromMinutes(30));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, _handler.CountRequests(path));

        // The healthy stations kept their 10-minute cadence throughout.
        Assert.Equal(5, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
    }

    [Fact]
    public async Task A_station_that_recovers_returns_to_the_poll_interval()
    {
        _handler.Throw(StationPath(NdbcFixtures.Buoy));
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(_source.CurrentFeatures, f => f.Id == "ndbc:44013");

        _handler.Serve(StationPath(NdbcFixtures.Buoy), NdbcFixtures.ReadBytes("44013.txt"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Contains(_source.CurrentFeatures, f => f.Id == "ndbc:44013");

        _time.Advance(TimeSpan.FromMinutes(10));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
    }

    [Fact]
    public async Task A_page_that_is_not_an_ndbc_file_is_an_error_not_a_feature()
    {
        _handler.Serve(StationPath(NdbcFixtures.Buoy), "<html><body>Service unavailable</body></html>");
        _source.UpdateArea(NewEngland);

        var result = await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Failed);
        Assert.DoesNotContain(_source.CurrentFeatures, f => f.Id == "ndbc:44013");
    }

    [Fact]
    public async Task A_failing_station_table_backs_off_and_then_loads()
    {
        _handler.Fail("/activestations.xml", HttpStatusCode.ServiceUnavailable);
        _source.UpdateArea(NewEngland);

        Assert.Equal(NdbcPollOutcome.NoStationTable, (await _source.PollAsync(TestContext.Current.CancellationToken)).Outcome);
        Assert.IsType<HttpRequestException>(_source.LastError);

        _time.Advance(TimeSpan.FromSeconds(30));
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _handler.CountRequests("/activestations.xml"));

        _time.Advance(TimeSpan.FromSeconds(30)); // 1 min: retried, fails, next in 2 min
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _handler.CountRequests("/activestations.xml"));

        _handler.Serve("/activestations.xml", NdbcFixtures.ReadBytes("activestations.xml"), "text/xml");
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(NdbcPollOutcome.NoStationTable, (await _source.PollAsync(TestContext.Current.CancellationToken)).Outcome);

        _time.Advance(TimeSpan.FromMinutes(1));
        var result = await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NdbcPollOutcome.Polled, result.Outcome);
        Assert.Equal(2, _source.CurrentFeatures.Count);
        Assert.Equal(3, _handler.CountObservationRequests());
    }

    [Fact]
    public async Task Observations_turn_stale_after_three_hours_and_drop_after_twelve()
    {
        _source.UpdateArea(NewEngland);
        _source.IsActive = true;
        await _source.PollAsync(TestContext.Current.CancellationToken);
        _source.IsActive = false; // no more fetches: let the observations age
        var observed = NdbcFixtures.NewestTime(NdbcFixtures.Buoy);

        _time.SetUtcNow(observed + TimeSpan.FromHours(2.9));
        _source.Age();
        Assert.False(NdbcObservationSource.IsStale(Buoy()));

        _time.SetUtcNow(observed + TimeSpan.FromHours(3.1));
        _source.Age();
        Assert.True(NdbcObservationSource.IsStale(Buoy()));
        Assert.Equal(NdbcObservationSource.StaleFeatureKind, Buoy().Kind);
        Assert.Contains(_changes, c => c.ChangedIds.Contains("ndbc:44013"));

        _time.SetUtcNow(observed + TimeSpan.FromHours(12.1));
        _source.Age();
        Assert.DoesNotContain(_source.CurrentFeatures, f => f.Id == "ndbc:44013");

        DynamicFeature Buoy() => _source.CurrentFeatures.Single(f => f.Id == "ndbc:44013");
    }

    [Fact]
    public async Task An_observation_already_older_than_twelve_hours_is_not_shown()
    {
        _time.SetUtcNow(NdbcFixtures.NewestTime(NdbcFixtures.Buoy) + TimeSpan.FromHours(13));
        _source.UpdateArea(NewEngland);

        var result = await _source.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Fetched);
        Assert.Empty(_source.CurrentFeatures);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Ageing_without_change_raises_no_event()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);
        _changes.Clear();

        _time.Advance(TimeSpan.FromMinutes(5));
        _source.Age();

        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Panning_away_hides_stations_and_panning_back_shows_them_without_a_fetch()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);

        _source.UpdateArea(SanFrancisco);
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Empty(_source.CurrentFeatures);
        Assert.Equal(1, _handler.CountRequests(StationPath("46026")));

        _time.Advance(TimeSpan.FromMinutes(2));
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _source.CurrentFeatures.Count);
        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
    }

    [Fact]
    public async Task The_polling_loop_fetches_on_a_new_area_and_again_each_interval()
    {
        var first = WaitForChange();
        _source.Start();
        _source.UpdateArea(NewEngland);
        await first.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(1, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));

        // The loop sleeps on the fake clock until the stations fall due. Step the
        // clock a minute at a time, so a step taken before the loop has parked
        // isn't lost.
        for (int minute = 0; minute < 30 && _handler.CountRequests(StationPath(NdbcFixtures.Buoy)) < 2; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await WaitUntil(() => _handler.CountRequests(StationPath(NdbcFixtures.Buoy)) >= 2, TimeSpan.FromMilliseconds(200), throwOnTimeout: false);
        }

        Assert.Equal(2, _handler.CountRequests(StationPath(NdbcFixtures.Buoy)));
        Assert.Equal(1, _handler.CountRequests("/activestations.xml"));
    }

    [Fact]
    public async Task Describe_lists_every_value_with_its_unit_and_the_observation_time()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);
        var buoy = _source.CurrentFeatures.Single(f => f.Id == "ndbc:44013");
        var observation = NdbcObservationSource.GetObservation(buoy)!;

        var description = _source.Describe(buoy)!;
        var rows = description.Properties.ToDictionary(p => p.Label, p => p.Value);

        Assert.StartsWith("BOSTON", description.Title, StringComparison.Ordinal);
        Assert.Equal("44013 (buoy)", rows["Station"]);
        Assert.Equal(NdbcObservationFormatter.FormatTime(observation.Time, _time.GetUtcNow()), rows["Observed"]);
        Assert.EndsWith("UTC (10 min ago)", rows["Observed"], StringComparison.Ordinal);
        Assert.Matches(@"^\d{3}° \d+\.\d kn \(\d+\.\d m/s\)$", rows["Wind"]);
        Assert.Matches(@"kn \(\d+\.\d m/s\)$", rows["Gust"]);
        Assert.Matches(@"^\d+\.\d m \(\d+\.\d ft\)", rows["Wave height"]);
        Assert.EndsWith(" s", rows["Dominant wave period"], StringComparison.Ordinal);
        Assert.EndsWith(" hPa", rows["Pressure"], StringComparison.Ordinal);
        Assert.EndsWith(" °C", rows["Air temperature"], StringComparison.Ordinal);
        Assert.EndsWith(" °C", rows["Water temperature"], StringComparison.Ordinal);
        Assert.Contains("Not for navigation", rows["Source"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_marks_a_stale_observation()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromHours(4));
        _source.Age();

        var description = _source.Describe(_source.CurrentFeatures.Single(f => f.Id == "ndbc:BUZM3"))!;

        Assert.EndsWith("— stale", description.Properties.Single(p => p.Label == "Observed").Value, StringComparison.Ordinal);
        Assert.DoesNotContain(description.Properties, p => p.Label == "Wave height");
    }

    private TaskCompletionSource ChangeSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task WaitForChange()
    {
        _source.Changed += (_, _) => ChangeSignal.TrySetResult();
        return ChangeSignal.Task;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout, bool throwOnTimeout = true)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                if (throwOnTimeout)
                    throw new TimeoutException("The condition wasn't met in time.");
                return;
            }
            await Task.Delay(10);
        }
    }
}
