using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

public class DisplayModeControllerTests
{
    [Fact]
    public void SetActive_NewValue_RaisesChangedAndExposesId()
    {
        var c = new DisplayModeController();
        var raised = 0;
        c.Changed += () => raised++;

        c.SetActive("DisplayBase");

        Assert.Equal("DisplayBase", c.ActiveDisplayModeId);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetActive_RepeatedSameValue_DoesNotRaise()
    {
        var c = new DisplayModeController();
        c.SetActive("Standard");

        var raised = 0;
        c.Changed += () => raised++;

        c.SetActive("Standard");

        Assert.Equal(0, raised);
    }

    [Fact]
    public void SetActive_Null_ClearsAndRaises()
    {
        var c = new DisplayModeController();
        c.SetActive("Standard");

        var raised = 0;
        c.Changed += () => raised++;

        c.SetActive(null);

        Assert.Null(c.ActiveDisplayModeId);
        Assert.Equal(1, raised);
    }
}

public class ViewingGroupControllerTests
{
    [Fact]
    public void DefaultState_NoModeNoOverrides_AllVisible()
    {
        var v = new ViewingGroupController();
        Assert.True(v.IsVisible(11010));
        Assert.True(v.IsVisible(99999));
    }

    [Fact]
    public void ActiveModeMembership_OnlyMembersVisible()
    {
        var v = new ViewingGroupController();
        v.SetActiveModeMembership(new HashSet<int> { 1, 2, 3 });

        Assert.True(v.IsVisible(2));
        Assert.False(v.IsVisible(99));
    }

    [Fact]
    public void UserOverride_TrumpsModeMembership()
    {
        var v = new ViewingGroupController();
        v.SetActiveModeMembership(new HashSet<int> { 1 });

        // User overrides 99 (not in membership) ON
        v.SetUserOverride(99, true);
        Assert.True(v.IsVisible(99));

        // User overrides 1 (in membership) OFF
        v.SetUserOverride(1, false);
        Assert.False(v.IsVisible(1));
    }

    [Fact]
    public void ClearUserOverrides_RestoresModeMembership()
    {
        var v = new ViewingGroupController();
        v.SetActiveModeMembership(new HashSet<int> { 1 });
        v.SetUserOverride(1, false);

        v.ClearUserOverrides();

        Assert.True(v.IsVisible(1));
    }

    [Fact]
    public void Changed_FiresOnAllMutations()
    {
        var v = new ViewingGroupController();
        var raised = 0;
        v.Changed += () => raised++;

        v.SetActiveModeMembership(new HashSet<int> { 1 });
        v.SetUserOverride(2, true);
        v.SetUserOverride(2, true); // no-op
        v.SetUserOverride(2, null); // clear
        v.ClearUserOverrides();      // already empty after clear -> no-op

        Assert.Equal(3, raised);
    }

    [Fact]
    public void SetVisible_IsAliasForUserOverride()
    {
        var v = new ViewingGroupController();
        v.SetActiveModeMembership(new HashSet<int> { 1 });

        v.SetVisible(1, false);

        Assert.False(v.IsVisible(1));
    }
}
