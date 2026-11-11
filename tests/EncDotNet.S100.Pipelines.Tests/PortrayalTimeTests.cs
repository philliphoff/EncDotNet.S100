using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-100 Part 9a time commands (<c>Date</c>, <c>Time</c>, <c>DateTime</c>,
/// <c>TimeValid</c>, <c>ClearTime</c>) and the filter that draws an
/// instruction only in its time intervals (#1126).
/// </summary>
public class PortrayalTimeTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour = 12, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static PortrayalTime At(int year, int month, int day, int hour = 12) =>
        PortrayalTime.At(Utc(year, month, day, hour));

    // ── Parser ──

    [Fact]
    public void Parse_FixedDateRange_AppliesToEveryFollowingInstruction()
    {
        // As S100Scripting.ProcessFixedDateRange emits it ahead of the rule.
        var parsed = DrawingInstructionParser.Parse(
            "F1",
            "Date:20120816,20131130;TimeValid:closedInterval;ViewingGroup:27020;PointInstruction:BOYCAN01;TextInstruction:A");

        Assert.Equal(2, parsed.Count);
        var expected = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20120816", "20131130", TimeIntervalClosure.ClosedInterval);
        Assert.All(parsed, i => Assert.Equal([expected], i.TimeValid));
        Assert.Same(parsed[0].TimeValid, parsed[1].TimeValid);
    }

    [Fact]
    public void Parse_SemiIntervals_KeepOnlyTheirBound()
    {
        var begin = DrawingInstructionParser.Parse("F1", "Date:20131201;TimeValid:geSemiInterval;PointInstruction:A");
        var end = DrawingInstructionParser.Parse("F1", "Date:,20131130;TimeValid:leSemiInterval;PointInstruction:A");

        Assert.Equal(
            new PortrayalTimeInterval(PortrayalTimeKind.Date, "20131201", null, TimeIntervalClosure.GeSemiInterval),
            Assert.Single(Assert.Single(begin).TimeValid));
        Assert.Equal(
            new PortrayalTimeInterval(PortrayalTimeKind.Date, null, "20131130", TimeIntervalClosure.LeSemiInterval),
            Assert.Single(Assert.Single(end).TimeValid));
    }

    [Fact]
    public void Parse_SeveralTimeValid_Accumulate()
    {
        // ProcessPeriodicDateRanges emits one pair per periodicDateRange.
        var parsed = DrawingInstructionParser.Parse(
            "F1",
            "Date:----03--,----05--;TimeValid:closedInterval;Date:----09--,----10--;TimeValid:closedInterval;AreaFillReference:X");

        var area = Assert.Single(parsed);
        Assert.Equal(["----03--", "----09--"], area.TimeValid.Select(t => t.Begin));
    }

    [Fact]
    public void Parse_ClearTime_EndsTheIntervals()
    {
        // QualityOfBathymetricData adds its date-dependent symbol, then ClearTime.
        var parsed = DrawingInstructionParser.Parse(
            "F1",
            "PointInstruction:A;Date:20120816,20131130;TimeValid:closedInterval;PointInstruction:CHDATD01;ClearTime;PointInstruction:B");

        Assert.Equal([0, 1, 0], parsed.Select(i => i.TimeValid.Count));
    }

    [Fact]
    public void Parse_TimeAndDateTime_SetTheirKind()
    {
        var parsed = DrawingInstructionParser.Parse(
            "F1",
            "Time:0800,1700;TimeValid:geLtInterval;PointInstruction:A;ClearTime;DateTime:20131130T080000Z,;TimeValid:gtSemiInterval;PointInstruction:B");

        Assert.Equal(PortrayalTimeKind.Time, Assert.Single(parsed[0].TimeValid).Kind);
        Assert.Equal(
            new PortrayalTimeInterval(PortrayalTimeKind.DateTime, "20131130T080000Z", null, TimeIntervalClosure.GtSemiInterval),
            Assert.Single(parsed[1].TimeValid));
    }

    [Theory]
    [InlineData("TimeValid:closedInterval;PointInstruction:A")] // no bounds yet
    [InlineData("Date:20131130,20131201;TimeValid:sometimes;PointInstruction:A")]
    [InlineData("Date:20131130,20131201;TimeValid:3;PointInstruction:A")]
    public void Parse_TimeValidWithoutBoundsOrKnownClosure_IsIgnored(string instructions)
    {
        Assert.Empty(Assert.Single(DrawingInstructionParser.Parse("F1", instructions)).TimeValid);
    }

    [Fact]
    public void Parse_NoTimeCommands_AlwaysDrawn()
    {
        Assert.Empty(Assert.Single(DrawingInstructionParser.Parse("F1", "PointInstruction:A")).TimeValid);
    }

    // ── Fixed dates ──

    [Theory]
    [InlineData(2012, 8, 15, false)]
    [InlineData(2012, 8, 16, true)]
    [InlineData(2013, 2, 18, true)]
    [InlineData(2013, 11, 30, true)]
    [InlineData(2013, 12, 1, false)]
    public void ClosedDateInterval_IncludesBothDays(int year, int month, int day, bool expected)
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20120816", "20131130", TimeIntervalClosure.ClosedInterval);
        Assert.Equal(expected, interval.Overlaps(At(year, month, day)));
    }

    [Theory]
    [InlineData(TimeIntervalClosure.OpenInterval, false, false)]
    [InlineData(TimeIntervalClosure.GeLtInterval, true, false)]
    [InlineData(TimeIntervalClosure.GtLeInterval, false, true)]
    [InlineData(TimeIntervalClosure.ClosedInterval, true, true)]
    public void Closure_DecidesWhetherTheBoundDaysCount(TimeIntervalClosure closure, bool onBegin, bool onEnd)
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20130101", "20130131", closure);
        Assert.Equal(onBegin, interval.Overlaps(At(2013, 1, 1)));
        Assert.Equal(onEnd, interval.Overlaps(At(2013, 1, 31)));
        Assert.True(interval.Overlaps(At(2013, 1, 15)));
    }

    [Fact]
    public void SemiInterval_IgnoresTheOtherBound()
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20131201", "20000101", TimeIntervalClosure.GeSemiInterval);
        Assert.False(interval.Overlaps(At(2013, 11, 30)));
        Assert.True(interval.Overlaps(At(2030, 1, 1)));
    }

    [Fact]
    public void Range_DrawsWhatAnyDayOfTheRangeDraws()
    {
        // S-164 FixedDateRange: 01.02.2012 to 01.12.2012 meets a range that
        // starts 16.08.2012, not one that ends 30.11.2013 only after it.
        var from2012 = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20120816", "20131130", TimeIntervalClosure.ClosedInterval);
        var from2013 = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20131201", null, TimeIntervalClosure.GeSemiInterval);
        var range = PortrayalTime.Between(Utc(2012, 2, 1), Utc(2012, 12, 1));

        Assert.True(from2012.Overlaps(range));
        Assert.False(from2013.Overlaps(range));
        Assert.True(from2013.Overlaps(PortrayalTime.Between(Utc(2013, 11, 1), Utc(2013, 12, 1))));
    }

    [Fact]
    public void LongRange_OfFixedDates_ComparesItsEnds()
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "20120816", "20131130", TimeIntervalClosure.ClosedInterval);
        Assert.True(interval.Overlaps(PortrayalTime.Between(Utc(2000, 1, 1), Utc(2020, 1, 1))));
        Assert.False(interval.Overlaps(PortrayalTime.Between(Utc(2014, 1, 1), Utc(2020, 1, 1))));
    }

    [Fact]
    public void Dates_AreReadInTheGivenZone()
    {
        // 23:30 UTC on 30 November is already 1 December an hour east.
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, null, "20131130", TimeIntervalClosure.LeSemiInterval);
        var time = Utc(2013, 11, 30, 23, 30);
        var east = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

        Assert.True(interval.Overlaps(PortrayalTime.At(time, TimeZoneInfo.Utc)));
        Assert.False(interval.Overlaps(PortrayalTime.At(time, east)));
    }

    // ── Periodic dates ──

    [Theory]
    [InlineData(2, 28, false)]
    [InlineData(3, 1, true)]
    [InlineData(10, 31, true)]
    [InlineData(11, 1, false)]
    public void MonthOnlyRange_RecursEveryYear(int month, int day, bool expected)
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "----03--", "----10--", TimeIntervalClosure.ClosedInterval);
        Assert.Equal(expected, interval.Overlaps(At(2013, month, day)));
        Assert.Equal(expected, interval.Overlaps(At(2027, month, day)));
    }

    [Theory]
    [InlineData(11, 30, false)]
    [InlineData(12, 1, true)]
    [InlineData(1, 15, true)]
    [InlineData(3, 31, true)]
    [InlineData(4, 1, false)]
    public void PeriodicRange_OverTheYearEnd_Wraps(int month, int day, bool expected)
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "----1201", "----0331", TimeIntervalClosure.ClosedInterval);
        Assert.Equal(expected, interval.Overlaps(At(2013, month, day)));
    }

    [Fact]
    public void YearOnlyBound_ComparesTheYear()
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "2000----", null, TimeIntervalClosure.GeSemiInterval);
        Assert.False(interval.Overlaps(At(1999, 12, 31)));
        Assert.True(interval.Overlaps(At(2000, 1, 1)));
    }

    [Fact]
    public void ExtendedDate_IsRead()
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Date, "2013-11-30", null, TimeIntervalClosure.GeSemiInterval);
        Assert.False(interval.Overlaps(At(2013, 11, 29)));
        Assert.True(interval.Overlaps(At(2013, 11, 30)));
    }

    // ── Times and instants ──

    [Theory]
    [InlineData(7, 59, false)]
    [InlineData(8, 0, true)]
    [InlineData(16, 59, true)]
    [InlineData(17, 0, false)]
    public void TimeOfDay_GeLt(int hour, int minute, bool expected)
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Time, "0800", "1700", TimeIntervalClosure.GeLtInterval);
        Assert.Equal(expected, interval.Overlaps(PortrayalTime.At(Utc(2013, 6, 1, hour, minute))));
    }

    [Fact]
    public void TimeOfDay_OverMidnight_Wraps()
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Time, "220000", "060000", TimeIntervalClosure.ClosedInterval);
        Assert.True(interval.Overlaps(PortrayalTime.At(Utc(2013, 6, 1, 23))));
        Assert.True(interval.Overlaps(PortrayalTime.At(Utc(2013, 6, 1, 3))));
        Assert.False(interval.Overlaps(PortrayalTime.At(Utc(2013, 6, 1, 12))));
    }

    [Fact]
    public void TimeOfDay_InRange_FindsTheBoundInside()
    {
        var interval = new PortrayalTimeInterval(PortrayalTimeKind.Time, "0815", "0830", TimeIntervalClosure.ClosedInterval);
        Assert.True(interval.Overlaps(PortrayalTime.Between(Utc(2013, 6, 1, 7), Utc(2013, 6, 1, 9))));
        Assert.False(interval.Overlaps(PortrayalTime.Between(Utc(2013, 6, 1, 9), Utc(2013, 6, 1, 10))));
    }

    [Fact]
    public void DateTime_WithAndWithoutOffset()
    {
        var utc = new PortrayalTimeInterval(PortrayalTimeKind.DateTime, "20131130T080000Z", "20131130T090000Z", TimeIntervalClosure.GeLtInterval);
        Assert.True(utc.Overlaps(PortrayalTime.At(Utc(2013, 11, 30, 8, 30))));
        Assert.False(utc.Overlaps(PortrayalTime.At(Utc(2013, 11, 30, 9, 0))));
        Assert.True(utc.Overlaps(PortrayalTime.Between(Utc(2013, 11, 30, 7), Utc(2013, 11, 30, 8, 0))));

        var east = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
        var local = new PortrayalTimeInterval(PortrayalTimeKind.DateTime, "20131130T080000", null, TimeIntervalClosure.GeSemiInterval);
        Assert.True(local.Overlaps(PortrayalTime.At(Utc(2013, 11, 30, 7, 30), east)));
        Assert.False(local.Overlaps(PortrayalTime.At(Utc(2013, 11, 30, 7, 30))));
    }

    // ── Filter ──

    [Fact]
    public void Filter_KeepsUndatedAndInDateInstructions_InOrder()
    {
        var list = DrawingInstructionParser.Parse(
            "F1",
            "PointInstruction:A;Date:20120816,20131130;TimeValid:closedInterval;PointInstruction:B;ClearTime;"
            + "Date:20131201;TimeValid:geSemiInterval;PointInstruction:C;ClearTime;PointInstruction:D");

        string[] Drawn(PortrayalTime? time) =>
            PortrayalTimeFilter.Apply(list, time).OfType<PointInstruction>().Select(p => p.SymbolReference!).ToArray();

        Assert.Equal(["A", "B", "D"], Drawn(At(2013, 2, 18)));
        Assert.Equal(["A", "C", "D"], Drawn(At(2014, 1, 1)));
        Assert.Equal(["A", "B", "C", "D"], Drawn(null));
        Assert.Same(list, PortrayalTimeFilter.Apply(list, null));
    }

    [Fact]
    public void Intervals_AreDistinct()
    {
        var list = DrawingInstructionParser.Parse(
            "F1", "Date:20120816,20131130;TimeValid:closedInterval;PointInstruction:A;PointInstruction:B")
            .Concat(DrawingInstructionParser.Parse(
                "F2", "Date:20120816,20131130;TimeValid:closedInterval;PointInstruction:A"));

        Assert.Single(PortrayalTimeFilter.Intervals(list));
    }

    [Fact]
    public void Changes_OnlyWhenAnIntervalFlips()
    {
        IReadOnlyList<PortrayalTimeInterval> intervals =
            [new PortrayalTimeInterval(PortrayalTimeKind.Date, "20120816", "20131130", TimeIntervalClosure.ClosedInterval)];

        Assert.False(PortrayalTimeFilter.Changes(intervals, At(2013, 2, 18, 1), At(2013, 2, 18, 23)));
        Assert.False(PortrayalTimeFilter.Changes(intervals, At(2013, 2, 18), At(2013, 3, 1)));
        Assert.True(PortrayalTimeFilter.Changes(intervals, At(2013, 11, 30), At(2013, 12, 1)));
        Assert.True(PortrayalTimeFilter.Changes(intervals, null, At(2013, 12, 1)));
        Assert.False(PortrayalTimeFilter.Changes([], null, At(2013, 12, 1)));
    }
}
