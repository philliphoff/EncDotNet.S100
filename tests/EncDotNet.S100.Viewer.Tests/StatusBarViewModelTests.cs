using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class StatusBarViewModelTests
{
    private static StatusBarViewModel Create() =>
        new(mcpServerHost: null, Notifications.TestNotifications.Create(), post: action => action());

    [Fact]
    public void UpdateOverscale_ShowsWorstCellFirst_ThenHidesWhenNoneOverscaled()
    {
        var bar = Create();
        Assert.False(bar.IsOverscaleVisible);

        bar.UpdateOverscale(new OverscaleReport
        {
            OverscaledCells = [new OverscaleCell("US5WA22M", 4.6), new OverscaleCell("US4WA30M", 2.0)],
            CellsInViewCount = 3,
        });

        Assert.True(bar.IsOverscaleVisible);
        Assert.NotEmpty(bar.OverscaleFactorText);
        Assert.Equal(["US5WA22M", "US4WA30M"], bar.OverscaleCells.Select(c => c.Name));

        bar.UpdateOverscale(OverscaleReport.None);

        Assert.False(bar.IsOverscaleVisible);
        Assert.Empty(bar.OverscaleFactorText);
        Assert.Empty(bar.OverscaleCells);
    }

    [Fact]
    public void CursorAndScaleReadouts_AreVisibleOnlyWithText()
    {
        var bar = Create();
        Assert.False(bar.IsMapScaleVisible);

        bar.MapScaleText = "1:180 000";
        bar.MouseLatText = string.Empty;

        Assert.True(bar.IsMapScaleVisible);
        Assert.False(bar.IsMouseLatLonVisible);
    }

    [Fact]
    public void McpStatus_IsHiddenWithoutAServer()
    {
        var bar = Create();

        Assert.False(bar.IsMcpRunning);
        Assert.Empty(bar.McpStatusText);
        Assert.Empty(bar.McpTooltipText);
    }
}
