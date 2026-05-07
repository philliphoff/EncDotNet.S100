using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class EcdisDisplayStateTests
{
    [Fact]
    public void DefaultsToStandardWithNoOverrides()
    {
        var state = new EcdisDisplayState();
        Assert.Equal(EcdisDisplayCategory.Standard, state.Category);
        Assert.Empty(state.GetHidden("S-101"));
    }

    [Fact]
    public void SetCategory_RaisesChangedOnceOnRealMutation()
    {
        var state = new EcdisDisplayState();
        var raised = 0;
        state.Changed += () => raised++;

        state.SetCategory(EcdisDisplayCategory.DisplayBase);
        state.SetCategory(EcdisDisplayCategory.DisplayBase); // no-op

        Assert.Equal(1, raised);
        Assert.Equal(EcdisDisplayCategory.DisplayBase, state.Category);
    }

    [Fact]
    public void HideThenShow_TogglesVisibilityAndRaisesChanged()
    {
        var state = new EcdisDisplayState();
        var raised = 0;
        state.Changed += () => raised++;

        state.HideViewingGroup("S-101", 11010);
        state.HideViewingGroup("S-101", 11010); // no-op duplicate
        Assert.Contains(11010, state.GetHidden("S-101"));

        state.ShowViewingGroup("S-101", 11010);
        Assert.Empty(state.GetHidden("S-101"));

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Snapshot_DetachedFromMutations()
    {
        var state = new EcdisDisplayState();
        state.SetCategory(EcdisDisplayCategory.OtherInformation);
        state.HideViewingGroup("S-101", 100);

        var snap = state.Snapshot();

        state.SetCategory(EcdisDisplayCategory.All);
        state.HideViewingGroup("S-101", 200);

        Assert.Equal(EcdisDisplayCategory.OtherInformation, snap.Category);
        Assert.Equal(new HashSet<int> { 100 }, snap.HiddenViewingGroups["S-101"]);
    }

    [Fact]
    public void ClearAllOverrides_RemovesEveryHiddenGroup()
    {
        var state = new EcdisDisplayState();
        state.HideViewingGroup("S-101", 1);
        state.HideViewingGroup("S-122", 2);

        state.ClearAllOverrides();

        Assert.Empty(state.GetHidden("S-101"));
        Assert.Empty(state.GetHidden("S-122"));
    }

    [Fact]
    public void Hydrate_RestoresStateAndRaisesChangedOnce()
    {
        var state = new EcdisDisplayState();
        var raised = 0;
        state.Changed += () => raised++;

        var hidden = new Dictionary<string, IReadOnlySet<int>>
        {
            ["S-101"] = new HashSet<int> { 11010, 11020 },
        };

        state.Hydrate(EcdisDisplayCategory.DisplayBase, hidden);

        Assert.Equal(EcdisDisplayCategory.DisplayBase, state.Category);
        Assert.Equal(new HashSet<int> { 11010, 11020 }, state.GetHidden("S-101"));
        Assert.Equal(1, raised);
    }
}

public class ViewerSettingsEcdisRoundTripTests
{
    [Fact]
    public void EcdisFields_RoundTripThroughLoadSave()
    {
        var path = Path.Combine(Path.GetTempPath(), "ecdis-rt-" + Path.GetRandomFileName() + ".json");
        try
        {
            var s1 = new ViewerSettings { SettingsFilePath = path };
            s1.EcdisDisplayCategory = "DisplayBase";
            s1.EcdisHiddenViewingGroups["S-101"] = "11010,11020";
            s1.EcdisHiddenViewingGroups["S-122"] = "200";
            s1.Save();

            var s2 = ViewerSettings.Load(path);
            Assert.Equal("DisplayBase", s2.EcdisDisplayCategory);
            Assert.Equal("11010,11020", s2.EcdisHiddenViewingGroups["S-101"]);
            Assert.Equal("200", s2.EcdisHiddenViewingGroups["S-122"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
