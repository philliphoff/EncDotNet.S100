namespace EncDotNet.S100.Tides.Tests;

public sealed class SubordinateTidePredictorTests
{
    private static readonly DateTime Start = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TidePredictor Seattle = new(NoaaFixture.StationOnMllw("9447130"));

    private static readonly SubordinateOffsets Ratios = new(
        TimeSpan.FromMinutes(28), TimeSpan.FromMinutes(23), 1.11, 1.02, SubordinateHeightAdjustment.Ratio);

    private static readonly SubordinateOffsets Fixed = new(
        TimeSpan.FromMinutes(-40), TimeSpan.FromMinutes(-15), 0.12, -0.03, SubordinateHeightAdjustment.Offset);

    [Theory]
    [MemberData(nameof(BothAdjustments))]
    public void HighsAndLows_AreTheReferencesMovedAndScaled(SubordinateOffsets offsets)
    {
        var subordinate = new SubordinateTidePredictor(Seattle, offsets);
        var reference = Seattle.FindExtremes(Start, Start.AddDays(10));
        var mapped = subordinate.FindExtremes(Start.AddHours(1), Start.AddDays(10).AddHours(-1));

        // Those well inside both periods match one for one. Each search
        // refines its own roots, so times agree to a second, not exactly.
        var inner = reference.Where(e => e.Time > Start.AddHours(2) && e.Time < Start.AddDays(10).AddHours(-2)).ToList();
        foreach (var extreme in inner)
        {
            var expectedTime = extreme.Time + offsets.TimeFor(extreme.Kind);
            var match = Assert.Single(mapped, m => Math.Abs((m.Time - expectedTime).TotalSeconds) < 2);
            Assert.Equal(extreme.Kind, match.Kind);
            Assert.Equal(offsets.Apply(extreme.HeightMeters, offsets.HeightFor(extreme.Kind)), match.HeightMeters, 6);
        }
    }

    [Theory]
    [MemberData(nameof(BothAdjustments))]
    public void Curve_InterpolatesTheOffsetsBetweenHighsAndLows(SubordinateOffsets offsets)
    {
        var subordinate = new SubordinateTidePredictor(Seattle, offsets);
        var reference = Seattle.FindExtremes(Start, Start.AddDays(3));
        for (var i = 0; i + 1 < reference.Count; i++)
        {
            var (a, b) = (reference[i], reference[i + 1]);
            var subA = a.Time + offsets.TimeFor(a.Kind);
            var subB = b.Time + offsets.TimeFor(b.Kind);

            // At the extremes, and a quarter of the way between them.
            Assert.Equal(offsets.Apply(a.HeightMeters, offsets.HeightFor(a.Kind)), subordinate.HeightAt(subA), 6);
            var quarter = subA + ((subB - subA) / 4);
            var referenceTime = a.Time + ((b.Time - a.Time) / 4);
            var difference = offsets.HeightFor(a.Kind) + ((offsets.HeightFor(b.Kind) - offsets.HeightFor(a.Kind)) / 4);
            Assert.Equal(offsets.Apply(Seattle.HeightAt(referenceTime), difference), subordinate.HeightAt(quarter), 6);
        }
    }

    [Fact]
    public void Rate_IsPositiveWhileRising_AndSeriesMatchesHeightAt()
    {
        var subordinate = new SubordinateTidePredictor(Seattle, Ratios);
        var extremes = subordinate.FindExtremes(Start, Start.AddDays(2));
        for (var i = 0; i + 1 < extremes.Count; i++)
        {
            var middle = extremes[i].Time + ((extremes[i + 1].Time - extremes[i].Time) / 2);
            var rate = subordinate.RateAt(middle);
            Assert.True(extremes[i].Kind == TideExtremeKind.Low ? rate > 0 : rate < 0, $"{middle:u}: {rate}");
        }

        var series = new double[500];
        subordinate.Predict(Start, TimeSpan.FromMinutes(6), series);
        for (var k = 0; k < series.Length; k += 37)
        {
            Assert.Equal(subordinate.HeightAt(Start.AddMinutes(6 * k)), series[k], 12);
        }
    }

    [Fact]
    public void Datum_IsTheReferences_AndArgumentsAreChecked()
    {
        var subordinate = new SubordinateTidePredictor(Seattle, Ratios);
        Assert.Equal("MLLW", subordinate.DatumName);
        Assert.Same(Seattle, subordinate.Reference);
        Assert.Throws<ArgumentException>(() => subordinate.HeightAt(DateTime.Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => subordinate.Predict(Start, TimeSpan.Zero, new double[1]));
        Assert.Throws<ArgumentException>(() => subordinate.FindExtremes(Start, Start.AddHours(-1)));
        Assert.Throws<ArgumentException>(() => new SubordinateTidePredictor(Seattle, Ratios with { HighWaterHeight = double.NaN }));
    }

    public static TheoryData<SubordinateOffsets> BothAdjustments() => new(Ratios, Fixed);
}
