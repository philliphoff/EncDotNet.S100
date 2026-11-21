namespace EncDotNet.S100.Tides.Tests;

/// <summary>
/// Compares the current predictors with NOAA's own published current
/// predictions for 2026: four harmonic stations of different tide types,
/// and two subordinate stations.
/// </summary>
public sealed class NoaaCurrentComparisonTests(ITestOutputHelper output)
{
    private static readonly DateTime YearStart = new(NoaaCurrentFixture.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [MemberData(nameof(NoaaCurrentFixture.HarmonicIds), MemberType = typeof(NoaaCurrentFixture))]
    public void SixMinuteVelocities_MatchNoaa(string id)
    {
        var expected = NoaaCurrentFixture.JanuarySixMinute(id);
        var predictor = new CurrentPredictor(NoaaCurrentFixture.Station(id));
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
        output.WriteLine($"{id}: {expected.Length} samples, RMS {rms * 100:F3} cm/s, worst {worst * 100:F3} cm/s");
        Assert.Equal(24 * 10 * 31, expected.Length);

        // NOAA rounds its velocities to 0.1 cm/s.
        Assert.True(rms < 0.002, $"RMS {rms * 100:F3} cm/s");
        Assert.True(worst < 0.005, $"worst {worst * 100:F3} cm/s");
    }

    [Theory]
    [MemberData(nameof(NoaaCurrentFixture.HarmonicIds), MemberType = typeof(NoaaCurrentFixture))]
    public void HarmonicSlacksAndMaxima_MatchNoaa(string id) =>
        AssertEventsMatch(id, new CurrentPredictor(NoaaCurrentFixture.Station(id)));

    [Theory]
    [MemberData(nameof(NoaaCurrentFixture.SubordinateIds), MemberType = typeof(NoaaCurrentFixture))]
    public void SubordinateSlacksAndMaxima_MatchNoaa(string id)
    {
        var reference = NoaaCurrentFixture.Subordinate.Single(s => s.Id == id).Reference;
        var (flood, ebb) = NoaaCurrentFixture.Directions(id);
        var predictor = new SubordinateCurrentPredictor(
            new CurrentPredictor(NoaaCurrentFixture.Station(reference)),
            NoaaCurrentFixture.SubordinateOffsets(id),
            flood,
            ebb);
        AssertEventsMatch(id, predictor);
    }

    [Theory]
    [MemberData(nameof(NoaaCurrentFixture.SubordinateIds), MemberType = typeof(NoaaCurrentFixture))]
    public void SubordinateCurve_PassesThroughItsEvents(string id)
    {
        var reference = NoaaCurrentFixture.Subordinate.Single(s => s.Id == id).Reference;
        var (flood, ebb) = NoaaCurrentFixture.Directions(id);
        var predictor = new SubordinateCurrentPredictor(
            new CurrentPredictor(NoaaCurrentFixture.Station(reference)),
            NoaaCurrentFixture.SubordinateOffsets(id),
            flood,
            ebb);

        foreach (var e in predictor.FindEvents(YearStart, YearStart.AddDays(30)))
        {
            var current = predictor.CurrentAt(e.Time);
            Assert.True(Math.Abs(current.VelocityMetersPerSecond - e.VelocityMetersPerSecond) < 0.001, $"{e.Kind} at {e.Time:u}");
            if (!e.IsSlack)
            {
                Assert.Equal(e.Kind == CurrentEventKind.MaximumFlood ? flood : ebb, current.DirectionDegrees);
            }
        }
    }

    [Theory]
    [MemberData(nameof(NoaaCurrentFixture.HarmonicIds), MemberType = typeof(NoaaCurrentFixture))]
    public void BothAxes_MeanFloodAndEbbDirections_AreNearNoaas(string id)
    {
        // NOAA's flood and ebb directions are the mean directions of the
        // current while it floods and ebbs. Predicting both axes, with the
        // minor axis 90° clockwise of the major, gives directions near
        // them: the check that the minor axis's sense is right.
        var predictor = new CurrentPredictor(NoaaCurrentFixture.Station(id, isRotary: true));
        var (flood, ebb) = NoaaCurrentFixture.Directions(id);
        var (floodEast, floodNorth, ebbEast, ebbNorth) = (0.0, 0.0, 0.0, 0.0);
        for (var time = YearStart; time < YearStart.AddDays(30); time = time.AddMinutes(30))
        {
            var current = predictor.CurrentAt(time);
            var (sin, cos) = Math.SinCos(current.DirectionDegrees * Math.PI / 180);
            if (current.IsFlood)
            {
                floodEast += current.SpeedMetersPerSecond * sin;
                floodNorth += current.SpeedMetersPerSecond * cos;
            }
            else
            {
                ebbEast += current.SpeedMetersPerSecond * sin;
                ebbNorth += current.SpeedMetersPerSecond * cos;
            }
        }

        var meanFlood = Bearing(floodEast, floodNorth);
        var meanEbb = Bearing(ebbEast, ebbNorth);
        output.WriteLine($"{id}: flood {meanFlood:F1}° (NOAA {flood}°), ebb {meanEbb:F1}° (NOAA {ebb}°)");
        Assert.True(AngleBetween(meanFlood, flood) < 3, $"flood {meanFlood:F1}° vs NOAA {flood}°");
        Assert.True(AngleBetween(meanEbb, ebb) < 3, $"ebb {meanEbb:F1}° vs NOAA {ebb}°");
    }

    /// <summary>
    /// Every slack matches one of NOAA's to a minute, and every turn of the
    /// flood or ebb matches to a minute and 0.3 cm/s, except where a weak
    /// current pauses or nearly stops without turning. There NOAA and we
    /// may each list an event the other doesn't: NOAA lists a minimum under
    /// about 0.1 kn as a slack, where we list a minimum flood or ebb, and
    /// leaves out maxima under 0.1 kn; and its choice of turns in a pause
    /// differs from <see cref="CurrentPredictor.MinimumTurnSeparation"/> now
    /// and then. That happens at Mobile Bay Entrance, a weak diurnal
    /// current, for under 5% of the events, and never at the other stations.
    /// </summary>
    private void AssertEventsMatch(string id, ICurrentPredictor predictor)
    {
        var expected = NoaaCurrentFixture.Events(id);
        var actual = predictor.FindEvents(YearStart, YearStart.AddYears(1));

        var worstMinutes = 0.0;
        var worstSpeed = 0.0;
        var unmatched = new List<string>();
        foreach (var noaa in expected)
        {
            var match = actual.Where(e => Category(e) == Category(noaa)).MinBy(e => Math.Abs((e.Time - noaa.Time).Ticks));
            var minutes = Math.Abs((match.Time - noaa.Time).TotalMinutes);
            var speed = noaa.IsSlack ? 0 : Math.Abs(match.VelocityMetersPerSecond - noaa.VelocityMetersPerSecond);

            // NOAA rounds its times to the minute and velocities to 0.1 cm/s;
            // its 6-minute velocities differ from ours by up to 0.23 cm/s.
            // Where a weak current turns slowly, that difference moves a
            // slack by minutes: there, our velocity at NOAA's slack must be
            // under 0.2 cm/s instead.
            var slowSlack = noaa.IsSlack && Math.Abs(predictor.VelocityAt(noaa.Time)) < 0.002;
            if (!(minutes <= 1 || slowSlack && minutes <= 10) || speed > 0.003)
            {
                unmatched.Add($"NOAA {(noaa.IsSlack ? "slack" : noaa.IsFlood ? "flood" : "ebb")} {noaa.Time:u} {noaa.VelocityMetersPerSecond * 100:F1} cm/s; nearest ours {match.Time:u} {match.VelocityMetersPerSecond * 100:F1} cm/s");
                continue;
            }

            worstMinutes = Math.Max(worstMinutes, minutes);
            worstSpeed = Math.Max(worstSpeed, speed);
        }

        foreach (var ours in actual)
        {
            if (!expected.Any(e => Category(e) == Category(ours) && Math.Abs((e.Time - ours.Time).TotalMinutes) <= 10))
            {
                unmatched.Add($"ours {ours.Kind} {ours.Time:u} {ours.VelocityMetersPerSecond * 100:F1} cm/s");
            }
        }

        output.WriteLine($"{id}: NOAA {expected.Count} events, ours {actual.Count}; worst matched {worstMinutes:F2} min, {worstSpeed * 100:F2} cm/s");
        foreach (var line in unmatched)
        {
            output.WriteLine($"  unmatched: {line}");
        }

        var allowed = id == "MOB1101_2" ? expected.Count / 20 : 0;
        Assert.True(unmatched.Count <= allowed, $"{unmatched.Count} unmatched events, {allowed} allowed");
        var slacks = actual.Where(e => e.IsSlack).ToList();
        for (var i = 1; i < slacks.Count; i++)
        {
            Assert.NotEqual(slacks[i - 1].Kind, slacks[i].Kind);
        }
    }

    /// <summary>
    /// Slack, flood or ebb. NOAA labels a slack only "slack", and
    /// sometimes a near-stop that doesn't turn, so slacks match whatever
    /// comes next.
    /// </summary>
    private static int Category(CurrentEvent e) => e.IsSlack ? 0 : e.IsFlood ? 1 : 2;

    private static double Bearing(double east, double north)
    {
        var degrees = Math.Atan2(east, north) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double AngleBetween(double a, double b)
    {
        var difference = Math.Abs(a - b) % 360;
        return Math.Min(difference, 360 - difference);
    }
}
