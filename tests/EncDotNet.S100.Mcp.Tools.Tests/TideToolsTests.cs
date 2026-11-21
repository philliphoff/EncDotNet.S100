using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// list_tide_stations and predict_tide (#1452) over NOAA's Puget Sound
/// stations (tests/datasets/Tides/Stations/puget-sound.json, retrieved
/// 2026-11-12): Seattle 9447130 and Port Townsend 9444900 (harmonic), and
/// Tacoma Narrows 9446486, Port Ludlow 9445017 and Marrowstone Point 9444972
/// (subordinate).
/// </summary>
public sealed class TideToolsTests
{
    private const string Seattle = "9447130";
    private const string TacomaNarrows = "9446486";
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 22, 7, 0, TimeSpan.Zero);

    private static string CataloguePath(string name = "puget-sound.json") =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", name);

    private static TideCatalogueFileSource Source(string? path = null) => new(path ?? CataloguePath(), "Get one.");

    private static PredictTideRequest Predict(
        string? station = Seattle,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        TimeSpan? step = null,
        TidePredictionMode mode = TidePredictionMode.Series,
        string? datum = null,
        double? latitude = null,
        double? longitude = null,
        int maxPoints = 2000) =>
        new(station, latitude, longitude, null, from, to, step, mode, datum, maxPoints);

    [Fact]
    public async Task Stations_near_Seattle_are_nearest_first_with_kind_reference_and_datums()
    {
        var tool = new ListTideStationsTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(new ListTideStationsRequest(47.6, -122.34, null, null, null), TestContext.Current.CancellationToken));

        Assert.Equal([Seattle, TacomaNarrows, "9445017"], result.Stations.Select(s => s.Id));
        var seattle = result.Stations[0];
        Assert.Equal("harmonic", seattle.Kind);
        Assert.Equal("tide", seattle.Type);
        Assert.Equal("MLLW", seattle.DatumsReference);
        Assert.Equal(0, seattle.Datums!["MLLW"]);
        Assert.InRange(seattle.Datums["MHHW"], 3.3, 3.6);
        Assert.Contains("LAT", seattle.PredictableDatums);
        Assert.Null(seattle.Offsets);

        var tacoma = result.Stations[1];
        Assert.Equal("subordinate", tacoma.Kind);
        Assert.Equal(Seattle, tacoma.ReferenceStationId);
        Assert.Equal(["MLLW"], tacoma.PredictableDatums);
        Assert.Equal("ratio", tacoma.Offsets!.HeightAdjustment);
        Assert.NotNull(tacoma.Offsets.HighFactor);
        Assert.Null(tacoma.Offsets.HighOffsetMeters);
        Assert.InRange(tacoma.DistanceMeters, 35_000, 45_000);

        Assert.Equal("NOAA CO-OPS", result.Catalogue.Provider);
        Assert.Equal(0, result.Catalogue.AgeDays);
        Assert.False(result.Catalogue.Stale);
        Assert.Equal(5, result.Catalogue.StationCount);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Stations_respect_the_radius_and_limit()
    {
        var tool = new ListTideStationsTool(Source(), new FixedTime(Now));

        var near = Ok(await tool.InvokeAsync(new ListTideStationsRequest(47.6, -122.34, 5_000, null, null), TestContext.Current.CancellationToken));
        var limited = Ok(await tool.InvokeAsync(new ListTideStationsRequest(47.6, -122.34, 500_000, null, 2), TestContext.Current.CancellationToken));

        Assert.Equal([Seattle], near.Stations.Select(s => s.Id));
        Assert.Equal(2, limited.Stations.Count);
        Assert.True(limited.Truncated);
    }

    [Fact]
    public async Task Stations_in_a_box_are_the_ones_inside_it()
    {
        var tool = new ListTideStationsTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(
            new ListTideStationsRequest(null, null, null, new BoundingBox(47.9, -123.0, 48.2, -122.5), null),
            TestContext.Current.CancellationToken));

        Assert.Equal(["9444900", "9444972", "9445017"], result.Stations.Select(s => s.Id).Order());
    }

    [Fact]
    public async Task A_year_old_catalogue_is_stale()
    {
        var tool = new ListTideStationsTool(Source(), new FixedTime(Now.AddDays(400)));

        var result = Ok(await tool.InvokeAsync(new ListTideStationsRequest(47.6, -122.34, null, null, null), TestContext.Current.CancellationToken));

        Assert.True(result.Catalogue.Stale);
        Assert.Equal(400, result.Catalogue.AgeDays);
    }

    [Fact]
    public async Task No_catalogue_says_how_to_get_one()
    {
        var tool = new ListTideStationsTool(Source(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")), new FixedTime(Now));

        var result = await tool.InvokeAsync(new ListTideStationsRequest(47.6, -122.34, null, null, null), TestContext.Current.CancellationToken);

        var error = Assert.IsType<TideCatalogueNotAvailable>(Error(result));
        Assert.Equal("tide_catalogue_not_available", error.Code);
        Assert.Equal("Get one.", error.Hint);
    }

    [Fact]
    public async Task Stations_need_a_point_or_a_box()
    {
        var tool = new ListTideStationsTool(Source(), new FixedTime(Now));

        var result = await tool.InvokeAsync(new ListTideStationsRequest(47.6, null, null, null, null), TestContext.Current.CancellationToken);

        Assert.IsType<InvalidArgument>(Error(result));
    }

    [Fact]
    public async Task A_series_matches_the_station_predictor_on_MLLW()
    {
        var catalogue = await TideStationCatalogueFile.LoadAsync(CataloguePath(), TestContext.Current.CancellationToken);
        var expected = catalogue.TryCreatePrediction(Seattle, VerticalDatum.MeanLowerLowWater)!.Predictor;
        var tool = new PredictTideTool(Source(), new FixedTime(Now));
        var from = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(-8));

        var result = Ok(await tool.InvokeAsync(
            Predict(from: from, to: from.AddHours(6), step: TimeSpan.FromMinutes(10)),
            TestContext.Current.CancellationToken));

        Assert.Equal(37, result.Series!.Count);
        Assert.Null(result.Extremes);
        Assert.Equal(600, result.StepSeconds);
        Assert.Equal(from, result.Series[0].Time);
        Assert.Equal(TimeSpan.Zero, result.Series[0].Time.Offset);
        foreach (var point in result.Series)
            Assert.Equal(expected.HeightAt(point.Time.UtcDateTime), point.Height, 3);

        Assert.Equal("prediction", result.Kind);
        Assert.Equal("MLLW", result.Datum);
        Assert.Equal(12, result.VerticalDatum);
        Assert.Equal("harmonic", result.Station.Kind);
        Assert.Null(result.Station.DistanceMeters);
        Assert.Null(result.Method);
        Assert.Equal("Astronomical tide only, not a forecast.", result.Caveat);
        Assert.Equal("NOAA CO-OPS", result.Provider);
    }

    [Fact]
    public async Task Highs_and_lows_alternate_and_are_to_the_minute()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));
        var from = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

        var result = Ok(await tool.InvokeAsync(
            Predict(from: from, to: from.AddDays(2), mode: TidePredictionMode.Extremes),
            TestContext.Current.CancellationToken));

        Assert.Null(result.Series);
        Assert.Null(result.StepSeconds);
        Assert.InRange(result.Extremes!.Count, 6, 9);
        for (var i = 1; i < result.Extremes.Count; i++)
            Assert.NotEqual(result.Extremes[i - 1].Kind, result.Extremes[i].Kind);
        Assert.All(result.Extremes, e => Assert.Equal(0, e.Time.Second));
        Assert.All(result.Extremes, e => Assert.InRange(e.Time, from, from.AddDays(2)));
    }

    [Fact]
    public async Task A_subordinate_station_by_name_says_where_its_heights_come_from()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(Predict(station: "tacoma", mode: TidePredictionMode.Both), TestContext.Current.CancellationToken));

        Assert.Equal(TacomaNarrows, result.Station.Id);
        Assert.Equal("subordinate", result.Station.Kind);
        Assert.Equal(Seattle, result.Station.ReferenceStationId);
        Assert.Equal("from SEATTLE (Madison St.), Elliott Bay 9447130, with offsets", result.Method);
        Assert.NotNull(result.Series);
        Assert.NotNull(result.Extremes);
    }

    [Fact]
    public async Task The_default_window_starts_now_rounded_down_to_the_step()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(Predict(), TestContext.Current.CancellationToken));

        Assert.Equal(new DateTimeOffset(2026, 11, 12, 22, 0, 0, TimeSpan.Zero), result.From);
        Assert.Equal(result.From.AddHours(24), result.To);
        Assert.Equal(49, result.Series!.Count);
    }

    [Fact]
    public async Task A_point_uses_the_nearest_station_predictable_on_the_datum()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));

        var mllw = Ok(await tool.InvokeAsync(Predict(station: null, latitude: 47.27, longitude: -122.55), TestContext.Current.CancellationToken));
        var lat = await tool.InvokeAsync(Predict(station: null, latitude: 47.27, longitude: -122.55, datum: "LAT"), TestContext.Current.CancellationToken);

        Assert.Equal(TacomaNarrows, mllw.Station.Id);
        Assert.InRange(mllw.Station.DistanceMeters!.Value, 0, 1_000);
        // The subordinate station is MLLW only, and Seattle is 40 km away: past the 10 km reach.
        var error = Assert.IsType<NoTideStationNear>(Error(lat));
        Assert.Equal("LAT", error.Datum);
    }

    [Fact]
    public async Task A_datum_the_station_lacks_lists_the_ones_it_has()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));

        var result = await tool.InvokeAsync(Predict(station: TacomaNarrows, datum: "lat"), TestContext.Current.CancellationToken);

        var error = Assert.IsType<TideDatumNotAvailable>(Error(result));
        Assert.Equal(["MLLW"], error.Available);
    }

    [Fact]
    public async Task An_unknown_or_ambiguous_station_is_not_found()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));

        var unknown = await tool.InvokeAsync(Predict(station: "Nowhere"), TestContext.Current.CancellationToken);
        var ambiguous = await tool.InvokeAsync(Predict(station: "port"), TestContext.Current.CancellationToken);

        Assert.Empty(Assert.IsType<TideStationNotFound>(Error(unknown)).Candidates);
        Assert.Equal(["9445017 Port Ludlow", "9444900 PORT TOWNSEND"], Assert.IsType<TideStationNotFound>(Error(ambiguous)).Candidates);
    }

    [Fact]
    public async Task Too_many_heights_and_bad_windows_are_refused()
    {
        var tool = new PredictTideTool(Source(), new FixedTime(Now));
        var from = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

        var tooMany = await tool.InvokeAsync(Predict(from: from, to: from.AddDays(30), step: TimeSpan.FromMinutes(1)), TestContext.Current.CancellationToken);
        var backwards = await tool.InvokeAsync(Predict(from: from, to: from.AddHours(-1)), TestContext.Current.CancellationToken);
        var badDatum = await tool.InvokeAsync(Predict(datum: "NAVD88"), TestContext.Current.CancellationToken);

        Assert.Equal("stepMinutes", Assert.IsType<InvalidArgument>(Error(tooMany)).Parameter);
        Assert.Equal("to", Assert.IsType<InvalidArgument>(Error(backwards)).Parameter);
        Assert.Equal("datum", Assert.IsType<InvalidArgument>(Error(badDatum)).Parameter);
    }

    [Fact]
    public async Task The_file_source_reloads_a_changed_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.Copy(CataloguePath("example-stations.json"), path);
            var source = Source(path);
            var first = await source.GetAsync(TestContext.Current.CancellationToken);
            Assert.Same(first, await source.GetAsync(TestContext.Current.CancellationToken));

            File.Copy(CataloguePath(), path, overwrite: true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var second = await source.GetAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, first!.Stations.Count);
            Assert.Equal(5, second!.Stations.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static T Ok<T>(ToolResult<T> result)
    {
        if (result.TryGetError(out var error))
            Assert.Fail($"{error.Code}: {error.Message}");
        result.TryGetValue(out var value);
        return value!;
    }

    private static ToolError Error<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetError(out var error), "Expected an error.");
        return error!;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
