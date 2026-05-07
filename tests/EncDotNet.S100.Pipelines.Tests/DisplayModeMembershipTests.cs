using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

public class DisplayModeMembershipTests
{
    private static PortrayalCatalogue MakeCatalogue() => new()
    {
        ProductId = "S-101",
        Version = "1.2.0",
        ViewingGroups =
        [
            new ViewingGroup { Id = "11010", Description = new Description { Name = "cursor" } },
            new ViewingGroup { Id = "11020", Description = new Description { Name = "lights" } },
            new ViewingGroup { Id = "21030", Description = new Description { Name = "buoys" } },
        ],
        ViewingGroupLayers =
        [
            new ViewingGroupLayer
            {
                Id = "1",
                Description = new Description { Name = "Display Base" },
                ViewingGroupIds = ["11010"],
            },
            new ViewingGroupLayer
            {
                Id = "3a",
                Description = new Description { Name = "Buoys" },
                ViewingGroupIds = ["21030", "non-numeric"],
            },
            new ViewingGroupLayer
            {
                Id = "3b",
                Description = new Description { Name = "Lights" },
                ViewingGroupIds = ["11020"],
            },
        ],
        DisplayModes =
        [
            new DisplayMode
            {
                Id = "DisplayBase",
                Description = new Description { Name = "Display Base" },
                ViewingGroupLayerIds = ["1"],
            },
            new DisplayMode
            {
                Id = "StandardDisplay",
                Description = new Description { Name = "Standard" },
                ViewingGroupLayerIds = ["1", "3a", "3b", "missing-layer"],
            },
        ],
    };

    [Fact]
    public void Resolve_DisplayBase_ReturnsBaseLayerVgs()
    {
        var set = DisplayModeMembership.Resolve(MakeCatalogue(), "DisplayBase");

        Assert.Equal(new[] { 11010 }, set);
    }

    [Fact]
    public void Resolve_StandardDisplay_AggregatesAllLayers_SkipsMissingAndNonNumeric()
    {
        var set = DisplayModeMembership.Resolve(MakeCatalogue(), "StandardDisplay");

        Assert.Equal(new HashSet<int> { 11010, 11020, 21030 }, set);
    }

    [Fact]
    public void Resolve_UnknownMode_ReturnsEmpty()
    {
        var set = DisplayModeMembership.Resolve(MakeCatalogue(), "DoesNotExist");

        Assert.Empty(set);
    }

    [Fact]
    public void Bind_DisplayModeChange_UpdatesViewingGroupController()
    {
        var catalogue = MakeCatalogue();
        var vg = new ViewingGroupController();
        var dm = new DisplayModeController();

        DisplayModeMembership.Bind(dm, vg, catalogue);

        // Initial state: no mode → all visible.
        Assert.True(vg.IsVisible(99999));

        dm.SetActive("DisplayBase");
        Assert.True(vg.IsVisible(11010));
        Assert.False(vg.IsVisible(11020));
        Assert.False(vg.IsVisible(21030));

        dm.SetActive("StandardDisplay");
        Assert.True(vg.IsVisible(11010));
        Assert.True(vg.IsVisible(11020));
        Assert.True(vg.IsVisible(21030));

        dm.SetActive(null);
        Assert.True(vg.IsVisible(11010));
        Assert.True(vg.IsVisible(99999));
    }

    [Fact]
    public void Bind_PreservesUserOverridesAcrossModeChanges()
    {
        var catalogue = MakeCatalogue();
        var vg = new ViewingGroupController();
        var dm = new DisplayModeController();
        DisplayModeMembership.Bind(dm, vg, catalogue);

        dm.SetActive("DisplayBase");
        vg.SetUserOverride(11020, true);   // force-on
        vg.SetUserOverride(11010, false);  // force-off

        dm.SetActive("StandardDisplay");

        Assert.True(vg.IsVisible(11020));   // override still on
        Assert.False(vg.IsVisible(11010));  // override still off
        Assert.True(vg.IsVisible(21030));   // mode-driven, no override
    }
}
