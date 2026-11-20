using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using EncDotNet.S100.Viewer.Tests.Headless;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The viewer's one segmented control (PanelStyles.axaml): a track of radio
/// segments and a segment-tab strip are the same size, so the Library's,
/// the Legend's and the Datasets panel's switches line up.
/// </summary>
public sealed class SegmentedControlTests
{
    [AvaloniaFact]
    public void Radio_segments_and_segment_tabs_are_the_same_size()
    {
        var radios = new Border
        {
            Classes = { "segmentTrack" },
            Child = new UniformGrid
            {
                Rows = 1,
                Children =
                {
                    new RadioButton { Classes = { "segment" }, Content = "All", IsChecked = true },
                    new RadioButton { Classes = { "segment" }, Content = "Local" },
                },
            },
        };
        var tabs = new TabControl
        {
            Classes = { "segmentTabs" },
            Items = { new TabItem { Header = "In view" }, new TabItem { Header = "All loaded" } },
        };
        var compact = new TabControl
        {
            Classes = { "segmentTabs", "compact" },
            Items = { new TabItem { Header = "Distance" }, new TabItem { Header = "Time" } },
        };
        var panel = new StackPanel { Width = 300, Children = { radios, tabs, compact } };

        using var host = ViewHost.Show(panel, width: 400, height: 300);

        Assert.Equal(32, radios.Bounds.Height);
        Assert.Equal(32, tabs.Bounds.Height);
        Assert.Equal(24, compact.Bounds.Height);
        var segment = (RadioButton)((UniformGrid)radios.Child!).Children[0];
        var tab = (TabItem)tabs.Items[0]!;
        Assert.Equal(segment.Bounds.Height, tab.Bounds.Height);
        Assert.Equal(segment.FontSize, tab.FontSize);
        Assert.Equal(13, tab.FontSize);
    }
}
