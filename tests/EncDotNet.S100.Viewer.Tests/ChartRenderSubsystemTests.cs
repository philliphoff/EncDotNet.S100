using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Validates the minimal Phase&#160;0 <see cref="IChartRenderSubsystem"/> seam:
/// the factory selects the arm matching <see cref="RenderSubsystemKind"/>, each
/// arm reports a consistent identity + telemetry kind, and the lifecycle flag
/// tracks activation.
/// </summary>
public class ChartRenderSubsystemTests
{
    [Theory]
    [InlineData(RenderSubsystemKind.Mapsui, typeof(MapsuiChartRenderSubsystem))]
    [InlineData(RenderSubsystemKind.TiledScene, typeof(TiledSceneChartRenderSubsystem))]
    public void Factory_Creates_Arm_For_Kind(RenderSubsystemKind kind, System.Type expected)
    {
        var subsystem = ChartRenderSubsystemFactory.Create(kind);

        Assert.IsType(expected, subsystem);
        Assert.Equal(kind, subsystem.Kind);
        Assert.Equal(kind, subsystem.Telemetry.Kind);
        Assert.False(string.IsNullOrWhiteSpace(subsystem.DisplayName));
    }

    [Fact]
    public void Activate_Then_Deactivate_Tracks_IsActive()
    {
        var subsystem = ChartRenderSubsystemFactory.Create(RenderSubsystemKind.Mapsui);

        Assert.False(subsystem.IsActive);
        subsystem.Activate();
        Assert.True(subsystem.IsActive);
        subsystem.Deactivate();
        Assert.False(subsystem.IsActive);
    }

    [Fact]
    public void CreateActive_Matches_Flag()
    {
        var subsystem = ChartRenderSubsystemFactory.CreateActive();

        Assert.Equal(RenderingOptimizations.RenderSubsystem, subsystem.Kind);
    }
}
