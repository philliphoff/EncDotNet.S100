using System.Text.RegularExpressions;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// Opens every S-57 base cell in the pinned S-64:100 test data through the
/// S-57 translation pipeline, with its updates applied, and portrays it at the
/// display tests' mariner settings. These aren't S-64 tests themselves; they
/// show the S-57 path can use the data before the per-section tests rely on
/// it (issue #1120, slice 7).
/// </summary>
public sealed partial class S64SmokeTests
{
    /// <summary>
    /// Update folders for base cells whose updates come in another folder: the
    /// tests load the base cell first and the updates after it. The 2.2.2
    /// updates are edition 1, for the Power Up cell; the 2.2.4 one is
    /// edition 2, for the Good Base Cells one.
    /// </summary>
    private static readonly Dictionary<string, string> UpdateFolders = new(StringComparer.Ordinal)
    {
        ["2.1.1 Power Up/ENC_ROOT/GB5X01SW.000"] = "2.2.2 Loading of Updates/ENC_ROOT/",
        ["2.2.5 Good Base Cells/ENC_ROOT/GB5X01SW.000"] = "2.2.4 Loading of New Update/ENC_ROOT/",
        ["8.0 Skin of the Earth/SOE_Test_1_Base/ENC_ROOT/US4MA04M.000"] = "8.0 Skin of the Earth/SOE_Test_1_Update/ENC_ROOT/",
        ["8.0 Skin of the Earth/SOE_Test_2_Base/ENC_ROOT/PH4BTS40.000"] = "8.0 Skin of the Earth/SOE_Test_2_Update/ENC_ROOT/",
        ["8.0 Skin of the Earth/SOE_Test_3_Base/ENC_ROOT/PT548509.000"] = "8.0 Skin of the Earth/SOE_Test_3_Update/ENC_ROOT/",
        ["8.0 Skin of the Earth/SOE_Test_4_Base/ENC_ROOT/US3AK40M.000"] = "8.0 Skin of the Earth/SOE_Test_4_Update/ENC_ROOT/",
        ["8.0 Skin of the Earth/SOE_Test_5_Base/ENC_ROOT/GB55062B.000"] = "8.0 Skin of the Earth/SOE_Test_5_Update/ENC_ROOT/",
    };

    /// <summary>
    /// Base cells whose portrayal draws <see cref="S64Cells.QuestionMark"/>,
    /// with the S-101 feature types that do and why.
    /// </summary>
    /// <remarks>
    /// The 3.2 cell charts objects of unknown classes, which S-52 draws as "?";
    /// translation drops them instead (#1242), so they draw nothing.
    /// </remarks>
    private static readonly Dictionary<string, string[]> ExpectedQuestionMarks = new(StringComparer.Ordinal)
    {
        // A point PIPSOL: S-101 pipelines are curves only (#1243).
        ["2.1.1 Power Up/ENC_ROOT/GB4X0000.000"] = ["PipelineSubmarineOnLand"],
        // A TS_FEB without CURVEL: S-101 makes speed mandatory (#1244).
        ["3.1 ENC Display/Other/ENC_ROOT/AA5OTHER.000"] = ["TidalStreamFloodEbb"],
    };

