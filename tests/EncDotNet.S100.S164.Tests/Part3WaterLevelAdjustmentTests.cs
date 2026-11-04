using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 3 water level adjustment (WaterLevelAdjustment,
/// AdjustmentOfDepthValues, WLAFeatureInformation, IncompatibleDatums) and
/// Part 7 SafetyContourWLA, against the v2.0.0 WaterLevelAdjustment
/// exchange set (#1132 phase 3, <c>docs/design/chart-wide-water-level-adjustment.md</c>).
/// </summary>
/// <remarks>
/// In this set the S-101 cell declares no sounding datum, the S-102 is on
/// MLLW and the S-104 on LAT. So the S-101 depths are adjusted only with the
/// "Apply anyway when datum is not declared" override, and the S-102 surface
/// is always refused. The S-104 covers the east of the cell, January 2026.
/// </remarks>
public sealed class Part3WaterLevelAdjustmentTests
{
    // The marker after an adjusted sounding (#1132 Q3).
    private const string Marker = "◆";
    private const string ExchangeSet = "WaterLevelAdjustment";
    private const string Cell = "10100AA164US4FL2A.000";
    private static readonly DateTime Time = new(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);

    // The manual's settings: safety contour 11.4 m.
    private static readonly MarinerSettings Settings = S164Portrayal.DisplayTestSettings with
    {
        SafetyContour = Depth.FromMetres(11.4),
    };

    [Fact]
    public async Task WaterLevelAdjustment_and_AdjustmentOfDepthValues_adjust_the_depths_in_the_footprint()
    {
        var ct = TestContext.Current.CancellationToken;
        var adjustment = await AdjustmentAsync(applyUndeclaredDatum: true, ct);
        var processor = await S164Portrayal.OpenAsync(ExchangeSet, Cell, ct);

        var charted = await InstructionsAsync(processor, null, ct);
        var adjusted = await InstructionsAsync(processor, adjustment, ct);

        // Adjusted soundings carry the marker; charted ones don't.
        Assert.DoesNotContain(charted, i => i is TextInstruction { Text: Marker });
        var markers = adjusted.OfType<TextInstruction>().Where(t => t.Text == Marker).ToList();
        Assert.NotEmpty(markers);

        // Every marker is inside the S-104 footprint (the east of the cell).
        Assert.All(markers, m => Assert.True(m.CoordinateOverride!.Value.Longitude > -80.21));

        // Soundings west of the footprint keep their charted figures.
        static IEnumerable<string> West(IEnumerable<DrawingInstruction> list) => list
            .OfType<PointInstruction>()
            .Where(p => p.CoordinateOverride is { Longitude: < -80.25 })
            .Select(p => $"{p.CoordinateOverride}|{p.SymbolReference}")
            .Order();
        Assert.Equal(West(charted), West(adjusted));

        // The depth areas in the footprint are re-shaded: the display list differs.
        Assert.NotEqual(
            charted.OfType<AreaInstruction>().Select(a => a.FillColor),
            adjusted.OfType<AreaInstruction>().Select(a => a.FillColor));
    }

    [Fact]
    public async Task WLAFeatureInformation_reports_the_water_level_and_the_adjusted_values()
    {
        var ct = TestContext.Current.CancellationToken;
        var adjustment = await AdjustmentAsync(applyUndeclaredDatum: true, ct);
        var processor = await S164Portrayal.OpenAsync(ExchangeSet, Cell, ct);
        _ = await InstructionsAsync(processor, adjustment, ct);

        var dataset = LoadedDatasetProjector.Project(new DatasetId(Cell), processor)!;
        var depthArea = FeatureAccessor.GetFeatures(dataset)!
            .Where(f => f.FeatureType == "DepthArea")
            .Select(f => processor.GetFeatureInfo(f.Id))
            .First(info => info!.Attributes.Any(a => a.Code == "waterLevelAdjustment"))!;

        var water = Assert.Single(depthArea.Attributes, a => a.Code == "waterLevelAdjustment");
        Assert.Contains(" m (lowest over the feature", water.DisplayValue);
        Assert.Contains(depthArea.Attributes, a => a.Code.StartsWith("adjusted.depthRange", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IncompatibleDatums_refuse_the_surface_and_the_undeclared_cell()
    {
        var ct = TestContext.Current.CancellationToken;
        var strict = await AdjustmentAsync(applyUndeclaredDatum: false, ct);
        var anyway = await AdjustmentAsync(applyUndeclaredDatum: true, ct);

        // S-101: no datum declared, so nothing is adjusted without the override.
        var processor = await S164Portrayal.OpenAsync(ExchangeSet, Cell, ct);
        var adjusted = await InstructionsAsync(processor, strict, ct);
        Assert.DoesNotContain(adjusted, i => i is TextInstruction { Text: Marker });

        // S-102 on MLLW, S-104 on LAT: refused even with the override.
        Assert.False(strict.Accepts(12));
        Assert.False(anyway.Accepts(12));
        Assert.True(anyway.Accepts(null));
        using var surface = await OpenSurfaceAsync(ct);
        var context = new RenderContext { Mariner = Settings };
        var plain = await surface.BuildCoveragePortrayalAsync(context, ct);
        var refused = await surface.BuildCoveragePortrayalAsync(context with { DepthAdjustment = anyway }, ct);
        Assert.Same(SafetyLines(plain), SafetyLines(refused));
    }

    [Fact]
    public async Task SafetyContourWLA_moves_the_S101_safety_contour_in_the_footprint()
    {
        var ct = TestContext.Current.CancellationToken;
        var adjustment = await AdjustmentAsync(applyUndeclaredDatum: true, ct);
        var processor = await S164Portrayal.OpenAsync(ExchangeSet, Cell, ct);

        static IEnumerable<string> SafetyContour(IEnumerable<DrawingInstruction> list) => list
            .OfType<LineInstruction>()
            .Where(l => l.LineColor == "DEPSC")
            .Select(l => $"{l.FeatureReference}|{l.CoordinatesOverride?.Count}")
            .Order();

        var charted = SafetyContour(await InstructionsAsync(processor, null, ct)).ToList();
        var adjusted = SafetyContour(await InstructionsAsync(processor, adjustment, ct)).ToList();

        Assert.NotEmpty(charted);
        Assert.NotEqual(charted, adjusted);
    }

    private static async Task<IReadOnlyList<DrawingInstruction>> InstructionsAsync(
        S101DatasetProcessor processor, IDepthAdjustment? adjustment, CancellationToken ct)
    {
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext
            {
                EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.OtherInformation },
                Mariner = Settings,
                DepthAdjustment = adjustment,
            },
            ct);
        return [.. result.SubLayers.SelectMany(l => l.Instructions)];
    }

    private static IReadOnlyList<Isoline> SafetyLines(CoveragePortrayalResult result) =>
        result.SubLayers.OfType<IsolineCoverageSubLayer>().Single(l => l.LayerKey == S102DatasetProcessor.SafetyContourLayerKey).Isolines;

    private static async Task<S102DatasetProcessor> OpenSurfaceAsync(CancellationToken ct)
    {
        var zip = await S164TestData.GetExchangeSetAsync(ExchangeSet, ct);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-102", Specification.CreatePortrayalCatalogueSource("S-102"));
        return new S102DatasetProcessor(
            ZipAssetSource.Create(zip, "S100_ROOT/"),
            "S-102/DATASET_FILES/102US004FL2AI252277.h5",
            catalogues,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory());
    }

    private static async Task<WaterLevelDepthAdjustment> AdjustmentAsync(bool applyUndeclaredDatum, CancellationToken ct)
    {
        var zip = await S164TestData.GetExchangeSetAsync(ExchangeSet, ct);
        var source = ZipAssetSource.Create(zip, "S100_ROOT/");
        var transforms = new ProjNetCrsTransformFactory();
        var datasets = new List<LoadedDataset>();
        foreach (var file in new[] { "104US00_Florida_Ovp_20260101_20260110.h5", "104US00_Florida_Ovp_20260111_20260120.h5" })
        {
            var processor = new S104DatasetProcessor(source, $"S-104/DATASET_FILES/{file}", transforms);
            datasets.Add(LoadedDatasetProjector.Project(new DatasetId(file), processor)!);
        }

        var water = new RouteCheckSourceCache(transforms).Get(datasets).WaterLevel!;
        return new WaterLevelDepthAdjustment(new WaterLevelSnapshot(water, Time), applyUndeclaredDatum, "s164");
    }
}
