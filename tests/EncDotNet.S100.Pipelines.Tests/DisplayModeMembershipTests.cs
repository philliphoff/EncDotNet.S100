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

    /// <summary>
    /// Catalogue mirroring the S-411 shape: the (only) declared display modes
    /// reference viewing-group layers whose sole viewing group id is
    /// non-numeric, so <see cref="DisplayModeMembership.Resolve"/> yields an
    /// <em>empty</em> integer membership. Reproduces issue #416.
    /// </summary>
    private static PortrayalCatalogue MakeEmptyMembershipCatalogue() => new()
    {
        ProductId = "S-411",
        Version = "1.2.1",
        ViewingGroups =
        [
            new ViewingGroup { Id = "IceStandardViewingGroup", Description = new Description { Name = "Ice" } },
        ],
        ViewingGroupLayers =
        [
            new ViewingGroupLayer
            {
                Id = "IceStandardViewingGroupLayer",
                Description = new Description { Name = "Ice" },
                ViewingGroupIds = ["IceStandardViewingGroup"],
            },
        ],
        DisplayModes =
        [
            new DisplayMode
            {
                Id = "IceScientificIceactDisplayMode",
                Description = new Description { Name = "Concentration" },
                ViewingGroupLayerIds = ["IceStandardViewingGroupLayer"],
            },
        ],
    };

    [Fact]
    public void Bind_ModeResolvingToEmptyMembership_TreatedAsNoFilter()
    {
        // Regression for issue #416: an S-411 display mode resolves to an empty
        // integer viewing-group set (its only viewing group id is non-numeric).
        // The adapter emits instructions under the numeric viewing group 27000,
        // so treating the empty membership as "hide everything" would blank the
        // whole chart. Bind must instead treat empty as "no filter".
        var catalogue = MakeEmptyMembershipCatalogue();
        var vg = new ViewingGroupController();
        var dm = new DisplayModeController();
        DisplayModeMembership.Bind(dm, vg, catalogue);

        dm.SetActive("IceScientificIceactDisplayMode");

        Assert.True(vg.IsVisible(27000));   // adapter's viewing group stays visible
        Assert.True(vg.IsVisible(99999));   // nothing is filtered out
    }

    [Fact]
    public void Bind_NumericMembershipMode_StillFilters()
    {
        // The empty-membership relaxation must not weaken normal (S-101-style)
        // filtering, where modes resolve to real numeric viewing groups.
        var catalogue = MakeCatalogue();
        var vg = new ViewingGroupController();
        var dm = new DisplayModeController();
        DisplayModeMembership.Bind(dm, vg, catalogue);

        dm.SetActive("DisplayBase");

        Assert.True(vg.IsVisible(11010));
        Assert.False(vg.IsVisible(11020));  // still filtered out
        Assert.False(vg.IsVisible(21030));
    }
}
