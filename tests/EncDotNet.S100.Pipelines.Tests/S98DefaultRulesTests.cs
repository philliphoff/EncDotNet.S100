using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins the five PR-L2 inter-product rules from
/// <see cref="S98DefaultRules"/> after the issue #398 re-platform onto the
/// renderer-neutral <see cref="SubLayerStackItem"/> / <see cref="StackPayload"/>
/// engine. Three rules (R-101-102-A, R-104-A, R-111-A, R-101-124-A) are pure
/// plane-order anchors satisfied by PR-L1's default plane table; the only
/// mutating rule is R-101-102-B (suppress S-101 depth features when S-102 is
/// loaded). Suppression now filters encoding-neutral
/// <see cref="DrawingInstruction"/>s (matched to their
/// <see cref="VectorFeatureTag"/> via <see cref="VectorPortrayalResult.FeatureTags"/>)
/// rather than Mapsui <c>IFeature</c>s.
/// </summary>
public class S98DefaultRulesTests
{
    private readonly InteroperabilityAuthority _auth = new();

    // ----------------------------------------------------------------
    // R-101-102-A — plane-order property
    // ----------------------------------------------------------------

    [Fact]
    public void R_101_102_A_keeps_s102_between_s101_areas_and_linework()
    {
        var s101a = SynthItem("s101-cell.000", S98DisplayPlane.BaseChartUnder);
        var s101l = SynthItem("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var s102 = SynthItem("s102-tile.h5", S98DisplayPlane.Bathymetry);

        var sorted = _auth.Sort(new[] { s101a, s102, s101l });
        var ruled = _auth.ApplyRules(
            sorted,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: true),
            });

