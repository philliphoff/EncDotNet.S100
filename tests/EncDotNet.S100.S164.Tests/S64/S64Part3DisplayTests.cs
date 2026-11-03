using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// S-64 §3.1 and §3.2 display tests on the S-57 cells <c>AA5DBASE</c>,
/// <c>AA5STNDR</c>, <c>AA5OTHER</c> and <c>AA3INVOB</c>, translated to S-101
/// and portrayed with the S-101 portrayal catalogue. The manual compares the
/// screen with S-52 plots; these check the display category each cell's
/// features land in at the §3.1 settings (safety contour 10 m, as S-164's
/// display tests).
/// </summary>
/// <remarks>
/// The S-64 cells put each feature in the category S-52 gives it. The S-101
/// portrayal catalogue moves a few, and those are listed as exceptions: dock
/// areas and lock basins are Standard Display in S-101 (22010), depth contours
/// other than the safety contour and landmarks are Other information (33020,
/// 32220), and overhead cables are Standard Display (12210).
/// </remarks>
public sealed class S64Part3DisplayTests
{
    private const string Base = "3.1 ENC Display/Base/ENC_ROOT/AA5DBASE.000";
    private const string Standard = "3.1 ENC Display/Standard/ENC_ROOT/AA5STNDR.000";
    private const string Other = "3.1 ENC Display/Other/ENC_ROOT/AA5OTHER.000";

    /// <summary>Depth and land areas every display cell carries as background.</summary>
    private static readonly HashSet<string> Background = ["DepthArea", "LandArea", "Coastline", "DataCoverage"];

    /// <summary>Dangers whose category follows their depth against the safety contour.</summary>
    private static readonly HashSet<string> Dangers = ["Obstruction", "Wreck", "UnderwaterAwashRock"];

    [Fact]
    public async Task DisplayBase_DrawsInDisplayBase()
    {
        var (processor, instructions) = await PortrayAsync(Base);
        var allowed = S164Portrayal.ViewingGroups("DisplayBase")
            .Concat(DisplayModeMembership.ResolveChartText(S164Portrayal.Catalogue))
            .ToHashSet();

        Assert.Equal(
            ["DepthContour (33020)", "DockArea (22010)", "Landmark (32220)", "LockBasin (22010)"],
            Outside(processor, instructions, allowed));
    }

    [Fact]
    public async Task DisplayStandard_DrawsInStandardDisplay()
    {
        var (processor, instructions) = await PortrayAsync(Standard);
        var allowed = S164Portrayal.ViewingGroups("StandardDisplay")
            .Concat(DisplayModeMembership.ResolveChartText(S164Portrayal.Catalogue))
            .ToHashSet();

        Assert.Empty(Outside(processor, instructions, allowed));
    }

    [Fact]
    public async Task DisplayOther_DrawsInOtherInformation()
    {
        // Apart from the background areas and the depth-dependent dangers,
        // every feature in OTHER is Other information only.
        var (processor, instructions) = await PortrayAsync(Other);
        var standard = S164Portrayal.ViewingGroups("StandardDisplay");

        var inStandard = instructions
            .Select(i => (Instruction: i, Type: processor.GetFeatureInfo(i.FeatureReference)?.FeatureType ?? ""))
            .Where(x => !Background.Contains(x.Type) && !Dangers.Contains(x.Type) && standard.Contains(x.Instruction.ViewingGroup))
            .Select(x => $"{x.Type} ({x.Instruction.ViewingGroup})")
            .Distinct()
            .ToArray();

        // The tidal stream is the "?" of #1244, and the objects of classes
        // translation has no rule for are "?" too, all drawn in 21010.
        Assert.Equal(
            ["CableOverhead (12210)", "S57UnknownObject (21010)", "TidalStreamFloodEbb (21010)"],
            inStandard.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task InvalidObjects_UnknownClassesDrawAQuestionMark()
    {
        // 3.2.1 a): the cell charts a point, a line and an area of unknown
        // classes (500 to 502): SY(QUESMRK1) on the point and in the area,
        // LC(QUESMRK1) along the line. Each can be picked, and its pick report
        // names the class.
        var processor = await S64Cells.OpenAsync("3.2 Invalid Object/ENC_ROOT/AA3INVOB.000", [], TestContext.Current.CancellationToken);
        var instructions = await S64Cells.PortrayAsync(processor, S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);

        string? ClassOf(DrawingInstruction i) => processor.GetFeatureInfo(i.FeatureReference)?.FeatureTypeName;

        var symbols = instructions.OfType<PointInstruction>()
            .Where(p => p.SymbolReference == S64Cells.QuestionMark).Select(ClassOf).ToHashSet();
        var lines = instructions.OfType<LineInstruction>()
            .Where(l => l.LineStyleReference == S64Cells.QuestionMark).Select(ClassOf).ToHashSet();

        Assert.Contains("Unknown S-57 object (500)", symbols);
        Assert.Contains("Unknown S-57 object (502)", symbols);
        Assert.Contains("Unknown S-57 object (501)", lines);
    }

    private static async Task<(Datasets.Pipelines.S57DatasetProcessor Processor, IReadOnlyList<DrawingInstruction> Instructions)> PortrayAsync(string cell)
    {
        var ct = TestContext.Current.CancellationToken;
        var processor = await S64Cells.OpenAsync(cell, [], ct);
        return (processor, await S64Cells.PortrayAsync(processor, S164Portrayal.DisplayTestSettings, ct));
    }

    private static string[] Outside(
        Datasets.Pipelines.S57DatasetProcessor processor, IEnumerable<DrawingInstruction> instructions, HashSet<int> allowed) =>
        instructions
            .Where(i => !i.AdditionalViewingGroups.Prepend(i.ViewingGroup).All(allowed.Contains))
            .Select(i => $"{processor.GetFeatureInfo(i.FeatureReference)?.FeatureType} ({i.ViewingGroup})")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
}
