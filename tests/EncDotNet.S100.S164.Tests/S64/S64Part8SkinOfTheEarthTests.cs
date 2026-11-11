using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// S-64 §8 skin of the earth tests. Each loads a cell, then an update that
/// changes the depth areas under a wreck or rock, and checks that the hazard's
/// symbol follows: whether it's an isolated danger depends on the depth of the
/// water around it. The translator works the surrounding depth out from the
/// depth areas after folding in the updates, so the change shows. An isolated
/// danger here is <c>ISODGR01</c> in viewing group 14010; the S-101 rule also
/// draws it in 24020 for a danger in shallow water.
/// </summary>
public sealed class S64Part8SkinOfTheEarthTests
{
    private const int IsolatedDanger = 14010;

    [Fact]
    public async Task Wreck_StopsBeingAnIsolatedDangerAfterUpdate4()
    {
        // 8.1: the wreck at 42°40.62'N 70°35.12'W lies in a 9.1 m depth area,
        // so it's an isolated danger at a 9 m safety contour. Update 4 puts it
        // in shallower water and it becomes a plain dangerous wreck.
        var (before, after) = await IsolatedDangersAsync(1, "US4MA04M", 9);

        Assert.Equal(["Wreck 42.67699 -70.58534"], before.Except(after));
        Assert.Empty(after.Except(before));
    }

    [Fact]
    public async Task Rock_StopsBeingAnIsolatedDangerWhenUpdate1InsertsADepthArea()
    {
        // 8.2: update 1 inserts a 0–2 m depth area around the rocks.
        var (before, after) = await IsolatedDangersAsync(2, "PH4BTS40", 10);

        Assert.Equal(
            ["UnderwaterAwashRock 13.67497 120.84404", "UnderwaterAwashRock 13.67578 120.84420"],
            before.Except(after).Order(StringComparer.Ordinal));
        Assert.Empty(after.Except(before));
    }

    [Fact]
    public async Task Rock_StopsBeingAnIsolatedDangerAfterUpdate2()
    {
        // 8.3.
        var (before, after) = await IsolatedDangersAsync(3, "PT548509", 10);

        Assert.Equal(
            ["UnderwaterAwashRock 38.67746 -28.20819", "UnderwaterAwashRock 38.67822 -28.20415"],
            before.Except(after).Order(StringComparer.Ordinal));
        Assert.Empty(after.Except(before));
    }

    [Fact]
    public async Task Rock_StopsBeingAnIsolatedDangerWhenItsDepthAreaMoves()
    {
        // 8.4: update 1 inserts a new depth area; the old one stays, moved
        // and with other depths.
        var (before, after) = await IsolatedDangersAsync(4, "US3AK40M", 10);

        Assert.Equal(["UnderwaterAwashRock 55.26079 -132.97992"], before.Except(after));
        Assert.Empty(after.Except(before));
    }

    [Fact]
    public async Task IsolatedDangerMarks_StayAfterAnUpdateRepeatsTheirDepthArea()
    {
        // 8.5: in Display Base at a 5 m safety contour; update 1 inserts a
        // depth area with the same depths as the one it replaces, so the
        // isolated dangers stay.
        var (before, after) = await IsolatedDangersAsync(5, "GB55062B", 5);

        Assert.Equal(2, before.Count);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// The isolated dangers of the §8 test <paramref name="test"/>'s cell
    /// before and after its updates, as their type and position, at the
    /// test's safety contour and safety depth.
    /// </summary>
    private static async Task<(IReadOnlySet<string> Before, IReadOnlySet<string> After)> IsolatedDangersAsync(
        int test, string cell, double safetyContour)
    {
        var ct = TestContext.Current.CancellationToken;
        var path = $"8.0 Skin of the Earth/SOE_Test_{test}_Base/ENC_ROOT/{cell}.000";
        var updates = S64SmokeTests.UpdatesFor(path, await S64TestData.ListAsync(ct));
        Assert.NotEmpty(updates);

        // The §8 settings: plain boundaries and simplified symbols.
        var settings = new MarinerSettings
        {
            SafetyContour = Depth.FromMetres(safetyContour),
            SafetyDepth = Depth.FromMetres(safetyContour),
            DeepContour = Depth.FromMetres(30),
            ShallowContour = Depth.FromMetres(2),
            PlainBoundaries = true,
            SimplifiedSymbols = true,
        };

        return (await PortrayAsync(path, [], settings, ct), await PortrayAsync(path, updates, settings, ct));
    }

    private static async Task<IReadOnlySet<string>> PortrayAsync(
        string cell, IReadOnlyList<string> updates, MarinerSettings settings, CancellationToken cancellationToken)
    {
        var processor = await S64Cells.OpenAsync(cell, updates, cancellationToken);
        var instructions = await S64Cells.PortrayAsync(processor, settings, cancellationToken);

        // Feature references change as updates insert and delete features, so
        // the dangers are matched by place.
        var features = new S101VectorSource(((S101DatasetData)processor.CreateLoadedData()).Dataset)
            .GetFeatures()
            .ToDictionary(f => f.Id.ToString(CultureInfo.InvariantCulture));
        return instructions.OfType<PointInstruction>()
            .Where(p => p.SymbolReference == "ISODGR01" && p.ViewingGroup == IsolatedDanger)
            .Select(p => features[p.FeatureReference])
            .Select(f => string.Create(
                CultureInfo.InvariantCulture, $"{f.FeatureType} {f.Coordinates[0].Latitude:F5} {f.Coordinates[0].Longitude:F5}"))
            .ToHashSet();
    }
}
