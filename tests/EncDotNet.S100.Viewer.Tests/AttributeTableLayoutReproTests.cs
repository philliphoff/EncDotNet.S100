using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Layout guard for the pick-report attribute table. Mirrors the wrapper
/// chain used by <c>PickReportView.axaml</c> (named ScrollViewer → content
/// StackPanel whose <see cref="Layoutable.MaxWidth"/> is bound to the
/// scroller's <see cref="ScrollViewer.Viewport"/> width → bordered
/// ItemsControl of fixed-label / star-value rows) and asserts a very long
/// value stays within the panel width — even when the ScrollViewer is allowed
/// to scroll horizontally (which is how a misbehaving theme template could
/// otherwise let the NoWrap value cells over-extend and overflow to the
/// right). The <c>Viewport.Width</c> cap is the defence under test.
/// </summary>
public class AttributeTableLayoutReproTests
{
    private const double HostWidth = 300;

    private static (Border host, TextBlock value) BuildChain(
        string value,
        ScrollBarVisibility horizontal)
    {
        var value1 = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,14,*") };
        var label = new TextBlock
        {
            Text = "Water Level Effect",
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 0);
        rowGrid.Children.Add(label);

        var valueOuter = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(value1, 1);
        valueOuter.Children.Add(value1);
        Grid.SetColumn(valueOuter, 2);
        rowGrid.Children.Add(valueOuter);

        var rowBorder = new Border { Name = "AttrRow", Padding = new Thickness(12, 9), Child = rowGrid };
        var items = new ItemsControl { ItemsSource = new[] { rowBorder } };
        var tableBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = items,
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { tableBorder },
        };

        var scroller = new ScrollViewer
        {
            Name = "AttrScroll",
            HorizontalScrollBarVisibility = horizontal,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 0, 12, 12),
            Content = content,
        };

        // Mirror the real fix: cap the content to the scroller's viewport width.
        content.Bind(Layoutable.MaxWidthProperty, new Binding
        {
            Source = scroller,
            Path = "Viewport.Width",
        });

        var host = new Border { Width = HostWidth, Child = scroller };
        return (host, value1);
    }

    private static void AssertWithinHost(ScrollBarVisibility horizontal)
    {
        var (host, value) = BuildChain(
            "Always Under Water/Submerged at all states of the tide and an extremely long tail that would overflow",
            horizontal);

        // Two passes so the Viewport.Width binding settles before the cap is read.
        for (var i = 0; i < 2; i++)
        {
            host.Measure(new Size(HostWidth, 1000));
            host.Arrange(new Rect(0, 0, HostWidth, 1000));
        }

        var topLeft = value.TranslatePoint(new Point(0, 0), host) ?? new Point();
        var right = topLeft.X + value.Bounds.Width;
        Assert.True(
            right <= HostWidth + 0.5,
            $"value right edge {right:F1} exceeds host width {HostWidth} (h={horizontal}, value width {value.Bounds.Width:F1}, x {topLeft.X:F1})");
    }

    [Fact]
    public void LongValue_HorizontalDisabled_StaysWithinHost()
        => HeadlessTest.Run(() => AssertWithinHost(ScrollBarVisibility.Disabled));

    [Fact]
    public void LongValue_HorizontalScrollAllowed_ViewportCapStillContains()
        => HeadlessTest.Run(() => AssertWithinHost(ScrollBarVisibility.Auto));
}
