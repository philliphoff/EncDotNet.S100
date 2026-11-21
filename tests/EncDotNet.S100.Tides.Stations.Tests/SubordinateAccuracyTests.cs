using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations.Tests;

/// <summary>
/// Compares subordinate stations' highs and lows with NOAA's published ones
/// for 2026: Tacoma Narrows Bridge and Port Ludlow (ratios, from Seattle)
/// and Marrowstone Point (fixed offsets, from Port Townsend).
/// </summary>
public sealed class SubordinateAccuracyTests(ITestOutputHelper output)
{
    private static readonly DateTime YearStart = new(Fixtures.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Lazy<TideStationCatalogue> Catalogue = new(Fixtures.PugetSoundCatalogue);

    [Theory]
    [InlineData(Fixtures.TacomaNarrows)]
    [InlineData(Fixtures.PortLudlow)]
    [InlineData(Fixtures.MarrowstonePoint)]
    public void HighsAndLows_MatchNoaaWithinThreeMinutesAndThreeCentimetres(string id)
    {
        var expected = Fixtures.MllwHighsAndLows(id);
        var prediction = Catalogue.Value.TryCreatePrediction(id, VerticalDatum.MeanLowerLowWater);
        Assert.NotNull(prediction);

        // A little either side of the year, so the first and last are found.
        var actual = prediction.Predictor.FindExtremes(YearStart.AddHours(-6), YearStart.AddYears(1).AddHours(6));

        var worstMinutes = 0.0;
        var worstMeters = 0.0;
        var sumSquares = 0.0;
        foreach (var e in expected)
        {
            var match = actual
                .Where(a => a.Kind == e.Kind)
                .MinBy(a => Math.Abs((a.Time - e.Time).Ticks));
            var minutes = Math.Abs((match.Time - e.Time).TotalMinutes);
            var meters = Math.Abs(match.HeightMeters - e.HeightMeters);
            worstMinutes = Math.Max(worstMinutes, minutes);
            worstMeters = Math.Max(worstMeters, meters);
            sumSquares += meters * meters;
            Assert.True(minutes <= 3, $"{e.Kind} {e.Time:u}: predicted {match.Time:u}, {minutes:F1} min off");
            Assert.True(meters <= 0.03, $"{e.Kind} {e.Time:u}: predicted {match.HeightMeters:F3} m, NOAA {e.HeightMeters:F3} m");
        }

        var inYear = actual.Count(a => a.Time >= YearStart && a.Time < YearStart.AddYears(1));
        output.WriteLine(
            $"{id}: {expected.Count} NOAA highs and lows, {inYear} predicted; worst {worstMinutes:F1} min, "
            + $"{worstMeters * 1000:F1} mm; RMS {Math.Sqrt(sumSquares / expected.Count) * 1000:F1} mm");

        foreach (var a in actual.Where(a => a.Time >= YearStart && a.Time < YearStart.AddYears(1)))
        {
            if (!expected.Any(e => e.Kind == a.Kind && Math.Abs((e.Time - a.Time).TotalMinutes) <= 3))
            {
                output.WriteLine($"  extra {a.Kind} {a.Time:u} {a.HeightMeters:F3}");
            }
        }

        // Every NOAA high and low has its own prediction: none are missing or extra.
        Assert.Equal(expected.Count, inYear);
    }

    [Fact]
    public void Curve_PassesThroughTheHighsAndLows_AndStaysBetweenThem()
    {
        var prediction = Catalogue.Value.TryCreatePrediction(Fixtures.TacomaNarrows, VerticalDatum.MeanLowerLowWater)!;
        var predictor = prediction.Predictor;
        var extremes = predictor.FindExtremes(YearStart, YearStart.AddDays(7));
        foreach (var extreme in extremes)
        {
            Assert.Equal(extreme.HeightMeters, predictor.HeightAt(extreme.Time), 6);
        }

        var series = new double[7 * 24 * 10];
        predictor.Predict(YearStart, TimeSpan.FromMinutes(6), series);
        for (var k = 0; k < series.Length; k++)
        {
            var time = YearStart.AddMinutes(6 * k);
            Assert.Equal(predictor.HeightAt(time), series[k], 9);
            var before = extremes.LastOrDefault(e => e.Time <= time);
            var after = extremes.FirstOrDefault(e => e.Time > time);
            if (before != default && after != default)
            {
                var low = Math.Min(before.HeightMeters, after.HeightMeters) - 0.01;
                var high = Math.Max(before.HeightMeters, after.HeightMeters) + 0.01;
                Assert.InRange(series[k], low, high);
            }
        }
    }
}
