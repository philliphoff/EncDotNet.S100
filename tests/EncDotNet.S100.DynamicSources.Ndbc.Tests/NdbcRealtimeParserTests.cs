namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

public sealed class NdbcRealtimeParserTests
{
    private const string Header =
        "#YY  MM DD hh mm WDIR WSPD GST  WVHT   DPD   APD MWD   PRES  ATMP  WTMP  DEWP  VIS PTDY  TIDE\n" +
        "#yr  mo dy hr mn degT m/s  m/s     m   sec   sec degT   hPa  degC  degC  degC  nmi  hPa    ft\n";

    [Fact]
    public void Buoy_file_reads_every_row_newest_first()
    {
        var rows = NdbcRealtimeParser.Parse(NdbcFixtures.ReadText("44013.txt"), NdbcFixtures.Buoy);

        Assert.True(rows.Count >= 24, $"Expected a day of rows, got {rows.Count}.");
        Assert.All(rows, r => Assert.Equal(NdbcFixtures.Buoy, r.StationId));
        Assert.All(rows, r => Assert.Equal(TimeSpan.Zero, r.Time.Offset));
        for (int i = 1; i < rows.Count; i++)
            Assert.True(rows[i].Time < rows[i - 1].Time, $"Row {i} isn't older than row {i - 1}.");
    }

