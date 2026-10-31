using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The pick panel orders each dataset's hits by drawing priority, then points,
/// curves and surfaces (S-98 15.1; S-164 FeatureInformation2, #1159).
/// </summary>
public sealed class PickServiceDrawingPriorityOrderTests
{
    private static PickHit Hit(string id, string dataset, int? priority, S100GeometryType geometry) => new()
    {
        FeatureType = "Feature",
        FeatureRef = id,
        DatasetFileName = dataset,
        DrawingPriority = priority,
        Geometry = geometry,
    };

    [Fact]
    public void Each_datasets_hits_follow_drawing_priority_then_geometry()
    {
        var hits = new List<PickHit>
        {
            Hit("fog", "a.000", 18, S100GeometryType.Point),
            Hit("depth", "a.000", 3, S100GeometryType.Surface),
            Hit("coverage", "a.000", null, S100GeometryType.Surface),
            Hit("light", "a.000", 24, S100GeometryType.Point),
            Hit("magvar", "a.000", 12, S100GeometryType.Surface),
            Hit("contour", "a.000", 12, S100GeometryType.Curve),
            Hit("other", "b.000", 30, S100GeometryType.Point),
        };

        var ordered = PickService.OrderByDrawingPriority(hits);

        Assert.Equal(["light", "fog", "contour", "magvar", "depth", "coverage", "other"], ordered.Select(h => h.FeatureRef));
    }

    [Fact]
    public void Hits_without_priorities_keep_the_stack_order()
    {
        var hits = new List<PickHit>
        {
            Hit("area", "a.gml", null, S100GeometryType.Surface),
            Hit("point", "a.gml", null, S100GeometryType.Point),
        };

        Assert.Equal(["area", "point"], PickService.OrderByDrawingPriority(hits).Select(h => h.FeatureRef));
    }
}
