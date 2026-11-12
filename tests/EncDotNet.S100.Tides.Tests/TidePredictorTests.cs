using System.Diagnostics;

namespace EncDotNet.S100.Tides.Tests;

public sealed class TidePredictorTests(ITestOutputHelper output)
{
    private static readonly DateTime Start = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Predict_MatchesHeightAt_AcrossNewYearAndReseeds()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("9455920"));
        var start = new DateTime(2026, 12, 20, 0, 3, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromMinutes(7);

        // Longer than the re-seed interval, and across 1 January 2027.
        var heights = new double[10_000];
        predictor.Predict(start, step, heights);

        for (var k = 0; k < heights.Length; k += 97)
        {
            Assert.Equal(predictor.HeightAt(start + (step * k)), heights[k], 9);
        }

        Assert.Equal(predictor.HeightAt(start + (step * (heights.Length - 1))), heights[^1], 9);
    }

    [Fact]
    public void Predict_DoesNotAllocate()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("9447130"));
        var heights = new double[7 * 24 * 10];
        predictor.Predict(Start, TimeSpan.FromMinutes(6), heights);

        var before = GC.GetAllocatedBytesForCurrentThread();
        predictor.Predict(Start, TimeSpan.FromMinutes(6), heights);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Predict_AWeekAtSixMinutes_TakesUnderAMillisecond()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("9447130"));
        var heights = new double[7 * 24 * 10];
        for (var i = 0; i < 20; i++)
        {
            predictor.Predict(Start, TimeSpan.FromMinutes(6), heights);
        }

        var samples = new double[50];
        for (var i = 0; i < samples.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            predictor.Predict(Start, TimeSpan.FromMinutes(6), heights);
            samples[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var median = samples[samples.Length / 2];
        output.WriteLine($"Week at 6 minutes, {predictor.Station.Constituents.Count} constituents: median {median:F3} ms, best {samples[0]:F3} ms");
        Assert.True(median < 1.0, $"median {median:F3} ms");
    }

    [Fact]
    public void RateAt_IsTheSlopeOfTheHeight()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("9447130"));
        for (var hour = 0; hour < 48; hour += 5)
        {
            var time = Start.AddHours(hour);
            var delta = TimeSpan.FromSeconds(1);
            var slope = (predictor.HeightAt(time + delta) - predictor.HeightAt(time - delta)) / (2 * delta.TotalHours);
            Assert.Equal(slope, predictor.RateAt(time), 6);
        }
    }

    [Fact]
    public void FindExtremes_AreTurnsOfTheTide()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("9439040"));
        var extremes = predictor.FindExtremes(Start, Start.AddDays(7));

        Assert.InRange(extremes.Count, 26, 28);
        foreach (var extreme in extremes)
        {
            Assert.Equal(DateTimeKind.Utc, extreme.Time.Kind);
            Assert.True(Math.Abs(predictor.RateAt(extreme.Time)) < 0.001);
            var before = predictor.HeightAt(extreme.Time.AddMinutes(-10));
            var after = predictor.HeightAt(extreme.Time.AddMinutes(10));
            if (extreme.Kind == TideExtremeKind.High)
            {
                Assert.True(extreme.HeightMeters > before && extreme.HeightMeters > after);
            }
            else
            {
                Assert.True(extreme.HeightMeters < before && extreme.HeightMeters < after);
            }
        }
    }

    [Fact]
    public void FindExtremes_OfAnEmptyPeriodIsEmpty()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("8443970"));
        Assert.Empty(predictor.FindExtremes(Start, Start));
        Assert.Throws<ArgumentException>(() => predictor.FindExtremes(Start, Start.AddHours(-1)));
    }

    [Fact]
    public void Datum_OffsetsEveryHeight()
    {
        var station = NoaaFixture.Station("9447130");
        var onMsl = new TidePredictor(station);
        var onMllw = new TidePredictor(station.WithDatum(new TideDatum("MLLW", 2.024)));

        Assert.Equal(onMsl.HeightAt(Start) + 2.024, onMllw.HeightAt(Start), 12);
        Assert.Equal(onMsl.RateAt(Start), onMllw.RateAt(Start), 12);
    }

    [Fact]
    public void AStationWithoutConstituents_PredictsItsMean()
    {
        var predictor = new TidePredictor(new TideStation("x", "Lake", [], new TideDatum("LWD", 0.5)));
        var heights = new double[3];
        predictor.Predict(Start, TimeSpan.FromHours(1), heights);

        Assert.Equal(0.5, predictor.HeightAt(Start));
        Assert.All(heights, h => Assert.Equal(0.5, h));
        Assert.Empty(predictor.FindExtremes(Start, Start.AddDays(1)));
    }

    [Fact]
    public void UnspecifiedTimes_AreUtc_AndLocalTimesAreRejected()
    {
        var predictor = new TidePredictor(NoaaFixture.Station("8729840"));
        var unspecified = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified);

        Assert.Equal(predictor.HeightAt(Start), predictor.HeightAt(unspecified));
        Assert.Throws<ArgumentException>(() => predictor.HeightAt(Start.ToLocalTime()));
        Assert.Throws<ArgumentOutOfRangeException>(() => predictor.Predict(Start, TimeSpan.Zero, new double[1]));
    }

    [Fact]
    public void Station_RejectsUnknownAndRepeatedConstituents()
    {
        Assert.Throws<ArgumentException>(() =>
            new TideStation("x", "x", [new TideConstituent("XX9", 1, 0)], TideDatum.MeanSeaLevel));
        Assert.Throws<ArgumentException>(() =>
            new TideStation("x", "x", [new TideConstituent("M2", 1, 0), new TideConstituent("m2", 1, 0)], TideDatum.MeanSeaLevel));
        Assert.Throws<ArgumentException>(() =>
            new TideStation("x", "x", [new TideConstituent("M2", double.NaN, 0)], TideDatum.MeanSeaLevel));
    }

    [Fact]
    public void HarmonicConstituents_KnowNoaasNamesAndOtherSpellings()
    {
        Assert.Equal(120, HarmonicConstituents.Names.Count);
        Assert.Equal("M2", HarmonicConstituents.Names[0]);
        Assert.Equal("MS4", HarmonicConstituents.Names[36]);
        Assert.True(HarmonicConstituents.Contains("rho1"));
        Assert.True(HarmonicConstituents.Contains("LAMBDA2"));
        Assert.False(HarmonicConstituents.Contains("XX9"));
        Assert.Equal(28.9841042, HarmonicConstituents.TryGetSpeed("M2")!.Value, 6);
        Assert.Null(HarmonicConstituents.TryGetSpeed("XX9"));
    }

    [Fact]
    public void HeightAt_ForASingleConstituent_IsItsCosine()
    {
        // A pure S2 tide: no nodal terms, V = 2T, so at Greenwich midnight
        // (T = 180°) the argument is 360° − G.
        var predictor = new TidePredictor(new TideStation(
            "x", "x", [new TideConstituent("S2", 1.0, 30)], TideDatum.MeanSeaLevel));
        var midnight = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(Math.Cos(-30 * Math.PI / 180), predictor.HeightAt(midnight), 9);
        Assert.Equal(Math.Cos((30 - 30) * Math.PI / 180), predictor.HeightAt(midnight.AddHours(1)), 9);
    }
}
