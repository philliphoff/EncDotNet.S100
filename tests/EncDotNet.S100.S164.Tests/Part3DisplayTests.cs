using System.IO.Compression;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 3 display tests (DisplayBase, DisplayStandard, DisplayOther,
/// SafetyContourDisplay, TextGrouping). The manual compares the screen with
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
        var standard = S164Portrayal.ViewingGroups("StandardDisplay");

        var outside = instructions
            .Where(i => !standard.Contains(i.ViewingGroup))
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

    [Fact(Skip = "Fails until #1126: SpatialReference is skipped, so the safety contour is drawn round each depth area's whole outline.")]
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

    [Fact(Skip = "Fails until #1140: text viewing groups aren't in any display mode, so chart text is dropped whenever an ECDIS category is set.")]
    public async Task TextGrouping_ChartTextShowsInOtherInformation()
    {
        var (_, instructions) = await S164Portrayal.PortrayAsync(
            "DisplayOther", "10100AA_OTHER.000", S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);

        var text = instructions.OfType<TextInstruction>().Select(t => t.Text.Trim()).ToList();

        Assert.Contains("varn 10.00", text);
        Assert.Contains("1.6 kn", text);
    }
}