    [Fact]
    public void Buoy_file_reads_wind_waves_pressure_and_temperatures()
    {
        var rows = NdbcRealtimeParser.Parse(NdbcFixtures.ReadText("44013.txt"), NdbcFixtures.Buoy);

        // 2026 10 27 14 50 230   7.4   8.9   1.1     6   4.6 210 1016.2  11.8  13.1   6.4   MM   MM    MM
        var newest = rows[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 14, 50, 0, TimeSpan.Zero), newest.Time);
        Assert.Equal(230, newest.WindFromDirection!.Value.TotalDegrees, 6);
        Assert.Equal(7.4, newest.WindSpeed!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(8.9, newest.WindGust!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(1.1, newest.WaveHeight!.Value.TotalMetres, 6);
        Assert.Equal(6, newest.DominantWavePeriodSeconds);
        Assert.Equal(4.6, newest.AverageWavePeriodSeconds);
        Assert.Equal(210, newest.MeanWaveDirection!.Value.TotalDegrees, 6);
        Assert.Equal(1016.2, newest.PressureHectopascals);
        Assert.Equal(11.8, newest.AirTemperatureCelsius);
        Assert.Equal(13.1, newest.WaterTemperatureCelsius);
        Assert.Equal(6.4, newest.DewPointCelsius);
        Assert.Null(newest.VisibilityNauticalMiles);
        Assert.Null(newest.PressureTendencyHectopascals);
        Assert.Null(newest.TideFeet);
    }

    [Fact]
    public void Missing_values_are_null()
    {
        var rows = NdbcRealtimeParser.Parse(NdbcFixtures.ReadText("44013.txt"), NdbcFixtures.Buoy);

        // Waves are hourly, so most rows have MM for them.
        Assert.Contains(rows, r => r.WaveHeight is null && r.WindSpeed is not null);
        // The 13:40 row lost its wind report.
        var gap = Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 27, 13, 40, 0, TimeSpan.Zero));
        Assert.Null(gap.WindFromDirection);
        Assert.Null(gap.WindSpeed);
        Assert.Null(gap.WindGust);
        Assert.Equal(1016.0, gap.PressureHectopascals);
        // The pressure tendency is on the hour, signed.
        Assert.Equal(-0.8, Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.Zero)).PressureTendencyHectopascals);
    }

    [Fact]
    public void Cman_file_has_wind_and_pressure_but_no_waves()
    {
        var rows = NdbcRealtimeParser.Parse(NdbcFixtures.ReadText("BUZM3.txt"), NdbcFixtures.Cman);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Null(r.WaveHeight));
        Assert.All(rows, r => Assert.Null(r.MeanWaveDirection));
        Assert.Contains(rows, r => r.WindSpeed is not null && r.PressureHectopascals is not null);

        // 2026 10 27 15 00 254   9.0  11.4    MM    MM    MM  MM 1015.8  10.9    MM   5.2   MM -1.1    MM
        var newest = rows[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero), newest.Time);
        Assert.Equal(254, newest.WindFromDirection!.Value.TotalDegrees, 6);
        Assert.Equal(9.0, newest.WindSpeed!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(11.4, newest.WindGust!.Value.TotalMetresPerSecond, 6);
        Assert.Null(newest.WaterTemperatureCelsius);
        Assert.Equal(-1.1, newest.PressureTendencyHectopascals);
    }

    [Fact]
    public void A_cut_off_last_line_is_skipped()
    {
        string text = Header +
            "2026 10 27 14 50 230   7.4   8.9   1.1     6   4.6 210 1016.2  11.8  13.1   6.4   MM   MM    MM\n" +
            "2026 10 27 14 40 234   7.0   8.8    MM    M";

        var rows = NdbcRealtimeParser.Parse(text, "44013");

        Assert.Single(rows);
    }

    [Fact]
    public void Columns_are_found_by_name_including_historical_names()
    {
        string text =
            "#YYYY MM DD hh WD   WSPD GST  BAR    WVHT\n" +
            "#yr   mo dy hr degT m/s  m/s  hPa    m\n" +
            "2026  10 27 14 180  5.0  6.0  1012.5 0.8\n";

        var row = Assert.Single(NdbcRealtimeParser.Parse(text, "TEST1"));

        Assert.Equal(new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.Zero), row.Time);
        Assert.Equal(180, row.WindFromDirection!.Value.TotalDegrees, 6);
        Assert.Equal(1012.5, row.PressureHectopascals);
        Assert.Equal(0.8, row.WaveHeight!.Value.TotalMetres, 6);
        Assert.Null(row.AirTemperatureCelsius);
    }

    [Fact]
    public void Reading_stops_at_the_first_row_older_than_the_limit()
    {
        var newest = NdbcFixtures.NewestTime(NdbcFixtures.Buoy);
        using var reader = new StringReader(NdbcFixtures.ReadText("44013.txt"));

        var rows = NdbcRealtimeParser.Parse(reader, NdbcFixtures.Buoy, notBefore: newest - TimeSpan.FromHours(1));

        Assert.All(rows, r => Assert.True(r.Time >= newest - TimeSpan.FromHours(1)));
        Assert.InRange(rows.Count, 1, 7);
    }

    [Theory]
    [InlineData("<html><body>Not here</body></html>\n")]
    [InlineData("")]
    public void A_file_without_a_header_is_rejected(string text)
    {
        Assert.Throws<FormatException>(() => NdbcRealtimeParser.Parse(text, "44013"));
    }

    [Fact]
    public void Combine_takes_older_wave_values_within_the_window()
    {
        string text = Header +
            "2026 10 27 15 00 240   8.0   9.5    MM    MM    MM  MM 1016.0    MM    MM    MM   MM -0.6    MM\n" +
            "2026 10 27 14 50  MM    MM    MM   1.4     7   5.0 200 1016.1  11.5  13.0   6.0   MM   MM    MM\n" +
            "2026 10 27 13 50 230   6.0   7.0   1.9     8   5.5 190 1015.9  11.0  12.9   5.9   MM   MM    MM\n";
        var rows = NdbcRealtimeParser.Parse(text, "44013");

        var combined = NdbcObservation.Combine(rows, TimeSpan.FromHours(1))!;

        Assert.Equal(rows[0].Time, combined.Time);
        Assert.Equal(8.0, combined.WindSpeed!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(240, combined.WindFromDirection!.Value.TotalDegrees, 6);
        Assert.Equal(1.4, combined.WaveHeight!.Value.TotalMetres, 6);
        Assert.Equal(7, combined.DominantWavePeriodSeconds);
        Assert.Equal(200, combined.MeanWaveDirection!.Value.TotalDegrees, 6);
        Assert.Equal(rows[1].Time, combined.WaveTime);
        Assert.Equal(11.5, combined.AirTemperatureCelsius);
        Assert.Equal(1016.0, combined.PressureHectopascals);

        var narrow = NdbcObservation.Combine(rows, TimeSpan.FromMinutes(5))!;
        Assert.Null(narrow.WaveHeight);
        Assert.Null(narrow.AirTemperatureCelsius);
        Assert.Null(NdbcObservation.Combine([], TimeSpan.FromHours(1)));
    }
}
