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

        // 2026 10 28 23 40 180  3.0  4.0    MM    MM    MM  MM 1029.7  11.6  13.2   6.2   MM   MM    MM
        var newest = rows[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 23, 40, 0, TimeSpan.Zero), newest.Time);
        Assert.Equal(180, newest.WindFromDirection!.Value.TotalDegrees, 6);
        Assert.Equal(3.0, newest.WindSpeed!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(4.0, newest.WindGust!.Value.TotalMetresPerSecond, 6);
        Assert.Null(newest.WaveHeight);
        Assert.Null(newest.DominantWavePeriodSeconds);
        Assert.Null(newest.MeanWaveDirection);
        Assert.Equal(1029.7, newest.PressureHectopascals);
        Assert.Equal(11.6, newest.AirTemperatureCelsius);
        Assert.Equal(13.2, newest.WaterTemperatureCelsius);
        Assert.Equal(6.2, newest.DewPointCelsius);
        Assert.Null(newest.VisibilityNauticalMiles);
        Assert.Null(newest.PressureTendencyHectopascals);
        Assert.Null(newest.TideFeet);

        // The newest wave report, at :20 (waves come at :20 and :50).
        // 2026 10 28 23 20 180  3.0  4.0   0.5     9   5.6  96 1029.9  11.5  13.2   6.0   MM   MM    MM
        var waves = rows[1];
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 23, 20, 0, TimeSpan.Zero), waves.Time);
        Assert.Equal(0.5, waves.WaveHeight!.Value.TotalMetres, 6);
        Assert.Equal(9, waves.DominantWavePeriodSeconds);
        Assert.Equal(5.6, waves.AverageWavePeriodSeconds);
        Assert.Equal(96, waves.MeanWaveDirection!.Value.TotalDegrees, 6);
    }

    [Fact]
    public void Missing_values_are_null()
    {
        var rows = NdbcRealtimeParser.Parse(NdbcFixtures.ReadText("44013.txt"), NdbcFixtures.Buoy);

        // Waves are half-hourly, so most rows have MM for them.
        Assert.Contains(rows, r => r.WaveHeight is null && r.WindSpeed is not null);
        // The 22:30 row lost its water temperature.
        var gap = Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 28, 22, 30, 0, TimeSpan.Zero));
        Assert.Null(gap.WaterTemperatureCelsius);
        Assert.Equal(11.0, gap.AirTemperatureCelsius);
        // The 09:10 row has a wave height but no dominant period.
        var partial = Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 28, 9, 10, 0, TimeSpan.Zero));
        Assert.Equal(0.9, partial.WaveHeight!.Value.TotalMetres, 6);
        Assert.Null(partial.DominantWavePeriodSeconds);
        Assert.Equal(3.8, partial.AverageWavePeriodSeconds);
        // The pressure tendency is on the hour, with an explicit sign even at zero.
        Assert.Equal(0.0, Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 28, 23, 0, 0, TimeSpan.Zero)).PressureTendencyHectopascals);
        Assert.Equal(-0.4, Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 28, 22, 0, 0, TimeSpan.Zero)).PressureTendencyHectopascals);
        // The 23:30 report is missing from the file altogether.
        Assert.DoesNotContain(rows, r => r.Time == new DateTimeOffset(2026, 10, 28, 23, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Cman_file_has_wind_and_pressure_but_no_waves()
    {
        var rows = NdbcRealtimeParser.Parse(NdbcFixtures.ReadText("BUZM3.txt"), NdbcFixtures.Cman);

        // The C-MAN station reports hourly.
        Assert.Equal(24, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.Time.Minute));
        Assert.All(rows, r => Assert.Null(r.WaveHeight));
        Assert.All(rows, r => Assert.Null(r.MeanWaveDirection));
        Assert.All(rows, r => Assert.Null(r.WaterTemperatureCelsius));
        Assert.Contains(rows, r => r.WindSpeed is not null && r.PressureHectopascals is not null);

        // 2026 10 28 23 00 200  5.1  6.2    MM    MM    MM  MM 1030.6    MM    MM    MM   MM +0.0    MM
        var newest = rows[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 23, 0, 0, TimeSpan.Zero), newest.Time);
        Assert.Equal(200, newest.WindFromDirection!.Value.TotalDegrees, 6);
        Assert.Equal(5.1, newest.WindSpeed!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(6.2, newest.WindGust!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(1030.6, newest.PressureHectopascals);
        Assert.Null(newest.AirTemperatureCelsius);
        Assert.Equal(0.0, newest.PressureTendencyHectopascals);

        // In a calm the direction is MM and the speed 0.0.
        // 2026 10 28 18 00  MM  0.0  0.5    MM    MM    MM  MM 1031.4    MM    MM    MM   MM -1.9    MM
        var calm = Assert.Single(rows, r => r.Time == new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero));
        Assert.Null(calm.WindFromDirection);
        Assert.Equal(0.0, calm.WindSpeed!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(0.5, calm.WindGust!.Value.TotalMetresPerSecond, 6);
        Assert.Equal(-1.9, calm.PressureTendencyHectopascals);
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
