using System.IO.Compression;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 3 display tests (DisplayBase, DisplayStandard, DisplayOther,
/// SafetyContourDisplay, TextGrouping, FixedDateRange, PeriodicDateRange). The manual compares the screen with
/// reference plots; these check what the plots depend on in the portrayal
/// output: no portrayal rule fails, each cell's features land in its display
/// category at the test's mariner settings, and the safety contour and chart
/// text are drawn as the S-101 portrayal catalogue asks.
/// </summary>
public sealed class Part3DisplayTests
{
    /// <summary>The S-101 PC draws this symbol when a rule fails or a feature is unknown.</summary>
    private const string QuestionMark = "QUESMRK1";

    /// <summary>Depth and land areas every display cell carries as background.</summary>
    private static readonly HashSet<string> Background = ["DepthArea", "LandArea", "Coastline"];

    /// <summary>Dangers whose category follows their depth against the safety contour.</summary>
    private static readonly HashSet<string> Dangers = ["Obstruction", "Wreck", "UnderwaterAwashRock"];

    public static TheoryData<string> DisplayExchangeSets() =>
        ["DisplayBase", "DisplayStandard", "DisplayOther", "DisplayOther_FC210", "Chart1", "Settings"];

    [Theory]
    [MemberData(nameof(DisplayExchangeSets))]
    public async Task EveryCellPortraysWithoutRuleFailures(string exchangeSet)
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = await S164TestData.GetExchangeSetAsync(exchangeSet, ct);
        string[] cells;
        using (var archive = ZipFile.OpenRead(zip))
        {
            cells = archive.Entries
                .Where(e => e.FullName.StartsWith("S100_ROOT/S-101/DATASET_FILES/", StringComparison.Ordinal)
                    && e.Name.EndsWith(".000", StringComparison.Ordinal))
                .Select(e => e.Name)
                .ToArray();
        }

        Assert.NotEmpty(cells);
        foreach (var cell in cells)
        {
            var (processor, instructions) = await S164Portrayal.PortrayAsync(exchangeSet, cell, S164Portrayal.DisplayTestSettings, ct);
            // A Chart1Feature carries its own drawing instructions; Chart 1
            // shows the question mark itself as a legend entry.
            var failed = instructions.OfType<PointInstruction>()
                .Where(p => p.SymbolReference == QuestionMark)
                .Select(p => processor.GetFeatureInfo(p.FeatureReference)?.FeatureType ?? p.FeatureReference)
                .Where(type => type != "Chart1Feature")
                .Distinct()
                .ToArray();
            Assert.True(failed.Length == 0, $"{cell}: {string.Join(", ", failed)} drew {QuestionMark}.");
        }
    }

    [Fact]
    public async Task DisplayBase_IsolatedDangersAreDisplayBase()
    {
        // With the test's 10 m safety contour, the dangers in DBASE lie in water
        // as deep as the safety contour, so they draw as isolated dangers.
        var (processor, instructions) = await S164Portrayal.PortrayAsync(
            "DisplayBase", "10100AA_DBASE.000", S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);
        var displayBase = S164Portrayal.ViewingGroups("DisplayBase");

        var isolated = instructions.OfType<PointInstruction>().Where(p => p.SymbolReference == "ISODGR01").ToList();

        Assert.Equal(3, isolated.Count);
        Assert.All(isolated, p => Assert.Contains(p.ViewingGroup, displayBase));
        Assert.All(isolated, p => Assert.Contains(processor.GetFeatureInfo(p.FeatureReference)?.FeatureType ?? "", Dangers));
    }

    [Fact]
    public async Task DisplayStandard_DrawsInStandardDisplay()
    {
        var (processor, instructions) = await S164Portrayal.PortrayAsync(
            "DisplayStandard", "10100AA_STNDR.000", S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);
        // Text also carries a chart-text group, which no display mode lists.
        var allowed = S164Portrayal.ViewingGroups("StandardDisplay")
            .Concat(DisplayModeMembership.ResolveChartText(S164Portrayal.Catalogue))
            .ToHashSet();

        var outside = instructions
            .Where(i => !i.AdditionalViewingGroups.Prepend(i.ViewingGroup).All(allowed.Contains))
            .Select(i => $"{processor.GetFeatureInfo(i.FeatureReference)?.FeatureType} ({i.ViewingGroup})")
            .Distinct()
            .ToArray();

        Assert.True(outside.Length == 0, "Outside Standard Display: " + string.Join(", ", outside));
    }

    [Fact]
    public async Task DisplayOther_DrawsInOtherInformation()
    {
        var (processor, instructions) = await S164Portrayal.PortrayAsync(
            "DisplayOther", "10100AA_OTHER.000", S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);
        var standard = S164Portrayal.ViewingGroups("StandardDisplay");

        // Apart from the background areas and the depth-dependent dangers,
        // every feature in OTHER is Other information only.
        var inStandard = instructions
            .Select(i => (Instruction: i, Type: processor.GetFeatureInfo(i.FeatureReference)?.FeatureType ?? ""))
            .Where(x => !Background.Contains(x.Type) && !Dangers.Contains(x.Type) && standard.Contains(x.Instruction.ViewingGroup))
            .Select(x => $"{x.Type} ({x.Instruction.ViewingGroup})")
            .Distinct()
            .ToArray();

        Assert.True(inStandard.Length == 0, "In Standard Display: " + string.Join(", ", inStandard));
    }

    [Fact]
    public async Task SafetyContourDisplay_FollowsOnlyTheSafetyContourEdges()
    {
        // DEPARE03 adds a SpatialReference for each edge where the depth
        // crosses the safety contour and draws DEPSC along those edges only.
        var (_, instructions) = await S164Portrayal.PortrayAsync(
            "DisplayBase", "10100AA_DBASE.000", S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);

        var safetyContour = instructions.OfType<LineInstruction>().Where(l => l.LineColor == "DEPSC").ToList();

        Assert.NotEmpty(safetyContour);
        Assert.All(safetyContour, l => Assert.NotNull(l.CoordinatesOverride));
    }

    [Fact]
    public async Task TextGrouping_ChartTextShowsInOtherInformation()
    {
        var (_, instructions) = await S164Portrayal.PortrayAsync(
            "DisplayOther", "10100AA_OTHER.000", S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);

        var text = instructions.OfType<TextInstruction>().Select(t => t.Text.Trim()).ToList();

        Assert.Contains("varn 10.00", text);
        Assert.Contains("1.6 kn", text);
    }

    // ── Date-dependent display (S-98 §12.8.1) ──

    private const string SettingsCell = "10100AA_X0001.000";

    private static PortrayalTime Day(int year, int month, int day) =>
        PortrayalTime.At(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    private static PortrayalTime Days(int year1, int month1, int day1, int year2, int month2, int day2) =>
        PortrayalTime.Between(
            new DateTimeOffset(year1, month1, day1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(year2, month2, day2, 23, 59, 0, TimeSpan.Zero));

    /// <summary>The features of the Settings cell drawn at <paramref name="time"/>, by id.</summary>
    private static async Task<HashSet<string>> DrawnAsync(PortrayalTime? time)
    {
        var (_, instructions) = await S164Portrayal.PortrayAsync(
            "Settings", SettingsCell, S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken, time);
        return instructions.Select(i => i.FeatureReference).ToHashSet(StringComparer.Ordinal);
    }

    // The buoy that replaced another on 16.08.2012 (fixed date ranges).
    private const string NewBuoy = "69";
    private const string OldBuoy = "70";

    // The traffic separation scheme in force until 30.11.2013, and the one from 01.12.2013.
    private static readonly string[] OldScheme = ["75", "77", "80", "83", "84"];
    private static readonly string[] NewScheme = ["76", "78", "79", "81", "82"];

    [Fact]
    public async Task FixedDateRange_FeaturesCarryTheirDates()
    {
        var (_, instructions) = await S164Portrayal.PortrayAsync(
            "Settings", SettingsCell, S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);
        PortrayalTimeInterval Interval(string feature) =>
            Assert.Single(instructions.First(i => i.FeatureReference == feature).TimeValid);

        Assert.Equal(new PortrayalTimeInterval(PortrayalTimeKind.Date, "20120816", "20860816", TimeIntervalClosure.ClosedInterval), Interval(NewBuoy));
        Assert.Equal(new PortrayalTimeInterval(PortrayalTimeKind.Date, null, "20131130", TimeIntervalClosure.LeSemiInterval), Interval(OldScheme[0]));
        Assert.Equal(new PortrayalTimeInterval(PortrayalTimeKind.Date, "20131201", null, TimeIntervalClosure.GeSemiInterval), Interval(NewScheme[0]));
    }

    [Fact]
    public async Task FixedDateRange_TodayDrawsTheCurrentBuoyAndScheme()
    {
        var drawn = await DrawnAsync(PortrayalTime.At(DateTimeOffset.UtcNow));

        Assert.Contains(NewBuoy, drawn);
        Assert.DoesNotContain(OldBuoy, drawn);
        Assert.All(NewScheme, f => Assert.Contains(f, drawn));
        Assert.All(OldScheme, f => Assert.DoesNotContain(f, drawn));
    }

    [Fact]
    public async Task FixedDateRange_ViewingDate()
    {
        // 18.02.2013: after the buoy changed, before the scheme did.
        var february = await DrawnAsync(Day(2013, 2, 18));
        Assert.Contains(NewBuoy, february);
        Assert.DoesNotContain(OldBuoy, february);

        // 30.11.2013: the last day of the old scheme.
        var november = await DrawnAsync(Day(2013, 11, 30));
        Assert.All(OldScheme, f => Assert.Contains(f, november));
        Assert.All(NewScheme, f => Assert.DoesNotContain(f, november));
    }

    [Fact]
    public async Task FixedDateRange_ViewingRangeDrawsBoth()
    {
        // 01.02.2012–01.12.2012 holds both buoys; 01.11.2013–01.12.2013 both schemes.
        var buoys = await DrawnAsync(Days(2012, 2, 1, 2012, 12, 1));
        Assert.Contains(NewBuoy, buoys);
        Assert.Contains(OldBuoy, buoys);

        var schemes = await DrawnAsync(Days(2013, 11, 1, 2013, 12, 1));
        Assert.All(OldScheme.Concat(NewScheme), f => Assert.Contains(f, schemes));
    }

    // Buoys laid March–October and October–December every year.
    private const string SummerBuoy = "72";
    private const string WinterBuoy = "73";

    [Fact]
    public async Task PeriodicDateRange_ViewingDateDrawsTheBuoyInSeason()
    {
        var november = await DrawnAsync(Day(2013, 11, 1));
        Assert.Contains(WinterBuoy, november);
        Assert.DoesNotContain(SummerBuoy, november);

        // October is in both seasons, in any year; January in neither.
        var october = await DrawnAsync(Day(2027, 10, 15));
        Assert.Contains(SummerBuoy, october);
        Assert.Contains(WinterBuoy, october);

        var january = await DrawnAsync(Day(2014, 1, 15));
        Assert.DoesNotContain(SummerBuoy, january);
        Assert.DoesNotContain(WinterBuoy, january);
    }

    [Fact]
    public async Task DateDependentFeatures_WithoutAViewTime_AreAllDrawn()
    {
        var drawn = await DrawnAsync(null);

        Assert.All(new[] { NewBuoy, OldBuoy, SummerBuoy, WinterBuoy }.Concat(OldScheme).Concat(NewScheme), f => Assert.Contains(f, drawn));
    }
}
