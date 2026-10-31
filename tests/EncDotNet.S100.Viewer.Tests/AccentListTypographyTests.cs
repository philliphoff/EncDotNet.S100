using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.Tests.Headless;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Rows of every <c>accentList</c> list render at regular weight (#1171):
/// ShadUI's ListBoxItem theme sets FontWeight Medium, which every TextBlock in
/// a row would otherwise inherit. A title keeps the weight set on it.
/// </summary>
public sealed class AccentListTypographyTests
{
    [AvaloniaTheory]
    [InlineData("accentList")]
    [InlineData("accentList dense")]
    public void Row_text_is_regular_weight_and_the_title_keeps_its_own(string classes)
    {
        var list = new ListBox
        {
            ItemsSource = new[] { "Row" },
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock { Text = text, Classes = { "accentListTitle" }, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Body" },
                },
            }),
        };
        list.Classes.AddRange(classes.Split(' '));

        using var host = ViewHost.Show(list, width: 400, height: 200);

        var item = host.Find<ListBoxItem>(i => Equals(i.DataContext, "Row"));
        var blocks = item.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Equal(FontWeight.SemiBold, blocks.Single(t => t.Text == "Row").FontWeight);
        Assert.Equal(FontWeight.Normal, blocks.Single(t => t.Text == "Body").FontWeight);
    }
}