    /// <summary>Every base cell in the edition, by path in the archive.</summary>
    internal static readonly string[] Cells =
    [
        "2.1.1 Power Up/ENC_ROOT/GB4X0000.000",
        "2.1.1 Power Up/ENC_ROOT/GB5X01NE.000",
        "2.1.1 Power Up/ENC_ROOT/GB5X01NW.000",
        "2.1.1 Power Up/ENC_ROOT/GB5X01SE.000",
        "2.1.1 Power Up/ENC_ROOT/GB5X01SW.000",
        "2.1.1 Power Up/ENC_ROOT/GB5X02SE.000",
        "2.1.5 Loading Corrupt Data/ENC_ROOT/GB5X01NE.000",
        "2.2.5 Good Base Cells/ENC_ROOT/GB5X01SW.000",
        "2.2.6 Re-issue/GB5X01SW_RE-ISSUE/ENC_ROOT/GB5X01SW.000",
        "3.1 ENC Display/Base/ENC_ROOT/AA5DBASE.000",
        "3.1 ENC Display/Other/ENC_ROOT/AA5OTHER.000",
        "3.1 ENC Display/Standard/ENC_ROOT/AA5STNDR.000",
        "3.2 Invalid Object/ENC_ROOT/AA3INVOB.000",
        "3.2 Invalid Object/Invalid Base/ENC_ROOT/GB5X01NE.000",
        "3.3 Settings/ENC_ROOT/GB4X0001.000",
        "3.4 Non-Official Data/ENC_ROOT/1B5X01NE.000",
        "3.4 Non-Official Data/New Producer Codes/IC3NEWCC.000",
        "3.4 Non-Official Data/New Producer Codes/IC3NEWPC.000",
        "3.6 Display Priorities/ENC_ROOT/2J4X0001.000",
        "3.6 Display Priorities/ENC_ROOT/2J5X0001.000",
        "3.6 Display Priorities/ENC_ROOT/2J5X0002.000",
        "3.7 Overlap/ENC_ROOT/GB3OVRLP.000",
        "3.7.7 Scale minimum/ENC_ROOT/AA3SCAMN.000",
        "3.8.5 Non ENC data/ENC_ROOT/AMLDEU7U_NKKK4824_CLB_V30_Trials_data.000",
        "3.8.5 Non ENC data/ENC_ROOT/AMLDEU7U_NKKK4827_CLB_V30_Trials_data.000",
        "3.8.5 Non ENC data/ENC_ROOT/AMLDEU7U_NKKK5427_CLB_V30_Trials_data.000",
        "3.8.5 Non ENC data/ENC_ROOT/AMLDEU7U_NKKK5430_CLB_V30_Trials_data.000",
        "3.9 Polar ENC Data/ENC_ROOT/AA1NPOL3.000",
        "3.9 Polar ENC Data/ENC_ROOT/AA1NPOLA.000",
        "3.9 Polar ENC Data/ENC_ROOT/AA1NPOLB.000",
        "5.0 Navigational Hazards/ENC_ROOT/AA3NAVHZ.000",
        "5.0 Navigational Hazards/Overview/ENC_ROOT/AA2OVRVU.000",
        "6.0 Special Conditions/ENC_ROOT/AA3ARSPC.000",
        "7.0 Safety Contour/ENC_ROOT/AA3SAFCO.000",
        "8.0 Skin of the Earth/SOE_Test_1_Base/ENC_ROOT/US4MA04M.000",
        "8.0 Skin of the Earth/SOE_Test_2_Base/ENC_ROOT/PH4BTS40.000",
        "8.0 Skin of the Earth/SOE_Test_3_Base/ENC_ROOT/PT548509.000",
        "8.0 Skin of the Earth/SOE_Test_4_Base/ENC_ROOT/US3AK40M.000",
        "8.0 Skin of the Earth/SOE_Test_5_Base/ENC_ROOT/GB55062B.000",
    ];

    public static TheoryData<string> BaseCells() => new(Cells);

    [Fact]
    public async Task CellListMatchesTheArchive()
    {
        var files = await S64TestData.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Cells, files.Where(f => f.EndsWith(".000", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BaseCells))]
    public async Task EveryCellPortraysWithoutRuleFailures(string cell)
    {
        var ct = TestContext.Current.CancellationToken;
        var files = await S64TestData.ListAsync(ct);

        var updates = UpdatesFor(cell, files);
        var processor = await S64Cells.OpenAsync(cell, updates, ct);
        var instructions = await S64Cells.PortrayAsync(processor, S164Portrayal.DisplayTestSettings, ct);

        Assert.NotEmpty(instructions);
        var failed = S64Cells.QuestionMarks(processor, instructions);
        Assert.Equal(ExpectedQuestionMarks.GetValueOrDefault(cell, []), failed);
    }

    /// <summary>
    /// The update files for <paramref name="cell"/>: those beside it and those
    /// in its update folder, in update-number order.
    /// </summary>
    internal static IReadOnlyList<string> UpdatesFor(string cell, IReadOnlyList<string> files)
    {
        var name = Path.GetFileNameWithoutExtension(cell);
        var folders = new List<string> { cell[..(cell.LastIndexOf('/') + 1)] };
        if (UpdateFolders.TryGetValue(cell, out var folder))
        {
            folders.Add(folder);
        }

        return files
            .Where(f => folders.Any(d => f.StartsWith(d, StringComparison.Ordinal) && f.IndexOf('/', d.Length) < 0))
            .Where(f => Path.GetFileNameWithoutExtension(f) == name && UpdateFile().IsMatch(f))
            .OrderBy(f => f[^3..], StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"\.(?!000)\d{3}$")]
    private static partial Regex UpdateFile();
}
