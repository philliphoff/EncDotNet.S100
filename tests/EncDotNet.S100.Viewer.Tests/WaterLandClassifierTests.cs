using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class WaterLandClassifierTests
{
    private readonly WaterLandClassifier _classifier = new();

    private static PickHit Hit(string featureType, string spec = "S-101") => new()
    {
        FeatureType = featureType,
        FeatureRef = $"{featureType}.1",
        ProductSpec = spec,
    };

    [Fact]
    public void Classify_depth_area_is_water()
    {
        Assert.Equal(WaterLandClass.Water, _classifier.Classify([Hit("DepthArea")], s102CoversPoint: false));
    }

    [Fact]
    public void Classify_dredged_area_is_water()
    {
        Assert.Equal(WaterLandClass.Water, _classifier.Classify([Hit("DredgedArea")], s102CoversPoint: false));
    }

    [Fact]
    public void Classify_unsurveyed_area_is_water()
    {
        Assert.Equal(WaterLandClass.Water, _classifier.Classify([Hit("UnsurveyedArea")], s102CoversPoint: false));
    }

    [Fact]
    public void Classify_land_area_is_land()
    {
        Assert.Equal(WaterLandClass.Land, _classifier.Classify([Hit("LandArea")], s102CoversPoint: false));
    }

    [Fact]
    public void Classify_water_wins_over_land_at_boundary()
    {
        var result = _classifier.Classify([Hit("LandArea"), Hit("DepthArea")], s102CoversPoint: false);

        Assert.Equal(WaterLandClass.Water, result);
    }

    [Fact]
    public void Classify_falls_back_to_water_when_s102_covers_and_no_s101_area()
    {
        var result = _classifier.Classify([Hit("LightAllAround")], s102CoversPoint: true);

        Assert.Equal(WaterLandClass.Water, result);
    }

    [Fact]
    public void Classify_suppresses_when_no_s101_area_and_no_s102()
    {
        var result = _classifier.Classify([Hit("LightAllAround")], s102CoversPoint: false);

        Assert.Equal(WaterLandClass.Unknown, result);
    }

    [Fact]
    public void Classify_suppresses_on_empty_hits_without_s102()
    {
        Assert.Equal(WaterLandClass.Unknown, _classifier.Classify([], s102CoversPoint: false));
    }

    [Fact]
    public void Classify_ignores_non_s101_areas_with_same_name()
    {
        // A same-named area from another product must not drive S-101 group-1
        // classification; with no S-102 coverage the result is suppressed.
        var result = _classifier.Classify([Hit("DepthArea", spec: "S-57")], s102CoversPoint: false);

        Assert.Equal(WaterLandClass.Unknown, result);
    }

    [Fact]
    public void Classify_land_takes_precedence_over_s102_fallback()
    {
        // The S-102 fallback only applies when no S-101 group-1 area is present.
        var result = _classifier.Classify([Hit("LandArea")], s102CoversPoint: true);

        Assert.Equal(WaterLandClass.Land, result);
    }
}