        Assert.Equal(
            new[] { S98DisplayPlane.BaseChartUnder, S98DisplayPlane.Bathymetry, S98DisplayPlane.BaseChartOver },
            ruled.Select(e => e.Plane).ToArray());
    }

    // ----------------------------------------------------------------
    // R-101-102-B — suppression
    // ----------------------------------------------------------------

    [Fact]
    public void R_101_102_B_does_not_suppress_when_only_s101_loaded()
    {
        var (areaItem, lineItem) = BuildS101ItemsWithDepthFeatures();
        var stack = new[] { areaItem, lineItem };

        var ruled = _auth.ApplyRules(
            stack,
            new[] { new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true) });

        // S-101 alone: nothing changes; the same items (and payloads) come back.
        Assert.Same(areaItem, ruled[0]);
        Assert.Same(lineItem, ruled[1]);
    }

    [Fact]
    public void R_101_102_B_suppresses_depth_area_and_depth_contour_when_s102_loaded()
    {
        var (areaItem, lineItem) = BuildS101ItemsWithDepthFeatures();
        var s102 = SynthItem("s102-tile.h5", S98DisplayPlane.Bathymetry);
        var stack = new[] { areaItem, s102, lineItem };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: true),
            });

        // Area sub-layer: DepthArea suppressed, LandArea retained.
        var areaTypes = FeatureTypesOf(ruled[0]);
        Assert.DoesNotContain("DepthArea", areaTypes);
        Assert.Contains("LandArea", areaTypes);

        // Line sub-layer: DepthContour suppressed, Coastline retained.
        var lineTypes = FeatureTypesOf(ruled[2]);
        Assert.DoesNotContain("DepthContour", lineTypes);
        Assert.Contains("Coastline", lineTypes);

        // S-102 item is passed through unchanged.
        Assert.Same(s102, ruled[1]);
    }

    [Fact]
    public void R_101_102_B_preserves_safety_contour_per_msc232_5_8()
    {
        // S-101 line sub-layer carries three DepthContour features at
        // 5m, 10m (=safety), 20m. MSC.232(82) §5.8 requires the
        // safety contour to remain visible even when S-102 replaces
        // bathy shading.
        var (areaItem, lineItem) = BuildS101ItemsWithDepthFeatures(
            depthContoursMetres: new double[] { 5.0, 10.0, 20.0 });

        var s102 = SynthItem("s102-tile.h5", S98DisplayPlane.Bathymetry);
        var stack = new[] { areaItem, s102, lineItem };

        var mariner = MarinerSettings.Default with { SafetyContour = Depth.FromMetres(10.0) };
        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: true),
            },
            mariner);

        var contourDepths = DepthContourValuesOf(ruled[2]);

        // Only the 10m contour survives — the safety contour.
        Assert.Equal(new[] { 10.0 }, contourDepths);
    }

    [Fact]
    public void R_101_102_B_does_not_fire_when_s102_inactive()
    {
        var (areaItem, lineItem) = BuildS101ItemsWithDepthFeatures();
        var stack = new[] { areaItem, lineItem };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                // S-102 loaded but NOT active — Layer Controls UI off.
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: false),
            });

        // No suppression — same item instances come back.
        Assert.Same(areaItem, ruled[0]);
        Assert.Same(lineItem, ruled[1]);
    }

    // ----------------------------------------------------------------
    // R-101-124-A, R-104-A, R-111-A — plane-order properties
    // ----------------------------------------------------------------

    [Fact]
    public void R_101_124_A_places_s124_on_cautions_and_warnings_above_s101()
    {
        var s101l = SynthItem("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var s124 = SynthItem("s124-warning.gml", S98DisplayPlane.CautionsAndWarnings);

        var sorted = _auth.Sort(new[] { s124, s101l });
        var ruled = _auth.ApplyRules(
            sorted,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s124-warning.gml", "S-124", Active: true),
            });

        Assert.Equal(
            new[] { S98DisplayPlane.BaseChartOver, S98DisplayPlane.CautionsAndWarnings },
            ruled.Select(e => e.Plane).ToArray());
    }

    [Fact]
    public void R_104_A_places_s104_color_band_below_s101_line_work()
    {
        var s101l = SynthItem("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var s104 = SynthItem("s104.h5", S98DisplayPlane.OnDemandSurface);

        var sorted = _auth.Sort(new[] { s101l, s104 });
        var ruled = _auth.ApplyRules(
            sorted,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s104.h5", "S-104", Active: true),
            });

        // OnDemandSurface (20) < BaseChartOver (30).
        Assert.Equal(
            new[] { S98DisplayPlane.OnDemandSurface, S98DisplayPlane.BaseChartOver },
            ruled.Select(e => e.Plane).ToArray());
    }

    [Fact]
    public void R_111_A_places_color_band_on_on_demand_and_arrows_on_dynamic_arrows()
    {
        var s101l = SynthItem("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var band = SynthItem("s111.h5", S98DisplayPlane.OnDemandSurface);
        var arrows = SynthItem("s111.h5", S98DisplayPlane.DynamicArrows);

        var sorted = _auth.Sort(new[] { arrows, s101l, band });
        var ruled = _auth.ApplyRules(
            sorted,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s111.h5", "S-111", Active: true),
            });

        // OnDemandSurface (20) < BaseChartOver (30) < DynamicArrows (60).
        Assert.Equal(
            new[] { S98DisplayPlane.OnDemandSurface, S98DisplayPlane.BaseChartOver, S98DisplayPlane.DynamicArrows },
            ruled.Select(e => e.Plane).ToArray());
    }

    // ----------------------------------------------------------------
    // R-101-104-B — clip S-104 surface to water
    // ----------------------------------------------------------------

    [Fact]
    public void R_101_104_B_attaches_land_mask_to_s104_surface_when_s101_loaded()
    {
        var s101 = BuildS101LandItem();
        var s104 = BuildCoverageGridItem("s104.h5", "S-104", S98DisplayPlane.OnDemandSurface);
        var stack = new[] { s104, s101 };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s104.h5", "S-104", Active: true),
            });

        var grid = GridSubLayerOf(ruled, "s104.h5");
        Assert.NotNull(grid.LandAreaMask);
        Assert.NotEmpty(grid.LandAreaMask!);
    }

    [Fact]
    public void R_101_104_B_does_not_mask_s102_surface()
    {
        var s101 = BuildS101LandItem();
        var s104 = BuildCoverageGridItem("s104.h5", "S-104", S98DisplayPlane.OnDemandSurface);
        var s102 = BuildCoverageGridItem("s102.h5", "S-102", S98DisplayPlane.Bathymetry);
        var stack = new[] { s102, s104, s101 };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s104.h5", "S-104", Active: true),
                new LoadedDatasetInfo("s102.h5", "S-102", Active: true),
            });

        // Only the S-104 surface is clipped to water; S-102 bathymetry (water
        // by nature) is left untouched.
        Assert.NotNull(GridSubLayerOf(ruled, "s104.h5").LandAreaMask);
        Assert.Null(GridSubLayerOf(ruled, "s102.h5").LandAreaMask);
    }

    [Fact]
    public void R_101_104_B_does_not_fire_when_s104_inactive()
    {
        var s101 = BuildS101LandItem();
        var s104 = BuildCoverageGridItem("s104.h5", "S-104", S98DisplayPlane.OnDemandSurface);
        var stack = new[] { s104, s101 };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                // S-104 loaded but NOT active.
                new LoadedDatasetInfo("s104.h5", "S-104", Active: false),
            });

        Assert.Null(GridSubLayerOf(ruled, "s104.h5").LandAreaMask);
    }

    [Fact]
    public void R_101_104_B_ignores_land_from_inactive_s101()
    {
        // Two S-101 cells: an active one without land, and an inactive one whose
        // only feature is a LandArea. The rule fires (an active S-101 exists) but
        // must not clip the active S-104 surface with the inactive cell's land.
        var activeS101 = BuildS101ItemWithoutLand();
        var inactiveS101 = BuildS101LandItem("s101-inactive.000");
        var s104 = BuildCoverageGridItem("s104.h5", "S-104", S98DisplayPlane.OnDemandSurface);
        var stack = new[] { s104, activeS101, inactiveS101 };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s101-inactive.000", "S-101", Active: false),
                new LoadedDatasetInfo("s104.h5", "S-104", Active: true),
            });

        Assert.Null(GridSubLayerOf(ruled, "s104.h5").LandAreaMask);
    }

    [Fact]
    public void R_101_104_B_does_not_fire_without_s101_land()
    {
        // S-104 surface + an S-101 whose only feature is a Coastline (no
        // LandArea) → no land geometry to attach.
        var s101 = BuildS101ItemWithoutLand();
        var s104 = BuildCoverageGridItem("s104.h5", "S-104", S98DisplayPlane.OnDemandSurface);
        var stack = new[] { s104, s101 };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s104.h5", "S-104", Active: true),
            });

        Assert.Null(GridSubLayerOf(ruled, "s104.h5").LandAreaMask);
    }

    // ----------------------------------------------------------------
    // Rule composition / declaration order
    // ----------------------------------------------------------------

    [Fact]
    public void Rules_execute_in_declaration_order_with_each_output_feeding_the_next()
    {
        // Build a marker rule that records the item-count it sees on
        // input. Compose it with a marker that always halves the
        // stack. If declaration order is honoured, the second rule
        // sees the first rule's output, not the original.
        var observed = new List<int>();

        var firstFires = false;
        var first = new S98InteroperabilityRule(
            RuleId: "TEST-1",
            SpecCitation: "test",
            Condition: _ => { firstFires = true; return true; },
            Effect: (stack, _) =>
            {
                observed.Add(stack.Count);
                return stack.Take(stack.Count / 2).ToList();
            });

        var second = new S98InteroperabilityRule(
            RuleId: "TEST-2",
            SpecCitation: "test",
            Condition: _ => true,
            Effect: (stack, _) =>
            {
                observed.Add(stack.Count);
                return stack;
            });

        var stack = Enumerable.Range(0, 8)
            .Select(i => SynthItem($"id-{i}", S98DisplayPlane.OtherChartOverlays))
            .ToArray();

        _auth.ApplyRules(
            stack,
            new[] { new LoadedDatasetInfo("id-0", "S-101", Active: true) },
            mariner: null,
            rules: new[] { first, second });

        Assert.True(firstFires);
        Assert.Equal(new[] { 8, 4 }, observed.ToArray());
    }

    [Fact]
    public void Default_rule_set_is_in_documented_order()
    {
        // S98DefaultRules.Default declaration order is part of the
        // public contract — pin it.
        Assert.Equal(
            new[] { "R-101-102-A", "R-101-102-B", "R-101-124-A", "R-104-A", "R-101-104-B", "R-111-A" },
            S98DefaultRules.Default.Select(r => r.RuleId).ToArray());
    }

    [Fact]
    public void Empty_rule_set_is_a_no_op()
    {
        var (areaItem, lineItem) = BuildS101ItemsWithDepthFeatures();
        var s102 = SynthItem("s102-tile.h5", S98DisplayPlane.Bathymetry);
        var stack = new[] { areaItem, s102, lineItem };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: true),
            },
            mariner: null,
            rules: System.Array.Empty<S98InteroperabilityRule>());

        // Same instances back — no rule fired.
        Assert.Same(areaItem, ruled[0]);
        Assert.Same(lineItem, ruled[2]);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// A stack item with no portrayal payload — used by the plane-order tests
    /// that only exercise <see cref="IInteroperabilityAuthority.Sort"/> /
    /// <see cref="IInteroperabilityAuthority.ApplyRules"/> ordering.
    /// </summary>
    private static SubLayerStackItem SynthItem(string id, S98DisplayPlane plane)
        => new(new SyntheticStackPayload(id), plane, 0, id);

    /// <summary>
    /// Builds the two S-101 vector stack items <c>S101DatasetProcessor</c>
    /// produces — an "areas" sub-layer with a DepthArea + LandArea and a
    /// "linework" sub-layer with one or more DepthContour features plus a
    /// Coastline — sharing one <see cref="VectorPortrayalResult"/> whose
    /// <see cref="VectorPortrayalResult.FeatureTags"/> carry the feature type
    /// (and, for contours, the VALDCO depth) the rule engine consults.
    /// </summary>
    private static (SubLayerStackItem Areas, SubLayerStackItem Lines) BuildS101ItemsWithDepthFeatures(
        double[]? depthContoursMetres = null)
    {
        depthContoursMetres ??= new double[] { 10.0 };

        var tags = new Dictionary<long, VectorFeatureTag>();
        long nextId = 1;

        // Areas: DepthArea (id 1), LandArea (id 2).
        var areaInstructions = new List<DrawingInstruction>();
        AddArea(areaInstructions, tags, ref nextId, "DepthArea", null);
        AddArea(areaInstructions, tags, ref nextId, "LandArea", null);

        // Lines: Coastline first, then one DepthContour per depth.
        var lineInstructions = new List<DrawingInstruction>();
        AddLine(lineInstructions, tags, ref nextId, "Coastline", null);
        foreach (var d in depthContoursMetres)
            AddLine(lineInstructions, tags, ref nextId, "DepthContour", d);

        var areaSub = new VectorSubLayer
        {
            LayerKey = "s101.areas",
            LayerName = "S-101 (areas)",
            Instructions = areaInstructions,
            Plane = S98DisplayPlane.BaseChartUnder,
            SourceFeatureType = "area",
        };
        var lineSub = new VectorSubLayer
        {
            LayerKey = "s101.linework",
            LayerName = "S-101 (lines)",
            Instructions = lineInstructions,
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "linework",
        };

        var result = new VectorPortrayalResult
        {
            SubLayers = new[] { areaSub, lineSub },
            Palette = new ColorPalette("test", new Dictionary<string, string>()),
            GeometryProvider = NullGeometryProvider.Instance,
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = "s101-cell.000",
            Info = "test",
            FeatureTags = tags,
        };

        var areaItem = new SubLayerStackItem(
            new VectorStackPayload(result, areaSub),
            S98DisplayPlane.BaseChartUnder, 0, "s101-cell.000", SourceFeatureType: "area");
        var lineItem = new SubLayerStackItem(
            new VectorStackPayload(result, lineSub),
            S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework");

        return (areaItem, lineItem);
    }

    private static void AddArea(
        List<DrawingInstruction> into, Dictionary<long, VectorFeatureTag> tags,
        ref long id, string featureType, object? depth)
    {
        into.Add(new AreaInstruction
        {
            FeatureReference = id.ToString(CultureInfo.InvariantCulture),
            FillColor = "CHBRN",
        });
        tags[id] = new VectorFeatureTag(featureType, depth);
        id++;
    }

    private static void AddLine(
        List<DrawingInstruction> into, Dictionary<long, VectorFeatureTag> tags,
        ref long id, string featureType, object? depth)
    {
        into.Add(new LineInstruction
        {
            FeatureReference = id.ToString(CultureInfo.InvariantCulture),
            LineColor = "CHBLK",
        });
        tags[id] = new VectorFeatureTag(featureType, depth);
        id++;
    }

    /// <summary>
    /// Reads the S-100 feature-type codes of the surviving instructions in a
    /// ruled item's vector payload, via its parent result's feature tags.
    /// </summary>
    private static IReadOnlyList<string> FeatureTypesOf(SubLayerStackItem item)
    {
        var payload = Assert.IsType<VectorStackPayload>(item.Payload);
        return SurvivingTags(payload).Select(t => t.FeatureType).ToList();
    }

    private static IReadOnlyList<double> DepthContourValuesOf(SubLayerStackItem item)
    {
        var payload = Assert.IsType<VectorStackPayload>(item.Payload);
        return SurvivingTags(payload)
            .Where(t => t.FeatureType == "DepthContour")
            .Select(t => (double)t.DepthContourValue!)
            .ToList();
    }

    private static IEnumerable<VectorFeatureTag> SurvivingTags(VectorStackPayload payload)
    {
        var tags = payload.Result.FeatureTags!;
        foreach (var instruction in payload.SubLayer.Instructions)
        {
            if (long.TryParse(instruction.FeatureReference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && tags.TryGetValue(id, out var tag))
            {
                yield return tag;
            }
        }
    }

    private sealed class NullGeometryProvider : IFeatureGeometryProvider
    {
        public static readonly NullGeometryProvider Instance = new();
        public FeatureGeometry? GetGeometry(string featureReference) => null;
    }

    /// <summary>
    /// Builds an S-101 area sub-layer item carrying a single <c>LandArea</c>
    /// feature (id 1) whose geometry provider returns a surface polygon — the
    /// input R-101-104-B collects to build the S-104 water-area mask.
    /// </summary>
    private static SubLayerStackItem BuildS101LandItem(string datasetId = "s101-cell.000")
    {
        var tags = new Dictionary<long, VectorFeatureTag> { [1] = new VectorFeatureTag("LandArea", null) };
        var land = new FeatureGeometry
        {
            Type = GeometryType.Surface,
            Coordinates = new[]
            {
                new GeoPosition(0.0, 0.0),
                new GeoPosition(0.0, 2.0),
                new GeoPosition(2.0, 2.0),
                new GeoPosition(2.0, 0.0),
                new GeoPosition(0.0, 0.0),
            },
        };

        var sub = new VectorSubLayer
        {
            LayerKey = "s101.areas",
            LayerName = "S-101 (areas)",
            Instructions = new List<DrawingInstruction>
            {
                new AreaInstruction { FeatureReference = "1", FillColor = "CHBRN" },
            },
            Plane = S98DisplayPlane.BaseChartUnder,
            SourceFeatureType = "area",
        };

        var result = new VectorPortrayalResult
        {
            SubLayers = new[] { sub },
            Palette = new ColorPalette("test", new Dictionary<string, string>()),
            GeometryProvider = new StubLandGeometryProvider("1", land),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
            FeatureTags = tags,
        };

        return new SubLayerStackItem(
            new VectorStackPayload(result, sub),
            S98DisplayPlane.BaseChartUnder, 0, datasetId, SourceFeatureType: "area");
    }

    /// <summary>
    /// Builds an S-101 line sub-layer item with a single <c>Coastline</c>
    /// feature and no <c>LandArea</c>, so R-101-104-B finds no land to attach.
    /// </summary>
    private static SubLayerStackItem BuildS101ItemWithoutLand()
    {
        var tags = new Dictionary<long, VectorFeatureTag> { [1] = new VectorFeatureTag("Coastline", null) };
        var sub = new VectorSubLayer
        {
            LayerKey = "s101.linework",
            LayerName = "S-101 (lines)",
            Instructions = new List<DrawingInstruction>
            {
                new LineInstruction { FeatureReference = "1", LineColor = "CHBLK" },
            },
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "linework",
        };

        var result = new VectorPortrayalResult
        {
            SubLayers = new[] { sub },
            Palette = new ColorPalette("test", new Dictionary<string, string>()),
            GeometryProvider = NullGeometryProvider.Instance,
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = "s101-cell.000",
            Info = "test",
            FeatureTags = tags,
        };

        return new SubLayerStackItem(
            new VectorStackPayload(result, sub),
            S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework");
    }

    /// <summary>
    /// Builds a minimal gridded-coverage stack item (2×2 EPSG:4326 grid) for a
    /// product, on the given plane — used to assert which surfaces R-101-104-B
    /// clips to water.
    /// </summary>
    private static SubLayerStackItem BuildCoverageGridItem(string datasetId, string spec, S98DisplayPlane plane)
    {
        var metadata = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 2,
            OriginLatitude = 0.0,
            OriginLongitude = 0.0,
            SpacingLatitudinal = 1.0,
            SpacingLongitudinal = 1.0,
        };
        var sampled = new SampledCoverage
        {
            Region = GridRegion.Full,
            Metadata = metadata,
            Values = new Dictionary<string, float[]> { ["waterLevelHeight"] = new float[] { 0f, 0f, 0f, 0f } },
        };
        var styled = new StyledCoverageLayer
        {
            Coverage = sampled,
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
        var viewport = new Viewport
        {
            MinLatitude = 0.0,
            MaxLatitude = 2.0,
            MinLongitude = 0.0,
            MaxLongitude = 2.0,
            WidthPixels = 1,
            HeightPixels = 1,
            ScaleDenominator = 1.0,
        };
        var grid = new GridCoverageSubLayer
        {
            LayerKey = spec.ToLowerInvariant() + ".surface",
            LayerName = spec + " surface",
            Plane = plane,
            Coverage = styled,
            Viewport = viewport,
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = new[] { grid },
            Spec = new SpecRef(spec, default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new SubLayerStackItem(new CoverageStackPayload(result, grid), plane, 0, datasetId);
    }

    private static GridCoverageSubLayer GridSubLayerOf(IReadOnlyList<SubLayerStackItem> ruled, string datasetId)
    {
        var item = ruled.Single(i => i.SourceDatasetId == datasetId);
        var payload = Assert.IsType<CoverageStackPayload>(item.Payload);
        return Assert.IsType<GridCoverageSubLayer>(payload.SubLayer);
    }

    private sealed class StubLandGeometryProvider : IFeatureGeometryProvider
    {
        private readonly string _id;
        private readonly FeatureGeometry _geometry;

        public StubLandGeometryProvider(string id, FeatureGeometry geometry)
        {
            _id = id;
            _geometry = geometry;
        }

        public FeatureGeometry? GetGeometry(string featureReference) =>
            string.Equals(featureReference, _id, StringComparison.Ordinal) ? _geometry : null;
    }
}
