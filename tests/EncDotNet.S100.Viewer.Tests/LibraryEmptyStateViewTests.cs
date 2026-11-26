using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Library's empty state: the quick-action buttons grow to fit their
/// wrapped subtitles and the drop hint wraps, at any panel width.
/// </summary>
public sealed class LibraryEmptyStateViewTests : IDisposable
{
    private static readonly string[] QuickActions =
        ["Library.Quick.Folder", "Library.Quick.Online", "Library.Quick.Feed", "Library.Quick.Manifest"];

    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;

    public LibraryEmptyStateViewTests()
    {
        _library = _context.CreateService(new CollectionIndexer([]));
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(260)]
    [InlineData(340)]
    [InlineData(480)]
    public void QuickActionsFitTheirWrappedSubtitles(double width)
    {
        var vm = TestLibrary.Panel(_library, new RecordingLibraryImporter(), new FakeLibraryLoader(), new FakeLibraryDownloader(),
            action => action(), action => action(), TimeProvider.System, feedHealth: null, notifications: null,
            viewTime: null, timeFormat: null, sync: null);
        Assert.True(vm.Tree.IsEmpty);
        using var host = Headless.ViewHost.Show(new Views.LibraryPanelView { DataContext = vm }, width: width, height: 900);
        host.Settle();

        foreach (var id in QuickActions)
        {
            var button = host.Find<Button>(id);
            var texts = button.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var title = texts.Single(t => t.Text == button.GetValue(Avalonia.Automation.AutomationProperties.NameProperty));
            var subtitle = texts.Single(t => t.Text == button.GetValue(Avalonia.Automation.AutomationProperties.HelpTextProperty));

            // The button holds the title and the whole subtitle, not one fixed line.
            var bottom = subtitle.TranslatePoint(new Point(0, subtitle.Bounds.Height), button)!.Value.Y;
            Assert.True(bottom <= button.Bounds.Height, $"{id}: subtitle bottom {bottom} past button height {button.Bounds.Height} at width {width}");
            Assert.True(button.Bounds.Height > title.Bounds.Height + subtitle.Bounds.Height, $"{id}: button too short at width {width}");

            // Title and subtitle wrap inside the button rather than running past its right edge.
            foreach (var text in new[] { title, subtitle })
            {
                var right = text.TranslatePoint(new Point(text.Bounds.Width, 0), button)!.Value.X;
                Assert.True(right <= button.Bounds.Width, $"{id}: \"{text.Text}\" right {right} past button width {button.Bounds.Width} at width {width}");
                Assert.True(text.DesiredSize.Width <= text.Bounds.Width + 0.5, $"{id}: \"{text.Text}\" clipped at width {width}");
            }
        }

        var hint = host.All<TextBlock>().Single(t => t.Text == Strings.Library_DropHint);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, hint.TextWrapping);
        Assert.True(hint.Bounds.Width <= width, $"drop hint {hint.Bounds.Width} wider than panel {width}");
    }
}
