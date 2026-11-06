using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the out-of-scale-band declutter helpers on
/// <see cref="S101DatasetProcessor"/>: the band cutoff is derived from the
/// <c>DataCoverage</c> feature's <c>minimumDisplayScale</c> attribute
/// (S-101 FC §3.1.1), and the per-feature cap only ever reduces visibility.
/// </summary>
public class S101OutOfScaleBandTests
{
    private static EncDotNet.S100.Pipelines.Vector.Feature Feature(
        string featureType, long id, params (string Key, object? Value)[] attributes)
    {
        var attrs = new Dictionary<string, object?>();
        foreach (var (key, value) in attributes)
            attrs[key] = value;

        return new EncDotNet.S100.Pipelines.Vector.Feature
        {
            FeatureType = featureType,
            Id = id,
            GeometryType = GeometryType.Surface,
            Coordinates = new[] { new GeoPosition(0, 0) },
            Attributes = attrs,
        };
    }

    [Fact]
    public void Resolve_SingleDataCoverage_ReturnsMinimumDisplayScaleDenominator()
    {
        var features = new[]
        {
            Feature("DepthArea", 1),
            Feature("DataCoverage", 2, ("minimumDisplayScale", "90000")),
        };

        var result = S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features);

        Assert.NotNull(result);
        Assert.Equal(90000, result!.Value);
    }

    [Fact]
    public void Resolve_NoDataCoverage_ReturnsNull()
    {
        var features = new[] { Feature("DepthArea", 1), Feature("Sounding", 2) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_DataCoverageWithoutAttribute_ReturnsNull()
    {
        var features = new[] { Feature("DataCoverage", 1) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_MalformedValue_ReturnsNull()
    {
        var features = new[] { Feature("DataCoverage", 1, ("minimumDisplayScale", "not-a-number")) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_NonPositiveValue_ReturnsNull()
    {
        var features = new[] { Feature("DataCoverage", 1, ("minimumDisplayScale", "0")) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_MultipleDataCoverage_UsesMostPermissiveLargestDenominator()
    {
        var features = new[]
        {
            Feature("DataCoverage", 1, ("minimumDisplayScale", "45000")),
            Feature("DataCoverage", 2, ("minimumDisplayScale", "180000")),
            Feature("DataCoverage", 3, ("minimumDisplayScale", "90000")),
        };

        var result = S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features);

        Assert.NotNull(result);
        Assert.Equal(180000, result!.Value);
    }

    [Fact]
    public void Resolve_InvertedPair_UsesCoarserDenominator()
    {
        // IHO S-101 test cells 101AA00DS0006/0007/0015 declare
        // minimumDisplayScale 22000 with maximumDisplayScale 90000. Read
        // literally, the cell vanished beyond 1:22 000 instead of 1:90 000.
        var features = new[]
        {
            Feature("DataCoverage", 1,
                ("minimumDisplayScale", "22000"), ("maximumDisplayScale", "90000")),
        };

        Assert.Equal(90000, S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_WellOrderedPair_UsesMinimumDisplayScale()
    {
        var features = new[]
        {
            Feature("DataCoverage", 1,
                ("minimumDisplayScale", "90000"), ("maximumDisplayScale", "22000")),
        };

        Assert.Equal(90000, S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_MaximumDisplayScaleOnly_ReturnsNull()
    {
        var features = new[] { Feature("DataCoverage", 1, ("maximumDisplayScale", "22000")) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    [Fact]
    public void Resolve_MinimumDisplayScaleOnNonDataCoverage_IsIgnored()
    {
        var features = new[] { Feature("DepthArea", 1, ("minimumDisplayScale", "90000")) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

    private sealed class PointGeometry : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => new()
        {
            Type = GeometryType.Point,
            Coordinates = [new GeoPosition(50.72, -1.29)],
        };
    }

    /// <summary>
    /// A tiled layer is drawn and picked from its bound scene, so the cap must
    /// land on the scene's ops: a missing SCAMIN inherits it, a looser one is
    /// tightened to it, and a tighter one is kept.
    /// </summary>
    [Theory]
    [InlineData(null, 90000.0)]
    [InlineData(200000.0, 90000.0)]
    [InlineData(45000.0, 45000.0)]
    public void Cap_TiledLayer_LandsOnTheBoundScene(double? scaleMinimum, double expected)
    {
        var layer = new MapsuiDisplayListRenderer
        {
            Palette = ColorPalette.Default,
            OutOfBandMinDisplayScale = 90000,
            Options = new S100MapsuiOptions { SceneMode = VectorSceneMode.Tiled },
        }.Render(
            [new PointInstruction { FeatureReference = "f1", SymbolReference = "SYM01", ScaleMinimum = scaleMinimum }],
            new PointGeometry());

        Assert.Empty(Assert.IsAssignableFrom<MemoryLayer>(layer).Features);
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(layer, out _, out var overlay));
        var op = Assert.IsType<PointPaintOp>(Assert.Single(overlay.Ops));
        Assert.Equal(expected, op.ScaleMinimum);
    }

    // The single-surface arm still picks through Mapsui features, so the cap
    // is stamped onto their styles as well (ApplyOutOfScaleBandCap).
    private static IFeature PointWithMaxVisible(double maxVisible)
    {
        var feature = new PointFeature(0, 0);
        feature.Styles.Add(new SymbolStyle { MaxVisible = maxVisible });
        return feature;
    }

    [Fact]
    public void Cap_DefaultUnboundedVisibility_IsCappedToBand()
    {
        var feature = PointWithMaxVisible(double.MaxValue);

        MapsuiDatasetRenderer.ApplyOutOfScaleBandCap(new[] { feature }, 25.2);

        Assert.Equal(25.2, GetMaxVisible(feature), 6);
    }

    [Fact]
    public void Cap_TighterScaminLimit_IsPreserved()
    {
        var feature = PointWithMaxVisible(10.0);

        MapsuiDatasetRenderer.ApplyOutOfScaleBandCap(new[] { feature }, 25.2);

        Assert.Equal(10.0, GetMaxVisible(feature), 6);
    }

    [Fact]
    public void Cap_LooserScaminLimit_IsReducedToBand()
    {
        var feature = PointWithMaxVisible(100.0);

        MapsuiDatasetRenderer.ApplyOutOfScaleBandCap(new[] { feature }, 25.2);

        Assert.Equal(25.2, GetMaxVisible(feature), 6);
    }

    [Fact]
    public void Cap_NonPositiveSentinel_IsLeftUntouched()
    {
        var feature = PointWithMaxVisible(0.0);

        MapsuiDatasetRenderer.ApplyOutOfScaleBandCap(new[] { feature }, 25.2);

        Assert.Equal(0.0, GetMaxVisible(feature), 6);
    }

    private static double GetMaxVisible(IFeature feature)
    {
        foreach (var style in feature.Styles)
            return style.MaxVisible;
        return double.NaN;
    }
}
