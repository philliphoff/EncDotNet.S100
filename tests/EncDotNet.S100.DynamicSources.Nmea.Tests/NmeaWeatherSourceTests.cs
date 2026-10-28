using System.Runtime.CompilerServices;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Quantities;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.DynamicSources.Nmea.Tests;

public sealed class NmeaWeatherSourceTests : IAsyncLifetime
{
    internal static readonly DateTimeOffset Start = new(2026, 10, 27, 14, 50, 0, TimeSpan.Zero);

    // The fixtures' RMC position: 42°21.000' N, 070°39.000' W.
    private static readonly GeoPosition RmcPosition = new(42.35, -70.65);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly List<DynamicFeaturesChanged> _changes = [];
    private NmeaWeatherSource? _source;

    internal static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "NMEA", name);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_source is not null)
            await _source.DisposeAsync();
    }

    private NmeaWeatherSource Create(INmeaLineSource? input = null, NmeaWeatherSourceOptions? options = null)
    {
        _source = new NmeaWeatherSource(input, options, _time);
        _source.Changed += (_, e) => { lock (_changes) _changes.Add(e); };
        return _source;
    }

    private async Task<NmeaWeatherSource> Replay(string fixture)
    {
        var source = Create(new NmeaReplayFile(FixturePath(fixture)));
        await source.ReadToEndAsync(TestContext.Current.CancellationToken);
        return source;
    }

    private static OwnShipWeather Weather(NmeaWeatherSource source) =>
        NmeaWeatherSource.GetWeather(Assert.Single(source.CurrentFeatures))!;

    private static void Feed(NmeaWeatherSource source, params string[] bodies)
    {
        foreach (var body in bodies)
            Assert.True(source.ProcessLine(NmeaSentence.Format(body)), body);
    }

    [Fact]
    public void Metadata_puts_own_ship_weather_on_other_chart_overlays_above_buoys()
    {
        var source = Create();

        Assert.Equal("nmea-weather", source.Id);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, source.Metadata.Plane);
        Assert.Equal(110, source.Metadata.WithinPlanePriority);
        Assert.Equal(NmeaWeatherSource.FeatureKind, source.Metadata.RendererKey);
        Assert.Empty(source.CurrentFeatures);
    }

    [Fact]
    public async Task A_full_instrument_stream_gives_mwd_true_wind_temperatures_and_pressure()
    {
        var source = await Replay("ownship-weather.nmea");

        var feature = Assert.Single(source.CurrentFeatures);
        Assert.Equal(NmeaWeatherSource.FeatureId, feature.Id);
        Assert.Equal(NmeaWeatherSource.FeatureKind, feature.Kind);
        // The last RMC: 42°21.001' N, 070°38.998' W.
        Assert.Equal(42.35 + 0.001 / 60, feature.Coordinates[0].Latitude, 9);
        Assert.Equal(-(70 + 38.998 / 60), feature.Coordinates[0].Longitude, 9);

        var weather = Weather(source);
        Assert.Equal(TrueWindSource.Mwd, weather.TrueWindSource);
        Assert.Equal(200.0, weather.TrueWind!.Value.FromDirection.TotalDegrees);
        Assert.Equal(14.0, weather.TrueWind!.Value.Speed.TotalKnots, 9);
        Assert.Equal(83.0, weather.RelativeWindAngle!.Value.TotalDegrees);
        Assert.Equal(13.8, weather.RelativeWindSpeed!.Value.TotalKnots, 9);
        Assert.Equal(90.1, weather.Heading!.Value.TotalDegrees, 9);
        Assert.Equal(173.1, weather.RelativeWindFromDirection!.Value.TotalDegrees, 9);
        Assert.Equal(12.4, weather.WaterTemperatureCelsius);
        Assert.Equal(10.8, weather.AirTemperatureCelsius); // not the engine's 78.5
        Assert.Equal(1013.2, weather.PressureHectopascals!.Value, 6);
        Assert.False(weather.IsRelativeOnly);

        // 20 whole sentences; the bad checksum, the cut-off MWD and the noise
        // are rejected; comments never reach the source.
        Assert.Equal(20, source.SentencesAccepted);
        Assert.Equal(3, source.LinesRejected);
    }

    [Fact]
    public async Task Relative_wind_with_heading_and_ground_motion_gives_true_wind_over_the_ground()
    {
        var source = await Replay("apparent-wind.nmea");

        var weather = Weather(source);
        Assert.Equal(TrueWindSource.DerivedOverGround, weather.TrueWindSource);
        Assert.Equal(200.0, weather.TrueWind!.Value.FromDirection.TotalDegrees, 0.5);
        Assert.Equal(14.0, weather.TrueWind!.Value.Speed.TotalKnots, 0.1);
    }

    [Fact]
    public void Without_ground_motion_true_wind_is_through_the_water()
    {
        var source = Create();
        Feed(source, "HEHDT,90.0,T", "IIVHW,90.0,T,104.5,M,6.0,N,11.1,K", "IIMWV,083.0,R,13.8,N,A");
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, null, null, null, Start));
        source.Refresh();

        var weather = Weather(source);
        Assert.Equal(TrueWindSource.DerivedThroughWater, weather.TrueWindSource);
        Assert.Equal(197.4, weather.TrueWind!.Value.FromDirection.TotalDegrees, 0.5);
        Assert.Equal(14.4, weather.TrueWind!.Value.Speed.TotalKnots, 0.1);
    }

    [Fact]
    public void Mwv_true_is_turned_into_a_direction_with_the_heading_and_beats_water_wind()
    {
        var source = Create();
        Feed(source, "HEHDT,90.0,T", "IIVHW,90.0,T,104.5,M,6.0,N,11.1,K", "IIMWV,083.0,R,13.8,N,A", "IIMWV,107.4,T,14.4,N,A");
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, null, null, null, Start));
        source.Refresh();

        var weather = Weather(source);
        Assert.Equal(TrueWindSource.MwvTrue, weather.TrueWindSource);
        Assert.Equal(197.4, weather.TrueWind!.Value.FromDirection.TotalDegrees, 9);
        Assert.Equal(14.4, weather.TrueWind!.Value.Speed.TotalKnots, 9);
    }

    [Fact]
    public void Mwd_with_only_a_magnetic_direction_uses_the_variation()
    {
        var source = Create();
        Feed(source, "GPRMC,145000.00,A,4221.000,N,07039.000,W,6.5,95.0,271026,14.5,W,A", "IIMWD,,T,214.5,M,14.0,N,7.2,M");
        source.Refresh();

        var weather = Weather(source);
        Assert.Equal(TrueWindSource.Mwd, weather.TrueWindSource);
        Assert.Equal(200.0, weather.TrueWind!.Value.FromDirection.TotalDegrees, 9);
    }

    [Fact]
    public void Hdg_with_variation_gives_the_heading()
    {
        var source = Create();
        Feed(source, "HCHDG,104.5,0.0,E,14.5,W", "IIMWV,083.0,R,13.8,N,A", "GPVTG,95.0,T,109.5,M,6.5,N,12.0,K,A");
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, null, null, null, Start));
        source.Refresh();

        var weather = Weather(source);
        Assert.Equal(90.0, weather.Heading!.Value.TotalDegrees, 9);
        Assert.Equal(TrueWindSource.DerivedOverGround, weather.TrueWindSource);
    }

    [Fact]
    public async Task Relative_wind_without_a_heading_is_relative_only()
    {
        var source = await Replay("relative-wind.nmea");

        var weather = Weather(source);
        Assert.True(weather.IsRelativeOnly);
        Assert.Null(weather.TrueWind);
        Assert.Equal(TrueWindSource.None, weather.TrueWindSource);
        Assert.Null(weather.Heading);
        Assert.Null(weather.RelativeWindFromDirection);
        // The last valid MWV, 6.2 m/s; the status-V one is ignored.
        Assert.Equal(46.0, weather.RelativeWindAngle!.Value.TotalDegrees);
        Assert.Equal(6.2, weather.RelativeWindSpeed!.Value.TotalMetresPerSecond, 9);
        Assert.Equal(RmcPosition, Assert.Single(source.CurrentFeatures).Coordinates[0]);
    }

    [Fact]
    public void Relative_wind_with_a_heading_but_no_speed_is_relative_with_a_direction()
    {
        var source = Create();
        Feed(source, "WIMWV,045.0,R,12.0,N,A");
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, Angle.FromDegrees(300), null, null, Start));
        source.Refresh();

        var weather = Weather(source);
        Assert.True(weather.IsRelativeOnly);
        Assert.Equal(345.0, weather.RelativeWindFromDirection!.Value.TotalDegrees, 9);
    }

    [Fact]
    public async Task The_host_supplies_the_position_and_any_motion_the_stream_lacks()
    {
        var source = await Replay("relative-wind.nmea");
        var ownShip = new GeoPosition(42.36, -70.70);

        // The stream has ground motion but no heading; the host has a heading.
        source.UpdateOwnShip(new OwnShipReference(ownShip, Angle.FromDegrees(90), null, null, Start));
        source.Refresh();

        var feature = Assert.Single(source.CurrentFeatures);
        Assert.Equal(ownShip, feature.Coordinates[0]);
        var weather = NmeaWeatherSource.GetWeather(feature)!;
        Assert.Equal(TrueWindSource.DerivedOverGround, weather.TrueWindSource);
        Assert.Equal(90.0, weather.Heading!.Value.TotalDegrees);
    }

    [Fact]
    public void The_streams_heading_wins_over_the_hosts()
    {
        var source = Create();
        Feed(source, "HEHDT,90.0,T", "WIMWV,045.0,R,12.0,N,A");
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, Angle.FromDegrees(300), null, Speed.Zero, Start));
        source.Refresh();

        var weather = Weather(source);
        Assert.Equal(90.0, weather.Heading!.Value.TotalDegrees);
        // Stationary: the host's zero speed over the ground, so true is relative turned by the heading.
        Assert.Equal(TrueWindSource.DerivedOverGround, weather.TrueWindSource);
        Assert.Equal(135.0, weather.TrueWind!.Value.FromDirection.TotalDegrees, 9);
        Assert.Equal(12.0, weather.TrueWind!.Value.Speed.TotalKnots, 9);
    }

    [Fact]
    public void No_position_means_no_feature()
    {
        var source = Create();
        Feed(source, "IIMTW,12.4,C", "WIMWV,045.0,R,12.0,N,A");
        source.Refresh();

        Assert.Empty(source.CurrentFeatures);
        Assert.Equal(12.4, source.Current!.WaterTemperatureCelsius);
    }

    [Fact]
    public async Task Stale_motion_is_not_combined_with_wind()
    {
        var source = await Replay("apparent-wind.nmea");
        Assert.Equal(TrueWindSource.DerivedOverGround, Weather(source).TrueWindSource);

        // Wind keeps coming, the compass and GNSS go quiet.
        _time.Advance(TimeSpan.FromSeconds(11));
        Feed(source, "IIMWV,083.0,R,13.8,N,A");
        source.Refresh();

        var weather = Weather(source);
        Assert.True(weather.IsRelativeOnly);
        Assert.Null(weather.Heading);
    }

    [Fact]
    public async Task Values_are_dropped_after_a_minute_and_then_the_feature_goes()
    {
        var source = await Replay("ownship-weather.nmea");
        Assert.Single(source.CurrentFeatures);
        lock (_changes) _changes.Clear();

        _time.Advance(TimeSpan.FromSeconds(61));
        source.Refresh();

        Assert.Empty(source.CurrentFeatures);
        var change = Assert.Single(_changes);
        Assert.Equal(DynamicSourceChangeKind.Removed, change.Kind);
        Assert.Equal([NmeaWeatherSource.FeatureId], change.ChangedIds);
    }

    [Fact]
    public void Changes_are_published_at_most_once_a_publish_interval()
    {
        var source = Create();
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, Angle.FromDegrees(90), null, Speed.Zero, Start));
        Feed(source, "IIMTW,12.4,C");
        Feed(source, "WIMWV,045.0,R,12.0,N,A", "WIMWV,046.0,R,12.0,N,A", "WIMWV,047.0,R,12.0,N,A");

        // The first value is published at once; the rest wait for the interval.
        var added = Assert.Single(_changes);
        Assert.Equal(DynamicSourceChangeKind.Added, added.Kind);
        Assert.Null(Weather(source).RelativeWindAngle);

        _time.Advance(TimeSpan.FromSeconds(1));
        Feed(source, "WIMWV,048.0,R,12.0,N,A");

        Assert.Equal(2, _changes.Count);
        Assert.Equal(DynamicSourceChangeKind.Updated, _changes[1].Kind);
        Assert.Equal(48.0, Weather(source).RelativeWindAngle!.Value.TotalDegrees);
    }

    [Fact]
    public async Task Started_the_timer_publishes_pending_changes_and_ages_values()
    {
        var source = Create();
        source.UpdateOwnShip(new OwnShipReference(RmcPosition, null, null, null, Start));
        Feed(source, "IIMTW,12.4,C");
        Feed(source, "IIMTW,12.6,C");
        Assert.Equal(12.4, Weather(source).WaterTemperatureCelsius);

        source.Start();
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitFor(() => Weather(source).WaterTemperatureCelsius == 12.6);

        _time.Advance(TimeSpan.FromSeconds(61));
        await WaitFor(() => source.CurrentFeatures.Count == 0);
    }

    [Fact]
    public async Task Started_the_source_reads_its_input_and_retries_after_a_failure()
    {
        var input = new FlakyInput(failures: 1, FixturePath("ownship-weather.nmea"));
        var source = Create(input);

        source.Start();
        await WaitFor(() => input.Attempts == 1 && source.LastError is not null);
        Assert.Empty(source.CurrentFeatures);

        _time.Advance(source.Options.RetryDelay);
        await WaitFor(
            () => source.CurrentFeatures is [var f] && NmeaWeatherSource.GetWeather(f)?.TrueWindSource == TrueWindSource.Mwd,
            advance: TimeSpan.FromSeconds(1));
        Assert.Equal(2, input.Attempts);
    }

    [Fact]
    public async Task Reading_a_replay_to_the_end_closes_the_file()
    {
        await Replay("apparent-wind.nmea");

        using (new FileStream(FixturePath("apparent-wind.nmea"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task Reading_without_an_input_is_refused()
    {
        var source = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pick_lists_every_value_with_its_unit()
    {
        var source = await Replay("ownship-weather.nmea");
        _time.Advance(TimeSpan.FromSeconds(5));

        var description = source.Describe(Assert.Single(source.CurrentFeatures))!;

        Assert.Equal("Own-ship weather", description.Title);
        var rows = description.Properties.ToDictionary(p => p.Label, p => p.Value);
        Assert.Equal("200° 14.0 kn (7.2 m/s)", rows["True wind"]);
        Assert.Equal("Instruments (MWD)", rows["True wind from"]);
        Assert.Equal("083° starboard 13.8 kn (7.1 m/s)", rows["Relative wind"]);
        Assert.Equal("090° T", rows["Heading"]);
        Assert.Equal("12.4 °C", rows["Water temperature"]);
        Assert.Equal("10.8 °C", rows["Air temperature"]);
        Assert.Equal("1013.2 hPa", rows["Pressure"]);
        Assert.Equal("2026-10-27 14:50:00 UTC (5 s ago)", rows["Updated"]);
        Assert.Contains("Not for navigation", rows["Source"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relative_only_pick_says_why_there_is_no_true_wind()
    {
        var source = await Replay("relative-wind.nmea");

        var rows = source.Describe(Assert.Single(source.CurrentFeatures))!.Properties.ToDictionary(p => p.Label, p => p.Value);

        Assert.False(rows.ContainsKey("True wind"));
        Assert.Equal("046° starboard 12.1 kn (6.2 m/s) (no true wind: heading unknown)", rows["Relative wind"]);
    }

    [Fact]
    public void Options_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NmeaWeatherSource(options: new NmeaWeatherSourceOptions { DropAfter = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NmeaWeatherSource(options: new NmeaWeatherSourceOptions { MaxMotionAge = TimeSpan.Zero }));
        Assert.ThrowsAny<ArgumentException>(() => new NmeaWeatherSource(options: new NmeaWeatherSourceOptions { Id = "" }));
    }

    // Waits for a condition the source's background work makes true,
    // advancing the fake clock a little each time when asked.
    private async Task WaitFor(Func<bool> condition, TimeSpan? advance = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the source.");
            if (advance is { } a)
                _time.Advance(a);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FlakyInput(int failures, string path) : INmeaLineSource
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public string Description => "flaky";

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attempts) <= failures)
                throw new IOException("Port went away (fake).");
            await foreach (var line in new NmeaReplayFile(path).ReadLinesAsync(cancellationToken))
                yield return line;
        }
    }
}
