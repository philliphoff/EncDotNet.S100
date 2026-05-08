using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

public class DisplayPlaneControllerTests
{
    [Fact]
    public void AllPlanesVisibleByDefault()
    {
        var ctrl = new DisplayPlaneController();
        Assert.True(ctrl.IsVisible(DisplayPlane.UnderRadar));
        Assert.True(ctrl.IsVisible(DisplayPlane.OverRadar));
        Assert.Empty(ctrl.HiddenPlanes);
    }

    [Theory]
    [InlineData(DisplayPlane.UnderRadar)]
    [InlineData(DisplayPlane.OverRadar)]
    public void SetVisible_False_HidesPlane(DisplayPlane plane)
    {
        var ctrl = new DisplayPlaneController();
        ctrl.SetVisible(plane, false);
        Assert.False(ctrl.IsVisible(plane));
        Assert.Contains(plane, ctrl.HiddenPlanes);
    }

    [Fact]
    public void SetVisible_True_ShowsPlane()
    {
        var ctrl = new DisplayPlaneController();
        ctrl.SetVisible(DisplayPlane.OverRadar, false);
        ctrl.SetVisible(DisplayPlane.OverRadar, true);
        Assert.True(ctrl.IsVisible(DisplayPlane.OverRadar));
    }

    [Fact]
    public void ShowAll_ClearsHiddenSet()
    {
        var ctrl = new DisplayPlaneController();
        ctrl.SetVisible(DisplayPlane.UnderRadar, false);
        ctrl.SetVisible(DisplayPlane.OverRadar, false);
        ctrl.ShowAll();
        Assert.Empty(ctrl.HiddenPlanes);
    }

    [Fact]
    public void Changed_Fires_OnHide()
    {
        var ctrl = new DisplayPlaneController();
        int fired = 0;
        ctrl.Changed += () => fired++;
        ctrl.SetVisible(DisplayPlane.UnderRadar, false);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenNoChange()
    {
        var ctrl = new DisplayPlaneController();
        ctrl.SetVisible(DisplayPlane.UnderRadar, false);
        int fired = 0;
        ctrl.Changed += () => fired++;
        // Already hidden — should not fire
        ctrl.SetVisible(DisplayPlane.UnderRadar, false);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ShowAll_DoesNotFire_WhenAlreadyAllVisible()
    {
        var ctrl = new DisplayPlaneController();
        int fired = 0;
        ctrl.Changed += () => fired++;
        ctrl.ShowAll();
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ShowAll_Fires_WhenPlanesHidden()
    {
        var ctrl = new DisplayPlaneController();
        ctrl.SetVisible(DisplayPlane.OverRadar, false);
        int fired = 0;
        ctrl.Changed += () => fired++;
        ctrl.ShowAll();
        Assert.Equal(1, fired);
    }
}
