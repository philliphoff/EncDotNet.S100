using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A per-dataset <see cref="ViewingGroupFilter"/> narrows what the display
/// mode and user overrides show, and never shows a group they hide.
/// </summary>
public class ViewingGroupFilterTests
{
    [Fact]
    public void ShowOnly_allows_only_its_groups_and_Hide_drops_its_groups()
    {
        var only = ViewingGroupFilter.ShowOnly([52270]);
        Assert.True(only.Allows(52270));
        Assert.False(only.Allows(52240));
        Assert.False(only.Allows(21010));

        var hide = ViewingGroupFilter.Hide([52240]);
        Assert.False(hide.Allows(52240));
        Assert.True(hide.Allows(52270));
    }

    [Fact]
    public void Filters_compare_by_value()
    {
        Assert.Equal(ViewingGroupFilter.ShowOnly([1, 2]), ViewingGroupFilter.ShowOnly([2, 1, 2]));
        Assert.Equal(ViewingGroupFilter.ShowOnly([1, 2]).GetHashCode(), ViewingGroupFilter.ShowOnly([2, 1]).GetHashCode());
        Assert.NotEqual(ViewingGroupFilter.ShowOnly([1]), ViewingGroupFilter.Hide([1]));
        Assert.NotEqual(ViewingGroupFilter.ShowOnly([]), ViewingGroupFilter.Hide([]));
        Assert.Equal("only [1, 2]", ViewingGroupFilter.ShowOnly([2, 1]).ToString());
    }

    [Fact]
    public void Controller_applies_the_filter_after_mode_membership_and_user_overrides()
    {
        var controller = new ViewingGroupController();
        controller.SetActiveModeMembership(new HashSet<int> { 52240, 52270 });
        controller.SetUserOverride(21010, true);
        var changes = 0;
        controller.Changed += () => changes++;

        controller.SetDatasetFilter(ViewingGroupFilter.ShowOnly([52270, 99999]));
        Assert.Equal(1, changes);
        Assert.True(controller.IsVisible(52270));
        Assert.False(controller.IsVisible(52240));
        Assert.False(controller.IsVisible(21010));
        // The filter never shows a group the mode leaves out.
        Assert.False(controller.IsVisible(99999));

        // An equal filter is a no-op; clearing restores the mode.
        controller.SetDatasetFilter(ViewingGroupFilter.ShowOnly([99999, 52270]));
        Assert.Equal(1, changes);
        controller.SetDatasetFilter(null);
        Assert.Equal(2, changes);
        Assert.True(controller.IsVisible(52240));
        Assert.True(controller.IsVisible(21010));
    }

    [Fact]
    public void Controller_hides_an_instruction_when_the_filter_drops_any_of_its_groups()
    {
        var controller = new ViewingGroupController();
        controller.SetDatasetFilter(ViewingGroupFilter.Hide([90020]));

        var instruction = new PointInstruction
        {
            FeatureReference = "F1",
            ViewingGroup = 27070,
            AdditionalViewingGroups = [90020],
            SymbolReference = "INFORM01",
        };

        Assert.False(controller.IsVisible(instruction));
        Assert.True(controller.IsVisible(new PointInstruction
        {
            FeatureReference = "F1",
            ViewingGroup = 27070,
            SymbolReference = "INFORM01",
        }));
    }
}
