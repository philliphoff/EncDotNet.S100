using Avalonia.Controls;
using Avalonia.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Library panel over a real <see cref="CollectionLibrary"/>, driven by
/// pointer and keyboard input. <see cref="LibraryPanelViewModelTests"/> covers
/// the panel's logic; these cover the view's wiring to it: the empty-state
/// actions, tree and list selection, the filter box, the details pane, and the
/// tree's context menu with in-place rename.
/// </summary>
public sealed class LibraryPanelViewTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly RecordingLibraryImporter _importer = new();
    private readonly FakeLibraryLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();

    public LibraryPanelViewTests()
    {
        _library = _context.CreateService();
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    [AvaloniaFact]
    public void The_empty_states_quick_actions_start_their_add_flows()
    {
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);

        host.Click(host.Find<Button>("Library.Quick.Online"));
        host.Click(host.Find<Button>("Library.Quick.Folder"));

        Assert.Equal([("online", (Guid?)null), ("folder", (Guid?)null)], _importer.Calls);
    }

    [AvaloniaFact]
    public async Task Clicking_a_source_in_the_tree_lists_only_its_items()
    {
        var collection = await AddS57CollectionAsync();
        _library.AddSources(collection.Id, [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("extra"))]);
        await _library.WhenIdle();
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);
        Assert.Equal(4, VisibleRows(host).Count);

        var source = panel.Nodes[0].Children[1];
        host.Click(NodeLabel(host, source));

        Assert.Same(source, panel.SelectedNode);
        Assert.Equal(2, VisibleRows(host).Count);
        Assert.All(panel.Items, i => Assert.Equal(source.Id, i.Source.Id));
    }

    [AvaloniaFact]
    public async Task Typing_in_the_filter_box_narrows_the_list()
    {
        await AddS57CollectionAsync();
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);

        host.Click(host.Find<TextBox>("Library.Filter"));
        host.Type("52m");

        var row = Assert.Single(VisibleRows(host));
        Assert.Equal("US5WA52M", ((LibraryItemViewModel)row.DataContext!).Name);
    }

    [AvaloniaFact]
    public async Task Clicking_a_row_shows_it_in_the_details_and_load_loads_it()
    {
        await AddS57CollectionAsync();
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);

        var item = panel.Items[1];
        host.Click(Row(host, item));
        Assert.Same(item, panel.SelectedItem);

        host.Click(host.Find<Button>("Library.Details.Load"));
        await _library.WhenIdle();

        Assert.Equal([(false, 1)], _loader.Calls);
    }

    [AvaloniaFact]
    public async Task Double_clicking_a_row_loads_it()
    {
        await AddS57CollectionAsync();
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);

        host.DoubleClick(Row(host, panel.Items[0]));
        await _library.WhenIdle();

        Assert.Same(panel.Items[0], panel.SelectedItem);
        Assert.Equal([(false, 1)], _loader.Calls);
    }

    [AvaloniaFact]
    public async Task Rename_from_the_context_menu_edits_in_place_and_enter_commits()
    {
        await AddS57CollectionAsync("Charts");
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);
        var collection = panel.Nodes.Single();
        var source = collection.Children.Single();

        // The menu acts on the node under the pointer, which it selects first.
        var menu = host.RightClick(NodeLabel(host, source));
        Assert.Same(source, panel.SelectedNode);
        host.Click(host.Find<MenuItem>("Library.NodeMenu.Rename", menu));

        // The box opens focused with the old name selected, so typing replaces it.
        var box = host.Find<TextBox>("Library.Tree.RenameBox");
        Assert.True(box.IsFocused);
        Assert.Equal(source.Name, box.SelectedText);
        host.Type("Survey 2026");
        host.Press(PhysicalKey.Enter);
        await _library.WhenIdle();
        host.Settle();

        Assert.False(source.IsRenaming);
        Assert.Equal("Survey 2026", Assert.Single(Assert.Single(_library.Collections).Sources).Definition.DisplayName);
        Assert.Equal("Survey 2026", panel.Nodes.Single().Children.Single().Name);
    }

    [AvaloniaFact]
    public async Task Escape_in_the_rename_box_keeps_the_old_name()
    {
        await AddS57CollectionAsync("Charts");
        using var panel = new LibraryPanelViewModel(_library, _importer, _loader, _downloader, action => action());
        using var host = Show(panel);
        var collection = panel.Nodes.Single();

        var menu = host.RightClick(NodeLabel(host, collection));
        host.Click(host.Find<MenuItem>("Library.NodeMenu.Rename", menu));
        host.Type("Something else");
        host.Press(PhysicalKey.Escape);
        await _library.WhenIdle();

        Assert.False(collection.IsRenaming);
        Assert.Equal("Charts", Assert.Single(_library.Collections).Definition.Name);
    }

    private static ViewHost Show(LibraryPanelViewModel panel)
        => ViewHost.Show(new LibraryPanelView { DataContext = panel }, width: 420, height: 900);

    private async Task<DatasetCollection> AddS57CollectionAsync(string name = "Charts")
    {
        var collection = _library.AddCollection(name, [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet(name))]);
        await _library.WhenIdle();
        return collection;
    }

    /// <summary>A tree node's name label: what a user clicks to pick the node.</summary>
    private static TextBlock NodeLabel(ViewHost host, LibraryNodeViewModel node)
        => host.Find<TextBlock>(t => ReferenceEquals(t.DataContext, node) && t.Text == node.Name);

    private static ListBoxItem Row(ViewHost host, LibraryItemViewModel item)
        => host.Find<ListBoxItem>(row => ReferenceEquals(row.DataContext, item));

    private static List<ListBoxItem> VisibleRows(ViewHost host)
    {
        var list = host.Find<ListBox>("Library.Items");
        return host.All<ListBoxItem>().Where(r => r.IsEffectivelyVisible && list.IndexFromContainer(r) >= 0).ToList();
    }
}
