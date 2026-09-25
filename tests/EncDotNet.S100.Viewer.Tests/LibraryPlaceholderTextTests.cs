using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Views;
using ShadTheme = ShadUI.ShadTheme;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Guards the Library views' text-box hints after the move from the
/// obsolete <c>TextBox.Watermark</c> to <c>PlaceholderText</c>: under the
/// app's ShadUI theme the TextBox template must still surface the localized
/// placeholder while the box is empty.
/// </summary>
public class LibraryPlaceholderTextTests
{
    [Fact]
    public void Library_panel_filter_box_shows_localized_placeholder()
        => AssertPlaceholderRendered(() => new LibraryPanelView(), Strings.Library_FilterWatermark);

    [Fact]
    public void Add_to_library_dialog_collection_name_shows_localized_placeholder()
        => AssertPlaceholderRendered(() => new AddToLibraryDialogView(), Strings.Library_CollectionNameWatermark);

    private static void AssertPlaceholderRendered(Func<Control> createView, string expected)
    {
        HeadlessTest.Run(() =>
        {
            var view = createView();
            // Add the theme before the content: a control resolves its
            // implicit theme when it joins the tree.
            var window = new Window();
            window.Styles.Add(new ShadTheme());
            window.Content = view;
            window.Show();
            try
            {
                var textBox = Assert.Single(
                    view.GetLogicalDescendants().OfType<TextBox>(),
                    t => t.PlaceholderText == expected);

                // The box may sit in a section hidden while the library is
                // empty, so build its template explicitly rather than rely on
                // a layout pass reaching it.
                textBox.ApplyTemplate();

                Assert.Contains(
                    textBox.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Text == expected && t.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
