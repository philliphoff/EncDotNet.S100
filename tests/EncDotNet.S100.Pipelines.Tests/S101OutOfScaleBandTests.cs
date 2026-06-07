using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Xunit;

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
            Coordinates = new (double, double)[] { (0, 0) },
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
    public void Resolve_MinimumDisplayScaleOnNonDataCoverage_IsIgnored()
    {
        var features = new[] { Feature("DepthArea", 1, ("minimumDisplayScale", "90000")) };

        Assert.Null(S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features));
    }

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
