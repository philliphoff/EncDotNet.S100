using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The draw order <see cref="VectorSceneBuilder"/> gives its ops (S-100 Part 9,
/// #1126): display plane first, then drawing priority, then areas, pattern
/// fills, lines, points and text within a priority.
/// </summary>
public sealed class VectorSceneBuilderDrawOrderTests
{
    /// <summary>
    /// The same square for every feature, except "pattern5", which is twice as
    /// wide. Lines share its edges, so they turn suppression off to all draw.
    /// </summary>
    private sealed class Square : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference)
        {
            double east = featureReference == "pattern5" ? -0.8 : -0.9;
            return new()
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new GeoPosition(50, -1),
                    new GeoPosition(50, east),
                    new GeoPosition(50.1, east),
                    new GeoPosition(50.1, -1),
                ],
            };
        }
    }

    private static IReadOnlyList<string> Order(params DrawingInstruction[] instructions)
    {
        var builder = new VectorSceneBuilder
        {
            // Translucent, so solid fills don't clip the patterns away.
            ResolveColor = static _ => new RgbaColor(200, 200, 200, 128),
            PatternResolver = static _ => [1, 2, 3, 4],
        };
        return builder.Build(instructions, new Square()).Ops.Select(o => o.FeatureReference).ToList();
    }

    [Fact]
    public void HigherPriorityArea_DrawsOverLowerPriorityLine()
    {
        Assert.Equal(
            ["line", "area"],
            Order(
                new AreaInstruction { FeatureReference = "area", FillColor = "DEPDW", DrawingPriority = 9 },
                new LineInstruction { FeatureReference = "line", LineColor = "CHBLK", Suppression = false, DrawingPriority = 3 }));
    }

    [Fact]
    public void WithinAPriority_AreasPatternsLinesPointsThenText()
    {
        Assert.Equal(
            ["area", "pattern", "line", "point", "text"],
            Order(
                new TextInstruction { FeatureReference = "text", Text = "T", DrawingPriority = 5 },
                new PointInstruction { FeatureReference = "point", SymbolReference = "S", DrawingPriority = 5 },
                new LineInstruction { FeatureReference = "line", LineColor = "CHBLK", Suppression = false, DrawingPriority = 5 },
                new AreaInstruction { FeatureReference = "pattern", AreaFillReference = "P", DrawingPriority = 5 },
                new AreaInstruction { FeatureReference = "area", FillColor = "DEPDW", DrawingPriority = 5 }));
    }

    [Fact]
    public void OverRadarPlane_DrawsAfterEveryPriorityUnderRadar()
    {
        Assert.Equal(
            ["under", "over"],
            Order(
                new LineInstruction { FeatureReference = "over", LineColor = "CHBLK", Suppression = false, DrawingPriority = 1, Plane = DisplayPlane.OverRadar },
                new LineInstruction { FeatureReference = "under", LineColor = "CHBLK", Suppression = false, DrawingPriority = 9 }));
    }

    [Fact]
    public void ClippedPatterns_KeepTheirPriorityPlace()
    {
        Assert.Equal(
            ["line3", "pattern5", "line8", "pattern9"],
            Order(
                new AreaInstruction { FeatureReference = "pattern9", AreaFillReference = "HIGH", DrawingPriority = 9 },
                new LineInstruction { FeatureReference = "line8", LineColor = "CHBLK", Suppression = false, DrawingPriority = 8 },
                new AreaInstruction { FeatureReference = "pattern5", AreaFillReference = "LOW", DrawingPriority = 5 },
                new LineInstruction { FeatureReference = "line3", LineColor = "CHBLK", Suppression = false, DrawingPriority = 3 }));
    }

    [Fact]
    public void Ties_KeepDisplayListOrder()
    {
        Assert.Equal(
            ["first", "second"],
            Order(
                new LineInstruction { FeatureReference = "first", LineColor = "CHBLK", Suppression = false, DrawingPriority = 4 },
                new LineInstruction { FeatureReference = "second", LineColor = "CHBLK", Suppression = false, DrawingPriority = 4 }));
    }
}
