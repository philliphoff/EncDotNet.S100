using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins the PR-L1 plane-assignment table and verifies the stable
/// sort + tiebreaker semantics required by
/// <see cref="LayerStackBuilder"/>.
/// </summary>
public class InteroperabilityAuthorityTests
{
    private readonly InteroperabilityAuthority _auth = new();

    [Theory]
    [InlineData("S-101", null, S98DisplayPlane.BaseChartOver)]
    [InlineData("S-101", "area", S98DisplayPlane.BaseChartUnder)]
    [InlineData("S-101", "linework", S98DisplayPlane.BaseChartOver)]
    [InlineData("S-57", null, S98DisplayPlane.BaseChartOver)]
    [InlineData("S-102", null, S98DisplayPlane.Bathymetry)]
    [InlineData("S-104", null, S98DisplayPlane.OnDemandSurface)]
    [InlineData("S-104", "s104.color-band", S98DisplayPlane.OnDemandSurface)]
    [InlineData("S-104", "s104.stations", S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-111", null, S98DisplayPlane.DynamicArrows)]
    [InlineData("S-111", "s111.arrows", S98DisplayPlane.DynamicArrows)]
    [InlineData("S-111", "s111.stations", S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-122", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-124", null, S98DisplayPlane.CautionsAndWarnings)]
    [InlineData("S-125", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-127", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-128", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-129", null, S98DisplayPlane.OnDemandSurface)]
    [InlineData("S-131", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-201", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-411", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-421", null, S98DisplayPlane.OtherChartOverlays)]
    [InlineData("S-999", null, S98DisplayPlane.OtherChartOverlays)]
    public void GetDefaultPlane_returns_expected_plane(string productSpec, string? kind, S98DisplayPlane expected)
    {
        Assert.Equal(expected, _auth.GetDefaultPlane(productSpec, kind));
    }

    [Fact]
    public void Sort_orders_by_plane_ascending()
    {
        var a = Entry("a", S98DisplayPlane.OtherChartOverlays);
        var b = Entry("b", S98DisplayPlane.BaseChartUnder);
        var c = Entry("c", S98DisplayPlane.Bathymetry);

        var sorted = _auth.Sort(new[] { a, b, c });

        Assert.Equal(new[] { "b", "c", "a" }, sorted.Select(e => e.SourceDatasetId).ToArray());
    }

    [Fact]
    public void Sort_uses_within_plane_priority_after_plane()
    {
        var a = Entry("a", S98DisplayPlane.OtherChartOverlays, priority: 10);
        var b = Entry("b", S98DisplayPlane.OtherChartOverlays, priority: 0);
        var c = Entry("c", S98DisplayPlane.OtherChartOverlays, priority: 5);

        var sorted = _auth.Sort(new[] { a, b, c });

        Assert.Equal(new[] { "b", "c", "a" }, sorted.Select(e => e.SourceDatasetId).ToArray());
    }

    [Fact]
    public void Sort_is_stable_when_plane_and_priority_match()
    {
        // Three datasets that all land on the same plane at the same
        // priority should preserve input order — that's the
        // dataset-load-order tiebreaker.
        var a = Entry("a", S98DisplayPlane.OtherChartOverlays);
        var b = Entry("b", S98DisplayPlane.OtherChartOverlays);
        var c = Entry("c", S98DisplayPlane.OtherChartOverlays);

        var sorted = _auth.Sort(new[] { a, b, c });

        Assert.Equal(new[] { "a", "b", "c" }, sorted.Select(e => e.SourceDatasetId).ToArray());
    }

    [Fact]
    public void Build_top_of_ui_dataset_wins_within_plane_tie()
    {
        // Two single-layer datasets on the same plane: the dataset
        // at the top of the UI (index 0 of perDataset) must paint
        // LAST (highest index in output), i.e. on top.
        var topUi = new[] { Entry("top", S98DisplayPlane.OtherChartOverlays) };
        var bottomUi = new[] { Entry("bottom", S98DisplayPlane.OtherChartOverlays) };

        var sorted = LayerStackBuilder.Build(_auth, new IReadOnlyList<LayerStackEntry>[] { topUi, bottomUi });

        Assert.Equal(new[] { "bottom", "top" }, sorted.Select(e => e.SourceDatasetId).ToArray());
    }

    [Fact]
    public void Build_interleaves_across_planes_independent_of_load_order()
    {
        // S-102 loaded BEFORE S-101 must still appear between S-101
        // area fills and S-101 line work.
        var s102 = new[] { Entry("s102", S98DisplayPlane.Bathymetry) };
        var s101areas = Entry("s101a", S98DisplayPlane.BaseChartUnder);
        var s101lines = Entry("s101l", S98DisplayPlane.BaseChartOver);
        var s101 = new[] { s101areas, s101lines };

        // s101 at top of UI, s102 below.
        var sorted = LayerStackBuilder.Build(_auth, new IReadOnlyList<LayerStackEntry>[] { s101, s102 });

        Assert.Equal(new[] { "s101a", "s102", "s101l" }, sorted.Select(e => e.SourceDatasetId).ToArray());
    }

    [Fact]
    public void LoadOrderAuthority_ignores_plane_and_uses_strict_dataset_ordering()
    {
        // Same input as the interleave test above, but the alternative
        // authority must paint every layer of the bottom-of-UI dataset
        // first, then every layer of the top-of-UI dataset — independent
        // of S-98 plane.
        var s102 = new[] { Entry("s102", S98DisplayPlane.Bathymetry) };
        var s101areas = Entry("s101a", S98DisplayPlane.BaseChartUnder);
        var s101lines = Entry("s101l", S98DisplayPlane.BaseChartOver);
        var s101 = new[] { s101areas, s101lines };

        var sorted = LayerStackBuilder.Build(
            new LoadOrderInteroperabilityAuthority(new InteroperabilityAuthority()),
            new IReadOnlyList<LayerStackEntry>[] { s101, s102 });

        // Bottom-of-UI dataset (s102) paints first, then s101's two
        // layers in processor-emitted order (areas first, lines on top).
        Assert.Equal(new[] { "s102", "s101a", "s101l" }, sorted.Select(e => e.SourceDatasetId).ToArray());
    }

    [Fact]
    public void LoadOrderAuthority_still_exposes_S98_default_planes_for_labels()
    {
        // The load-order authority's GetDefaultPlane delegates to the
        // S-98 oracle so layer-controls UIs can still annotate layers
        // with their conceptual plane even when sort order ignores it.
        var lo = new LoadOrderInteroperabilityAuthority(new InteroperabilityAuthority());
        Assert.Equal(S98DisplayPlane.Bathymetry, lo.GetDefaultPlane("S-102"));
        Assert.Equal(S98DisplayPlane.CautionsAndWarnings, lo.GetDefaultPlane("S-124"));
        Assert.Equal(S98DisplayPlane.BaseChartUnder, lo.GetDefaultPlane("S-101", "area"));
    }

    private static LayerStackEntry Entry(string id, S98DisplayPlane plane, int priority = 0)
        => new(new MemoryLayer(id), plane, priority, id);
}
