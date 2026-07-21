using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class BaseDepthResolverTests
{
    private readonly BaseDepthResolver _resolver = new();

    private static PickHit AreaHit(string featureType, double minDepthMetres) => new()
    {
        FeatureType = featureType,
        FeatureRef = $"{featureType}.1",
        ProductSpec = "S-101",
        Attributes =
        [
            new PickAttribute
            {
                Code = "depthRangeMinimumValue",
                RawValue = minDepthMetres.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DepthMetresValue = minDepthMetres,
            },
        ],
    };

    private static S101SoundingSample Sounding(double depth, double distance) =>
        new(new GeoPosition(51.9, 4.4), depth, distance);

    [Fact]
    public void Resolve_prefers_bathymetry_over_all_vector_sources()
    {
        var result = _resolver.Resolve(
            new S102DepthSample(12.5, 0.3, VerticalDatumCode: 10),
            [AreaHit("DredgedArea", 9.0), AreaHit("DepthArea", 7.0)],
            Sounding(6.0, 5.0));

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.Bathymetry, result!.Source);
        Assert.Equal(12.5, result.DepthMeters);
        Assert.Equal(0.3, result.UncertaintyMeters);
        Assert.Equal(10, result.VerticalDatumCode);
        Assert.Null(result.SoundingDistanceMeters);
    }

    [Fact]
    public void Resolve_prefers_dredged_area_over_depth_area_and_sounding()
    {
        var result = _resolver.Resolve(
            bathymetry: null,
            [AreaHit("DepthArea", 7.0), AreaHit("DredgedArea", 9.0)],
            Sounding(6.0, 5.0));

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.DredgedArea, result!.Source);
        Assert.Equal(9.0, result.DepthMeters);
        Assert.Null(result.UncertaintyMeters);
        Assert.Null(result.VerticalDatumCode);
    }

    [Fact]
    public void Resolve_uses_depth_area_when_no_bathymetry_or_dredged()
    {
        var result = _resolver.Resolve(
            bathymetry: null,
            [AreaHit("DepthArea", 7.0)],
            Sounding(6.0, 5.0));

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.DepthArea, result!.Source);
        Assert.Equal(7.0, result.DepthMeters);
    }

    [Fact]
    public void Resolve_falls_through_to_nearest_sounding()
    {
        var result = _resolver.Resolve(
            bathymetry: null,
            [],
            Sounding(6.0, 42.0));

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.Sounding, result!.Source);
        Assert.Equal(6.0, result.DepthMeters);
        Assert.Equal(42.0, result.SoundingDistanceMeters);
    }

    [Fact]
    public void Resolve_returns_null_when_no_source_available()
    {
        var result = _resolver.Resolve(bathymetry: null, [], nearestSounding: null);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_takes_shoalest_when_multiple_depth_areas_overlap()
    {
        var result = _resolver.Resolve(
            bathymetry: null,
            [AreaHit("DepthArea", 12.0), AreaHit("DepthArea", 5.0), AreaHit("DepthArea", 8.0)],
            nearestSounding: null);

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.DepthArea, result!.Source);
        Assert.Equal(5.0, result.DepthMeters);
    }

    [Fact]
    public void Resolve_handles_drying_depth_area_with_negative_minimum()
    {
        var result = _resolver.Resolve(
            bathymetry: null,
            [AreaHit("DepthArea", -1.5)],
            nearestSounding: null);

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.DepthArea, result!.Source);
        Assert.Equal(-1.5, result.DepthMeters);
    }

    [Fact]
    public void Resolve_reads_legacy_drval1_code()
    {
        var hit = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureRef = "DepthArea.1",
            Attributes =
            [
                new PickAttribute
                {
                    Code = "DRVAL1",
                    RawValue = "4.0",
                    DepthMetresValue = 4.0,
                },
            ],
        };

        var result = _resolver.Resolve(bathymetry: null, [hit], nearestSounding: null);

        Assert.NotNull(result);
        Assert.Equal(4.0, result!.DepthMeters);
    }

    [Fact]
    public void Resolve_ignores_area_without_depth_metres_value()
    {
        var hit = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureRef = "DepthArea.1",
            Attributes =
            [
                new PickAttribute { Code = "depthRangeMinimumValue", RawValue = "not-a-number" },
            ],
        };

        var result = _resolver.Resolve(bathymetry: null, [hit], Sounding(3.0, 1.0));

        Assert.NotNull(result);
        Assert.Equal(BaseDepthSource.Sounding, result!.Source);
    }
}
