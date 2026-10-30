using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The pick report's order (S-98 15.1, #1159): drawing priority, highest
/// first, then points, curves, surfaces; undrawn features last.
/// </summary>
public sealed class PickOrderTests
{
    [Theory]
    [InlineData(24, 18, -1)]
    [InlineData(3, 12, 1)]
    [InlineData(5, null, -1)]
    [InlineData(null, 5, 1)]
    [InlineData(null, null, 0)]
    public void Higher_priorities_come_first_and_none_last(int? a, int? b, int expected) =>
        Assert.Equal(expected, Math.Sign(PickOrder.ComparePriority(a, b)));

    [Fact]
    public void Equal_priorities_rank_points_then_curves_then_surfaces()
    {
        Assert.True(PickOrder.Compare(12, S100GeometryType.Point, 12, S100GeometryType.Curve) < 0);
        Assert.True(PickOrder.Compare(12, S100GeometryType.Curve, 12, S100GeometryType.Surface) < 0);
        Assert.True(PickOrder.Compare(12, S100GeometryType.Surface, 15, S100GeometryType.Point) > 0);
    }

    [Fact]
    public void A_features_priority_is_its_highest_instruction()
    {
        DrawingInstruction Instruction(string feature, int priority) => new PointInstruction
        {
            FeatureReference = feature,
            DrawingPriority = priority,
        };

        var priorities = S101DatasetProcessor.DrawingPriorities(
            [Instruction("1", 12), Instruction("1", 24), Instruction("2", 3), Instruction("", 30)]);

        Assert.Equal(24, priorities["1"]);
        Assert.Equal(3, priorities["2"]);
        Assert.Equal(2, priorities.Count);
    }
}
