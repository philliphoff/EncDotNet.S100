namespace EncDotNet.S100.Tides.Tests;

/// <summary>
/// Compares the predictor with NOAA's own published predictions for five
/// stations of different tide types over 2026.
/// </summary>
public sealed class NoaaComparisonTests(ITestOutputHelper output)
{
    private static readonly DateTime YearStart = new(NoaaFixture.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [MemberData(nameof(NoaaFixture.StationIds), MemberType = typeof(NoaaFixture))]
    public void SixMinuteHeights_MatchNoaaWithinTwoMillimetresRms(string id)
    {
        var expected = NoaaFixture.MslSixMinute(id);
        var predictor = new TidePredictor(NoaaFixture.Station(id));
        var actual = new double[expected.Length];
        predictor.Predict(YearStart, TimeSpan.FromMinutes(6), actual);

        var sumSquares = 0.0;
        var worst = 0.0;
        for (var k = 0; k < expected.Length; k++)
        {
            var error = actual[k] - expected[k];
            sumSquares += error * error;
            worst = Math.Max(worst, Math.Abs(error));
        }

        var rms = Math.Sqrt(sumSquares / expected.Length);
        output.WriteLine($"{id}: {expected.Length} samples, RMS {rms:F4} m, worst {worst:F4} m");
        Assert.Equal(24 * 10 * 365, expected.Length);
        // NOAA rounds its heights to the millimetre; the issue asks for 2 cm.
        Assert.True(rms < 0.002, $"RMS {rms:F4} m");
    }

    [Theory]
    [MemberData(nameof(NoaaFixture.StationIds), MemberType = typeof(NoaaFixture))]
    public void HighsAndLows_MatchNoaaWithinOneMinuteAndTwoMillimetres(string id)
    {
        var expected = NoaaFixture.MslHighsAndLows(id);
        var predictor = new TidePredictor(NoaaFixture.Station(id));
        var actual = predictor.FindExtremes(YearStart, YearStart.AddYears(1));

        // Pair each of NOAA's events with ours of the same kind nearest in time.
        var worstMinutes = 0.0;
        var worstMeters = 0.0;
        foreach (var noaa in expected)
        {
            var match = actual
                .Where(e => e.Kind == noaa.Kind)
                .MinBy(e => Math.Abs((e.Time - noaa.Time).Ticks));
            var minutes = Math.Abs((match.Time - noaa.Time).TotalMinutes);
            var meters = Math.Abs(match.HeightMeters - noaa.HeightMeters);
            worstMinutes = Math.Max(worstMinutes, minutes);
            worstMeters = Math.Max(worstMeters, meters);
            // NOAA rounds its times to the minute; the issue asks for 2 minutes and 2 cm.
            Assert.True(minutes <= 1, $"{noaa.Kind} at {noaa.Time:u}: ours at {match.Time:u}, {minutes:F1} min apart");
            Assert.True(meters <= 0.002, $"{noaa.Kind} at {noaa.Time:u}: {noaa.HeightMeters:F3} m, ours {match.HeightMeters:F3} m");
        }

        output.WriteLine($"{id}: NOAA {expected.Count} events, ours {actual.Count}; worst {worstMinutes:F2} min, {worstMeters:F4} m");
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 1; i < actual.Count; i++)
        {
            Assert.NotEqual(actual[i - 1].Kind, actual[i].Kind);
        }
    }

    [Theory]
    [MemberData(nameof(NoaaFixture.StationIds), MemberType = typeof(NoaaFixture))]
    public void HeightsOnMllw_MatchNoaa(string id)
    {
        var expected = NoaaFixture.MllwJanuary(id);
        var predictor = new TidePredictor(NoaaFixture.StationOnMllw(id));

        var sumSquares = expected.Sum(e => Math.Pow(predictor.HeightAt(e.Time) - e.Height, 2));
        var rms = Math.Sqrt(sumSquares / expected.Count);
        output.WriteLine($"{id}: RMS {rms:F4} m on MLLW");
        Assert.True(rms < 0.002, $"RMS {rms:F4} m");
    }

    [Theory]
    [MemberData(nameof(NoaaFixture.StationIds), MemberType = typeof(NoaaFixture))]
    public void Speeds_MatchNoaa(string id)
    {
        foreach (var row in NoaaFixture.Harcon(id))
        {
            var speed = HarmonicConstituents.TryGetSpeed(row.Name);
            Assert.NotNull(speed);
            // NOAA gives some speeds to six decimals, some to seven.
            Assert.True(Math.Abs(speed.Value - row.Speed) < 2e-5, $"{row.Name}: {speed:F7} vs NOAA {row.Speed:F7}");
        }
    }

    [Theory]
    [MemberData(nameof(NoaaFixture.StationIds), MemberType = typeof(NoaaFixture))]
    public void LocalPhases_ConvertToNoaaGreenwichPhases(string id)
    {
        var offset = TimeSpan.FromHours(NoaaFixture.Stations.Single(s => s.Id == id).UtcOffsetHours);
        // NOAA lists a phase of 0 for constituents it didn't resolve (zero amplitude).
        foreach (var row in NoaaFixture.Harcon(id).Where(r => r.Amplitude > 0))
        {
            var constituent = TideConstituent.FromLocalPhase(row.Name, row.Amplitude, row.PhaseLocal, offset);

            // NOAA rounds both phases to 0.1°.
            var difference = Math.Abs(constituent.PhaseDegrees - row.PhaseGmt) % 360;
            difference = Math.Min(difference, 360 - difference);
            Assert.True(difference <= 0.11, $"{row.Name}: {constituent.PhaseDegrees:F2}° vs NOAA {row.PhaseGmt:F1}°");
        }
    }
}
