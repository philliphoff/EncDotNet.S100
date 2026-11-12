namespace EncDotNet.S100.Tides.Tests;

public sealed class CurrentPredictorTests
{
    private static readonly DateTime Start = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CurrentStation M2Only(double mean = 0, double minor = 0, bool isRotary = false, double axis = 90) => new(
        "TEST_1",
        "Test",
        [new CurrentConstituent("M2", 1.0, 0, minor, 90)],
        axis,
        mean,
        0,
        floodDirectionDegrees: 85,
        ebbDirectionDegrees: 265,
        isRotary);

    [Fact]
    public void FindEvents_AlternatesSlacksAndMaxima_ForAnM2Current()
    {
        var predictor = new CurrentPredictor(M2Only());
        var events = predictor.FindEvents(Start, Start.AddDays(3));

        // Four slacks and four maxima a lunar day (24.84 h).
        Assert.InRange(events.Count, 22, 26);
        for (var i = 1; i < events.Count; i++)
        {
            Assert.NotEqual(events[i - 1].IsSlack, events[i].IsSlack);
        }

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case CurrentEventKind.MaximumFlood:
                    Assert.True(e.VelocityMetersPerSecond > 0.9);
                    break;
                case CurrentEventKind.MaximumEbb:
                    Assert.True(e.VelocityMetersPerSecond < -0.9);
                    break;
                case CurrentEventKind.SlackBeforeFlood:
                    Assert.True(predictor.VelocityAt(e.Time.AddMinutes(10)) > 0);
                    Assert.True(Math.Abs(predictor.VelocityAt(e.Time)) < 0.001);
                    break;
                case CurrentEventKind.SlackBeforeEbb:
                    Assert.True(predictor.VelocityAt(e.Time.AddMinutes(10)) < 0);
                    Assert.True(Math.Abs(predictor.VelocityAt(e.Time)) < 0.001);
                    break;
                default:
                    Assert.Fail($"Unexpected {e.Kind}");
                    break;
            }
        }
    }

    [Fact]
    public void FindEvents_ReportsNoSlack_WhenTheMeanCurrentOutrunsTheTide()
    {
        // A river current of 1.5 m/s with a 1 m/s tide never turns.
        var predictor = new CurrentPredictor(M2Only(mean: 1.5));
        var events = predictor.FindEvents(Start, Start.AddDays(2));

        Assert.DoesNotContain(events, e => e.IsSlack);
        Assert.Contains(events, e => e.Kind == CurrentEventKind.MaximumFlood);
        Assert.Contains(events, e => e.Kind == CurrentEventKind.MinimumFlood);
        Assert.All(events, e => Assert.True(e.IsFlood));
    }

    [Fact]
    public void CurrentAt_Rectilinear_UsesFloodAndEbbDirections()
    {
        var predictor = new CurrentPredictor(M2Only(minor: 0.5));
        var events = predictor.FindEvents(Start, Start.AddDays(1));
        var flood = events.First(e => e.Kind == CurrentEventKind.MaximumFlood);
        var ebb = events.First(e => e.Kind == CurrentEventKind.MaximumEbb);

        var atFlood = predictor.CurrentAt(flood.Time);
        var atEbb = predictor.CurrentAt(ebb.Time);
        Assert.Equal(85, atFlood.DirectionDegrees);
        Assert.Equal(265, atEbb.DirectionDegrees);
        Assert.Equal(Math.Abs(flood.VelocityMetersPerSecond), atFlood.SpeedMetersPerSecond, 6);
        Assert.True(atFlood.IsFlood);
        Assert.False(atEbb.IsFlood);
    }

    [Fact]
    public void CurrentAt_Rotary_TurnsClockwiseThroughTheCompass()
    {
        // Equal axes, minor lagging the major by 90°: a circle, turning
        // clockwise (east, south, west, north) for a major axis toward east.
        var predictor = new CurrentPredictor(M2Only(minor: 1.0, isRotary: true));
        var directions = new List<double>();
        for (var t = Start; t < Start.AddHours(12.42); t = t.AddMinutes(30))
        {
            var current = predictor.CurrentAt(t);
            // M2's node factor in 2026 is about 0.97.
            Assert.InRange(current.SpeedMetersPerSecond, 0.95, 0.99);
            directions.Add(current.DirectionDegrees);
        }

        var turned = 0.0;
        for (var i = 1; i < directions.Count; i++)
        {
            var step = directions[i] - directions[i - 1];
            turned += step < -180 ? step + 360 : step > 180 ? step - 360 : step;
        }

        Assert.InRange(turned, 330, 360);
    }

    [Fact]
    public void Constructor_RejectsUnknownAndRepeatedConstituents()
    {
        Assert.Throws<ArgumentException>(() => new CurrentStation(
            "x", "x", [new CurrentConstituent("XYZ", 1, 0)], 0, 0, 0, 0, 180, false));
        Assert.Throws<ArgumentException>(() => new CurrentStation(
            "x", "x", [new CurrentConstituent("M2", 1, 0), new CurrentConstituent("m2", 1, 0)], 0, 0, 0, 0, 180, false));
        Assert.Throws<ArgumentException>(() => new CurrentStation(
            "x", "x", [new CurrentConstituent("M2", double.NaN, 0)], 0, 0, 0, 0, 180, false));
    }

    [Fact]
    public void FindEvents_RejectsLocalTimesAndReversedPeriods()
    {
        var predictor = new CurrentPredictor(M2Only());
        Assert.Throws<ArgumentException>(() => predictor.FindEvents(DateTime.Now, DateTime.Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => predictor.FindEvents(Start, Start.AddDays(-1)));
    }

    [Fact]
    public void M2EllipseRatio_IsMinorOverMajor()
    {
        Assert.Equal(0.25, M2Only(minor: 0.25).M2EllipseRatio);
        Assert.Equal(0, M2Only().M2EllipseRatio);
    }

    [Fact]
    public void Subordinate_MovesEventsAndScalesMaxima()
    {
        var reference = new CurrentPredictor(M2Only());
        var offsets = new SubordinateCurrentOffsets(
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(-15), TimeSpan.FromMinutes(5), 0.5, 2.0);
        var subordinate = new SubordinateCurrentPredictor(reference, offsets, 10, 190);

        var referenceEvents = reference.FindEvents(Start, Start.AddDays(2));
        var events = subordinate.FindEvents(Start.AddHours(-1), Start.AddDays(2).AddHours(1));
        foreach (var r in referenceEvents)
        {
            var mapped = events.Single(e => e.Kind == r.Kind && Math.Abs((e.Time - r.Time - offsets.TimeFor(r.Kind)).TotalSeconds) < 1);
            var expected = r.IsSlack ? 0 : r.VelocityMetersPerSecond * (r.IsFlood ? 0.5 : 2.0);
            Assert.Equal(expected, mapped.VelocityMetersPerSecond, 9);
            Assert.Equal(expected, subordinate.VelocityAt(mapped.Time), 3);
        }

        var flood = events.First(e => e.Kind == CurrentEventKind.MaximumFlood);
        Assert.Equal(10, subordinate.CurrentAt(flood.Time).DirectionDegrees);
    }

    [Fact]
    public void Subordinate_DropsAnEventTheOffsetsWouldRunBackwards()
    {
        var reference = new CurrentPredictor(M2Only());

        // Maximum flood moved 4 hours later lands after the slack before ebb
        // (about 3.1 hours after it), which isn't moved: the first of the
        // two is kept, and the slack dropped.
        var offsets = new SubordinateCurrentOffsets(
            TimeSpan.Zero, TimeSpan.FromHours(4), TimeSpan.Zero, TimeSpan.Zero, 1, 1);
        var subordinate = new SubordinateCurrentPredictor(reference, offsets, 0, 180);
        var events = subordinate.FindEvents(Start, Start.AddDays(2));

        for (var i = 1; i < events.Count; i++)
        {
            Assert.True(events[i].Time > events[i - 1].Time);
        }

        Assert.Contains(events, e => e.Kind == CurrentEventKind.MaximumFlood);
        Assert.DoesNotContain(events, e => e.Kind == CurrentEventKind.SlackBeforeEbb);
    }
}
