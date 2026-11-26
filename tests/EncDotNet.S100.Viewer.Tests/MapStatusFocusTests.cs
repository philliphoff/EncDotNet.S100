using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="MapStatusFocus"/> (#1732): the status bar describes the map
/// under the pointer, else the active map.
/// </summary>
public sealed class MapStatusFocusTests
{
    private readonly MapViews _views = new(
        new MapCapabilityAccessor<IMapViewportController>(),
        new MapCapabilityAccessor<IMapCoordinateConverter>(),
        new MapCapabilityAccessor<IImageRenderer>(),
        new DemandPlannerSet());

    private readonly MapViewsTests.FakeMapView _one = new("map-1");
    private readonly MapViewsTests.FakeMapView _two = new("map-2");

    private MapStatusFocus TwoMaps()
    {
        var focus = new MapStatusFocus(_views);
        _views.Add(_one);
        _views.Add(_two);
        _views.Activate(_two);
        return focus;
    }

    [Fact]
    public void With_the_pointer_over_no_map_it_is_the_active_map()
    {
        var focus = TwoMaps();

        Assert.Same(_two, focus.Current);
        Assert.True(focus.IsCurrent(_two));
        Assert.False(focus.IsCurrent(_one));
    }

    [Fact]
    public void The_map_under_the_pointer_wins_over_the_active_map()
    {
        var focus = TwoMaps();
        var changes = 0;
        focus.Changed += (_, _) => changes++;

        focus.PointerEntered(_one);

        Assert.Same(_one, focus.Current);
        Assert.Equal(1, changes);

        focus.PointerLeft(_one);

        Assert.Same(_two, focus.Current);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Hovering_the_active_map_changes_nothing()
    {
        var focus = TwoMaps();
        var changes = 0;
        focus.Changed += (_, _) => changes++;

        focus.PointerEntered(_two);
        focus.PointerLeft(_two);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Activating_a_map_moves_it_only_while_the_pointer_is_over_no_map()
    {
        var focus = TwoMaps();
        var changes = 0;
        focus.Changed += (_, _) => changes++;

        focus.PointerEntered(_one);
        _views.Activate(_one);
        _views.Activate(_two);
        Assert.Equal(1, changes);
        Assert.Same(_one, focus.Current);

        focus.PointerLeft(_one);
        _views.Activate(_one);

        Assert.Equal(3, changes);
        Assert.Same(_one, focus.Current);
    }

    [Fact]
    public void A_late_leave_from_another_map_is_ignored()
    {
        var focus = TwoMaps();
        focus.PointerEntered(_one);

        focus.PointerLeft(_two);

        Assert.Same(_one, focus.Current);
    }

    [Fact]
    public void Removing_the_hovered_map_falls_back_to_the_active_map()
    {
        var focus = TwoMaps();
        focus.PointerEntered(_one);

        _views.Remove(_one);

        Assert.Null(focus.Hovered);
        Assert.Same(_two, focus.Current);
    }

    [AvaloniaFact]
    public void The_badge_sits_with_the_scale_when_no_position_is_shown()
    {
        var bar = new StatusBarViewModel(mcpServerHost: null, Notifications.TestNotifications.Create(), UiThread.Post)
        {
            MapScaleText = "1:12 000",
            MouseLatText = string.Empty,
        };
        Assert.True(bar.IsScaleSeparatorVisible);
        Assert.False(bar.IsPointerGroupVisible);

        bar.StatusMapLabel = "Map 2";

        Assert.True(bar.IsPointerGroupVisible);
        Assert.False(bar.IsScaleSeparatorVisible);

        bar.MouseLatText = "47°40.500'N";

        Assert.True(bar.IsScaleSeparatorVisible);
    }
}
