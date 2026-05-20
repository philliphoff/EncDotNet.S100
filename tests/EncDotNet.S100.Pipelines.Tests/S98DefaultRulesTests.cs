using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using Mapsui;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins the five PR-L2 inter-product rules from
/// <see cref="S98DefaultRules"/>. Three rules
/// (R-101-102-A, R-104-A, R-111-A, R-101-124-A) are pure
/// plane-order anchors satisfied by PR-L1's default plane table;
/// the only mutating rule is R-101-102-B (suppress S-101 depth
/// features when S-102 is loaded). Tests cover every gate from
/// the briefing: condition firing, safety-contour exception, the
/// Active flag, and rule composition declaration order.
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
        var s101a = StackEntry("s101-cell.000", S98DisplayPlane.BaseChartUnder);
        var s101l = StackEntry("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var s102 = StackEntry("s102-tile.h5", S98DisplayPlane.Bathymetry);

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
        var (areaLayer, lineLayer) = BuildS101LayersWithDepthFeatures();
        var stack = new[]
        {
            new LayerStackEntry(areaLayer, S98DisplayPlane.BaseChartUnder, 0, "s101-cell.000", SourceFeatureType: "area"),
            new LayerStackEntry(lineLayer, S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework"),
        };

        var ruled = _auth.ApplyRules(
            stack,
            new[] { new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true) });

        // S-101 alone: nothing changes; the same Mapsui layers come back.
        Assert.Same(areaLayer, ruled[0].Layer);
        Assert.Same(lineLayer, ruled[1].Layer);
    }

    [Fact]
    public void R_101_102_B_suppresses_depth_area_and_depth_contour_when_s102_loaded()
    {
        var (areaLayer, lineLayer) = BuildS101LayersWithDepthFeatures();
        var s102 = new MemoryLayer("s102");
        var stack = new[]
        {
            new LayerStackEntry(areaLayer, S98DisplayPlane.BaseChartUnder, 0, "s101-cell.000", SourceFeatureType: "area"),
            new LayerStackEntry(s102, S98DisplayPlane.Bathymetry, 0, "s102-tile.h5"),
            new LayerStackEntry(lineLayer, S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework"),
        };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: true),
            });

        // Area layer: DepthArea suppressed, LandArea retained.
        var filteredArea = Assert.IsType<MemoryLayer>(ruled[0].Layer);
        var areaTypes = filteredArea.Features
            .Select(f => f[FeatureTagKeys.FeatureType] as string)
            .ToArray();
        Assert.DoesNotContain("DepthArea", areaTypes);
        Assert.Contains("LandArea", areaTypes);

        // Line layer: DepthContour suppressed, Coastline retained.
        var filteredLine = Assert.IsType<MemoryLayer>(ruled[2].Layer);
        var lineTypes = filteredLine.Features
            .Select(f => f[FeatureTagKeys.FeatureType] as string)
            .ToArray();
        Assert.DoesNotContain("DepthContour", lineTypes);
        Assert.Contains("Coastline", lineTypes);

        // S-102 layer is passed through unchanged.
        Assert.Same(s102, ruled[1].Layer);
    }

    [Fact]
    public void R_101_102_B_preserves_safety_contour_per_msc232_5_8()
    {
        // S-101 line layer carries three DepthContour features at
        // 5m, 10m (=safety), 20m. MSC.232(82) §5.8 requires the
        // safety contour to remain visible even when S-102 replaces
        // bathy shading.
        var (areaLayer, lineLayer) = BuildS101LayersWithDepthFeatures(
            depthContoursMetres: new double[] { 5.0, 10.0, 20.0 });

        var s102 = new MemoryLayer("s102");
        var stack = new[]
        {
            new LayerStackEntry(areaLayer, S98DisplayPlane.BaseChartUnder, 0, "s101-cell.000", SourceFeatureType: "area"),
            new LayerStackEntry(s102, S98DisplayPlane.Bathymetry, 0, "s102-tile.h5"),
            new LayerStackEntry(lineLayer, S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework"),
        };

        var mariner = MarinerSettings.Default with { SafetyContour = 10.0 };
        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: true),
            },
            mariner);

        var filteredLine = Assert.IsType<MemoryLayer>(ruled[2].Layer);
        var contourDepths = filteredLine.Features
            .Where(f => (f[FeatureTagKeys.FeatureType] as string) == "DepthContour")
            .Select(f => (double)f[FeatureTagKeys.DepthContourValue]!)
            .ToArray();

        // Only the 10m contour survives — the safety contour.
        Assert.Equal(new[] { 10.0 }, contourDepths);
    }

    [Fact]
    public void R_101_102_B_does_not_fire_when_s102_inactive()
    {
        var (areaLayer, lineLayer) = BuildS101LayersWithDepthFeatures();
        var stack = new[]
        {
            new LayerStackEntry(areaLayer, S98DisplayPlane.BaseChartUnder, 0, "s101-cell.000", SourceFeatureType: "area"),
            new LayerStackEntry(lineLayer, S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework"),
        };

        var ruled = _auth.ApplyRules(
            stack,
            new[]
            {
                new LoadedDatasetInfo("s101-cell.000", "S-101", Active: true),
                // S-102 loaded but NOT active — Layer Controls UI off.
                new LoadedDatasetInfo("s102-tile.h5", "S-102", Active: false),
            });

        // No suppression — same Mapsui layer instances come back.
        Assert.Same(areaLayer, ruled[0].Layer);
        Assert.Same(lineLayer, ruled[1].Layer);
    }

    // ----------------------------------------------------------------
    // R-101-124-A, R-104-A, R-111-A — plane-order properties
    // ----------------------------------------------------------------

    [Fact]
    public void R_101_124_A_places_s124_on_cautions_and_warnings_above_s101()
    {
        var s101l = StackEntry("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var s124 = StackEntry("s124-warning.gml", S98DisplayPlane.CautionsAndWarnings);

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
        var s101l = StackEntry("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var s104 = StackEntry("s104.h5", S98DisplayPlane.OnDemandSurface);

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
        var s101l = StackEntry("s101-cell.000", S98DisplayPlane.BaseChartOver);
        var band = StackEntry("s111.h5", S98DisplayPlane.OnDemandSurface);
        var arrows = StackEntry("s111.h5", S98DisplayPlane.DynamicArrows);

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
    // Rule composition / declaration order
    // ----------------------------------------------------------------

    [Fact]
    public void Rules_execute_in_declaration_order_with_each_output_feeding_the_next()
    {
        // Build a marker rule that records the layer-count it sees on
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
            .Select(i => StackEntry($"id-{i}", S98DisplayPlane.OtherChartOverlays))
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
            new[] { "R-101-102-A", "R-101-102-B", "R-101-124-A", "R-104-A", "R-111-A" },
            S98DefaultRules.Default.Select(r => r.RuleId).ToArray());
    }

    [Fact]
    public void Empty_rule_set_is_a_no_op()
    {
        var (areaLayer, lineLayer) = BuildS101LayersWithDepthFeatures();
        var s102 = new MemoryLayer("s102");
        var stack = new[]
        {
            new LayerStackEntry(areaLayer, S98DisplayPlane.BaseChartUnder, 0, "s101-cell.000", SourceFeatureType: "area"),
            new LayerStackEntry(s102, S98DisplayPlane.Bathymetry, 0, "s102-tile.h5"),
            new LayerStackEntry(lineLayer, S98DisplayPlane.BaseChartOver, 0, "s101-cell.000", SourceFeatureType: "linework"),
        };

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
        Assert.Same(areaLayer, ruled[0].Layer);
        Assert.Same(lineLayer, ruled[2].Layer);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static LayerStackEntry StackEntry(string id, S98DisplayPlane plane)
        => new(new MemoryLayer(id), plane, 0, id);

    /// <summary>
    /// Builds two synthetic Mapsui layers that mirror what
    /// <c>S101DatasetProcessor</c> produces after PR-L2 feature
    /// tagging: an "areas" layer with a DepthArea + LandArea, and a
    /// "linework" layer with one or more DepthContour features
    /// plus a Coastline. Features carry only the metadata the
    /// rule engine consults (S-100 feature type + VALDCO), not
    /// real geometry — that's the rule contract.
    /// </summary>
    private static (MemoryLayer Areas, MemoryLayer Lines) BuildS101LayersWithDepthFeatures(
        double[]? depthContoursMetres = null)
    {
        depthContoursMetres ??= new double[] { 10.0 };

        var areaFeatures = new List<IFeature>
        {
            TaggedFeature("DepthArea"),
            TaggedFeature("LandArea"),
        };
        var areas = new MemoryLayer
        {
            Name = "S-101 (areas)",
            Features = areaFeatures,
        };

        var lineFeatures = new List<IFeature> { TaggedFeature("Coastline") };
        foreach (var d in depthContoursMetres)
        {
            var f = TaggedFeature("DepthContour");
            f[FeatureTagKeys.DepthContourValue] = d;
            lineFeatures.Add(f);
        }
        var lines = new MemoryLayer
        {
            Name = "S-101 (lines)",
            Features = lineFeatures,
        };

        return (areas, lines);
    }

    private static IFeature TaggedFeature(string featureType)
    {
        // PointFeature is the simplest IFeature in Mapsui — the rule
        // engine only reads dictionary attributes, never geometry.
        var f = new PointFeature(0, 0);
        f[FeatureTagKeys.FeatureType] = featureType;
        return f;
    }
}
