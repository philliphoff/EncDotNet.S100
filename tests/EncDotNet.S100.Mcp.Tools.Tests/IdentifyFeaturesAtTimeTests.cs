using System.Collections.ObjectModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>identify_features</c> returns only what the map draws (#1354): at a
/// request's time, time-dependent warnings (S-412, CAP) that aren't in force
/// then are left out; with no time it follows the host's map
/// (<see cref="IMapDrawState"/>), hidden S-124 parts included; and with
/// neither, every drawable feature, as before.
/// </summary>
public class IdentifyFeaturesAtTimeTests
{
    // synthetic_all_warnings.gml: ICE1 (61N 23W) is in force from 2026-11-07
    // 00:00 to 2026-11-10 00:00 UTC.
    private static readonly DateTimeOffset IceInForce = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AllEnded = new(2026, 11, 10, 1, 0, 0, TimeSpan.Zero);

    // hand-written-circle.xml: the circle (38.5N 74.5W, 30 km) is in force
    // from 2026-08-28 15:00 to 18:00 UTC.
    private static readonly DateTimeOffset CapInForce = new(2026, 8, 28, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CapExpired = new(2026, 8, 28, 19, 0, 0, TimeSpan.Zero);

    private static FakeDatasetCatalog S412Catalog()
    {
        var dataset = S412Dataset.Open(Path.Combine(AppContext.BaseDirectory, "TestData", "S412", "synthetic_all_warnings.gml"));
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("warnings"),
            new SpecRef("S-412", new SpecVersion(1, 1, 0)),
            LoadedDatasetFactory.Box(50, -38, 62, -20),
            null,
            new S412DatasetData(dataset)));
        return catalog;
    }

    private static FakeDatasetCatalog CapCatalog()
    {
        var processor = new CapDatasetProcessor(Path.Combine(AppContext.BaseDirectory, "TestData", "CAP", "hand-written-circle.xml"));
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("cap"),
            new SpecRef("CAP", new SpecVersion(1, 2, 0)),
            LoadedDatasetFactory.Box(37, -76, 41, -73),
            null,
            processor.CreateLoadedData()));
        return catalog;
    }

    private static async Task<IReadOnlyList<string>> IdentifyAsync(
        IdentifyFeaturesTool tool, double latitude, double longitude, DateTimeOffset? time = null)
    {
        var result = await tool.InvokeAsync(
            new IdentifyFeaturesRequest(latitude, longitude, Time: time), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value));
        return [.. value.Features.Select(f => f.FeatureId)];
    }

    [Fact]
    public async Task S412_AtATimeTheWarningHasEnded_FindsNothing()
    {
        var tool = new IdentifyFeaturesTool(S412Catalog());

        Assert.Empty(await IdentifyAsync(tool, 61.0, -23.0, AllEnded));
        Assert.Equal(["ICE1"], await IdentifyAsync(tool, 61.0, -23.0, IceInForce));
    }

    [Fact]
    public async Task S412_WithNoTimeAndNoMap_FindsEveryWarning()
    {
        var tool = new IdentifyFeaturesTool(S412Catalog());

        Assert.Equal(["ICE1"], await IdentifyAsync(tool, 61.0, -23.0));
    }

    [Fact]
    public async Task S412_WithNoTime_FollowsTheMapTime()
    {
        var tool = new IdentifyFeaturesTool(S412Catalog(), new FakeMapDrawState(AllEnded));

        Assert.Empty(await IdentifyAsync(tool, 61.0, -23.0));
        // An explicit time wins over the map's.
        Assert.Equal(["ICE1"], await IdentifyAsync(tool, 61.0, -23.0, IceInForce));
    }

    [Fact]
    public async Task Cap_AtATimeTheWarningHasExpired_FindsNothing()
    {
        var catalog = CapCatalog();
        var tool = new IdentifyFeaturesTool(catalog);

        Assert.Empty(await IdentifyAsync(tool, 38.5, -74.5, CapExpired));
        Assert.Single(await IdentifyAsync(tool, 38.5, -74.5, CapInForce));
        Assert.Single(await IdentifyAsync(tool, 38.5, -74.5));
        Assert.Empty(await IdentifyAsync(new IdentifyFeaturesTool(catalog, new FakeMapDrawState(CapExpired)), 38.5, -74.5));
    }

    [Fact]
    public async Task S124_LeavesOutTheHiddenPartsAndWhatOnlyTheyReference()
    {
        var catalog = S124Catalog(out var dataset);
        var tool = new IdentifyFeaturesTool(catalog, new FakeMapDrawState(null, (dataset, ["P1"])));

        // P1 and the area only it references are hidden; P2 and the area it
        // shares with P1 stay.
        Assert.Equal(["P2", "SHARED"], await IdentifyAsync(tool, 0.0, 0.0));
    }

    [Fact]
    public async Task S124_WithEveryPartHidden_FindsNothing()
    {
        var catalog = S124Catalog(out var dataset);
        var tool = new IdentifyFeaturesTool(catalog, new FakeMapDrawState(null, (dataset, ["P1", "P2"])));

        Assert.Empty(await IdentifyAsync(tool, 0.0, 0.0));
    }

    [Fact]
    public async Task S124_WithAnExplicitTime_IgnoresTheMapsHiddenParts()
    {
        var catalog = S124Catalog(out var dataset);
        var tool = new IdentifyFeaturesTool(catalog, new FakeMapDrawState(null, (dataset, ["P1", "P2"])));

        Assert.Equal(["P1", "P2", "ONLY_P1", "SHARED"], await IdentifyAsync(tool, 0.0, 0.0, IceInForce));
    }

    // Two parts at the origin: P1 references ONLY_P1 and SHARED, P2 SHARED;
    // the smaller area ranks first.
    private static FakeDatasetCatalog S124Catalog(out LoadedDataset dataset)
    {
        dataset = LoadedDatasetFactory.S124("navwarn", S124Synth.Dataset(
            Part("P1", "ONLY_P1", "SHARED"),
            Part("P2", "SHARED"),
            Area("ONLY_P1", 0.1),
            Area("SHARED", 0.5)));
        var catalog = new FakeDatasetCatalog();
        catalog.Add(dataset);
        return catalog;
    }

    private static S124Feature Part(string id, params string[] areas) => new()
    {
        Id = id,
        FeatureType = "NavwarnPart",
        GeometryType = S100GeometryType.Point,
        Points = [new GeoPosition(0.0, 0.0)],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
        References = [.. areas.Select(a => S124Synth.Ref("theAffectedArea", "#" + a))],
    };

    private static S124Feature Area(string id, double half) => new()
    {
        Id = id,
        FeatureType = "NavwarnAreaAffected",
        GeometryType = S100GeometryType.Surface,
        ExteriorRing = [
            new GeoPosition(-half, -half), new GeoPosition(-half, half), new GeoPosition(half, half), new GeoPosition(half, -half), new GeoPosition(-half, -half)],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
    };

    private sealed class FakeMapDrawState(DateTimeOffset? time, params (LoadedDataset Dataset, string[] Ids)[] hidden) : IMapDrawState
    {
        public DateTimeOffset? Time => time;

        public IReadOnlySet<string>? HiddenFeatures(LoadedDataset dataset) =>
            hidden.FirstOrDefault(h => ReferenceEquals(h.Dataset, dataset)).Ids?.ToHashSet(StringComparer.Ordinal);
    }
}
