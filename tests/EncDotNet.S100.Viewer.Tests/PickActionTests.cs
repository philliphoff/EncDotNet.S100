using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Pick actions in the pick report (data source design note P4), and "Take
/// helm" as one.
/// </summary>
public sealed class PickActionTests
{
    private static VesselSection Vessel(string? mmsi) => new("SEA STAR", mmsi, 90, 92, 11.5, 180, 30);

    [Fact]
    public void The_selected_hits_actions_are_buttons_that_ask_the_host()
    {
        var vm = TestPickReport.Create();
        var action = new PickAction("test.do", "Do it", _ => true);
        var hit = new PickHit { FeatureType = "vessel.ais.cargo", FeatureRef = "ais:366000001", Actions = [action] };
        PickActionRequest? requested = null;
        vm.PickActionRequested += (_, r) => requested = r;

        vm.SetPicks([hit]);

        var button = Assert.Single(vm.SelectedActions);
        Assert.True(vm.HasActions);
        Assert.Equal("Do it", button.Label);
        Assert.Equal("PickReport.Action.test.do", button.AutomationId);
        vm.InvokeActionCommand.Execute(button);
        Assert.Same(hit, requested!.Hit);
        Assert.Same(action, requested.Action);

        vm.SetPicks([new PickHit { FeatureType = "DepthArea", FeatureRef = "42" }]);
        Assert.Empty(vm.SelectedActions);
        Assert.False(vm.HasActions);
    }

    [Theory]
    [InlineData("366000001", 366000001u)]
    [InlineData(null, null)]
    [InlineData("not-a-number", null)]
    public void Take_helm_reads_the_vessels_MMSI(string? mmsi, uint? expected)
    {
        Assert.Equal(expected, TakeHelmPickAction.MmsiOf([Vessel(mmsi)]));
    }

    [Fact]
    public void Take_helm_is_offered_only_for_a_vessel_with_an_MMSI()
    {
        var registry = new PickActionRegistry();
        TakeHelmPickAction.Register(registry);
        TakeHelmPickAction.Register(registry); // Once only.

        PickContext Context(params PickSection[] sections) => new()
        {
            Spec = new EncDotNet.S100.Core.SpecRef(EncDotNet.S100.Core.SpecName.Nmea, default),
            FeatureRef = "f",
            FeatureType = "t",
            Attributes = [],
            Sections = sections,
        };

        Assert.Single(registry.Actions);
        Assert.Equal(TakeHelmPickAction.Id, Assert.Single(registry.ActionsFor(Context(Vessel("366000001")))).Id);
        Assert.Empty(registry.ActionsFor(Context(Vessel(null))));
        Assert.Empty(registry.ActionsFor(Context()));
    }
}
