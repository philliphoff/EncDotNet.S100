using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

/// <summary>
/// <see cref="ViewerUiAutomation"/> against real views: the element tree, every
/// action, and the failures, driven only through automation peers (the
/// <c>ui_*</c> MCP tools' backend), not through pointer input.
/// </summary>
public sealed class ViewerUiAutomationTests
{
    private static readonly UiTreeQuery Interactive = new(Root: null, Depth: 30, InteractiveOnly: true, MaxNodes: 2000);

    [AvaloniaFact]
    public async Task The_interactive_tree_lists_ids_and_actions_but_not_template_parts()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5SEAFL.000", "S-57");
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        var automation = Automation(host);

        var tree = await automation.GetTreeAsync(Interactive);

        var root = Assert.Single(tree.Roots);
        Assert.Equal("window", root.Kind);
        var all = Flatten(root.Element).ToList();
        var tab = Assert.Single(all, e => e.Id == "Datasets.DatasetsTab");
        Assert.Contains("selectionItem", tab.Patterns);
        Assert.Equal("tabItem", tab.Role);
        Assert.Contains(all, e => e.Id == "Datasets.List");
        Assert.DoesNotContain(all, e => e.Id?.StartsWith("PART_", StringComparison.Ordinal) == true);
        Assert.False(tree.Truncated);
        Assert.Equal(all.Count, tree.NodeCount);
    }

    [AvaloniaFact]
    public async Task Selecting_and_invoking_drive_the_datasets_panel_and_within_scopes_a_rows_button()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var other = datasets.Add("/data/US5OTHER.000", "S-57");
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        var automation = Automation(host);

        var tab = await automation.ActAsync(new UiTarget("Datasets.DatasetsTab", null), UiAction.Select, null);
        Assert.True(tab.Selected);

        // Rows have no id; the tree gives each a ref and its dataset's name.
        var tree = await automation.GetTreeAsync(Interactive);
        var row = Assert.Single(Flatten(tree.Roots[0].Element), e => e.Role == "listItem" && e.Name == cell.DisplayName);
        await automation.ActAsync(new UiTarget(null, row.Ref), UiAction.Select, null);
        Assert.Same(cell, datasets.SelectedDataset);

        // The row's Remove button repeats per row: unscoped, the id is ambiguous.
        var ambiguous = await Assert.ThrowsAsync<UiAutomationException>(
            () => automation.ActAsync(new UiTarget("Datasets.Row.Remove", null), UiAction.Invoke, null));
        Assert.Equal(UiFailure.Ambiguous, ambiguous.Failure);
        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Contains(ambiguous.Candidates, c => c.Context?.Contains("US5SEAFL") == true);

        await automation.ActAsync(new UiTarget("Datasets.Row.Remove", null, Within: row.Ref), UiAction.Invoke, null);
        Assert.Equal([other], datasets.Entries);
    }

    [AvaloniaFact]
    public async Task The_wizard_can_be_completed_by_value_selection_toggle_and_invoke()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        library.Initialize();
        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
                Task.FromResult(new CatalogueProbe(null, null, null))),
            () => new AddToLibraryDialogViewModel(library, new LibraryCatalogueReaders
            {
                NoaaEnc = (_, _) => Task.FromResult(NoaaEncProductCatalogReader.Read(
                    LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml"))),
            }));
        wizard.Start(null);
        using var host = ViewHost.Show(new AddOnlineCatalogueWizardView { DataContext = wizard }, width: 640, height: 760);
        var automation = Automation(host);

        // There is nothing to go back to on the first step.
        var disabled = await Assert.ThrowsAsync<UiAutomationException>(
            () => automation.ActAsync(new UiTarget("Wizard.Back", null), UiAction.Invoke, null));
        Assert.Equal(UiFailure.Disabled, disabled.Failure);

        var search = await automation.ActAsync(new UiTarget("Catalogues.Search", null), UiAction.SetValue, "NOAA ENC");
        Assert.Equal("NOAA ENC", search.Value);
        var list = (await automation.GetTreeAsync(Interactive with { Root = new UiTarget("Catalogues.List", null) })).Roots[0].Element;
        var row = Flatten(list).First(e => e.Role == "listItem" && e.Name?.Contains("all U.S. waters") == true);
        await automation.ActAsync(new UiTarget(null, row.Ref), UiAction.Select, null);
        await automation.ActAsync(new UiTarget("Wizard.Next", null), UiAction.Invoke, null);
        Assert.Equal(2, wizard.CurrentStep);

        var state = wizard.Scope!.Choices("States")[0];
        var options = await Assert.ThrowsAsync<UiAutomationException>(
            () => automation.ActAsync(new UiTarget("CatalogueScope.Option", null), UiAction.Toggle, "on"));
        var first = options.Candidates.First(c => c.Context?.Contains(state.Label) == true);
        var toggled = await automation.ActAsync(new UiTarget(null, first.Ref), UiAction.Toggle, "on");
        Assert.Equal("on", toggled.Toggle);
        Assert.True(state.IsSelected);
        await automation.ActAsync(new UiTarget(null, first.Ref), UiAction.Toggle, "on");
        Assert.True(state.IsSelected);
    }

    [AvaloniaFact]
    public async Task A_context_menu_opens_as_a_popup_whose_items_can_be_invoked()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        library.Initialize();
        library.AddCollection("Charts", [new ExchangeSetSource(Guid.NewGuid(), null, context.CreateS57ExchangeSet("Charts"))]);
        await library.WhenIdle();
        using var panel = new LibraryPanelViewModel(
            library, new RecordingLibraryImporter(), new FakeLibraryLoader(), new FakeLibraryDownloader(), action => action());
        using var host = ViewHost.Show(new LibraryPanelView { DataContext = panel }, width: 420, height: 900);
        var automation = Automation(host);
        var collection = panel.Nodes.Single();

        var tree = await automation.GetTreeAsync(Interactive with { Root = new UiTarget("Library.Tree", null) });
        var node = Flatten(tree.Roots[0].Element).First(e => e.Role == "treeItem" && e.Name == "Charts");

        var collapsed = await automation.ActAsync(new UiTarget(null, node.Ref), UiAction.Collapse, null);
        Assert.False(collapsed.Expanded);
        var expanded = await automation.ActAsync(new UiTarget(null, node.Ref), UiAction.Expand, null);
        Assert.True(expanded.Expanded);

        // The menu acts on the node it opened on, which it selects first, as for
        // a right-click: open it on the source node while the collection is
        // selected. (Headless popups are overlays inside the window; on the
        // desktop the menu is a popup root, listed as a root of kind "popup".)
        var source = collection.Children.Single();
        var sourceNode = Flatten(tree.Roots[0].Element).Last(e => e.Role == "treeItem");
        Assert.Same(collection, panel.SelectedNode);
        await automation.ActAsync(new UiTarget(null, sourceNode.Ref), UiAction.ContextMenu, null);
        Assert.Same(source, panel.SelectedNode);
        await automation.ActAsync(new UiTarget(null, node.Ref), UiAction.ContextMenu, null);
        Assert.Same(collection, panel.SelectedNode);
        var withMenu = await automation.GetTreeAsync(Interactive);
        Assert.Contains(withMenu.Roots.SelectMany(r => Flatten(r.Element)), e => e.Id == "Library.NodeMenu.Rename");
        await automation.ActAsync(new UiTarget("Library.NodeMenu.Rename", null), UiAction.Invoke, null);

        Assert.True(collection.IsRenaming);
        await automation.ActAsync(new UiTarget("Library.Tree.RenameBox", null), UiAction.SetValue, "Harbour charts");
        // Leaving the box commits, as for a user.
        await automation.ActAsync(new UiTarget("Library.Filter", null), UiAction.Focus, null);
        await library.WhenIdle();

        Assert.False(collection.IsRenaming);
        Assert.Equal("Harbour charts", Assert.Single(library.Collections).Definition.Name);
    }

    [AvaloniaFact]
    public async Task Failures_say_what_is_wrong()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5SEAFL.000", "S-57");
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        var automation = Automation(host);

        var missing = await Assert.ThrowsAsync<UiAutomationException>(
            () => automation.ActAsync(new UiTarget("Datasets.Tab", null), UiAction.Select, null));
        Assert.Equal(UiFailure.NotFound, missing.Failure);
        Assert.Contains("Datasets.DatasetsTab", missing.Suggestions);

        var unsupported = await Assert.ThrowsAsync<UiAutomationException>(
            () => automation.ActAsync(new UiTarget("Datasets.DatasetsTab", null), UiAction.Invoke, null));
        Assert.Equal(UiFailure.NotSupported, unsupported.Failure);
        Assert.Contains("selectionItem", unsupported.Patterns);

        var stale = await Assert.ThrowsAsync<UiAutomationException>(
            () => automation.ActAsync(new UiTarget(null, "e999999"), UiAction.Invoke, null));
        Assert.Equal(UiFailure.NotFound, stale.Failure);
    }

    [AvaloniaFact]
    public async Task Max_nodes_truncates_the_tree()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5SEAFL.000", "S-57");
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);

        var tree = await Automation(host).GetTreeAsync(new UiTreeQuery(null, Depth: 30, InteractiveOnly: false, MaxNodes: 5));

        Assert.True(tree.Truncated);
        Assert.Equal(5, tree.NodeCount);
    }

    private static ViewerUiAutomation Automation(ViewHost host) => new(() => [host.Window]);

    private static IEnumerable<UiElementSnapshot> Flatten(UiElementSnapshot element)
        => new[] { element }.Concat((element.Children ?? []).SelectMany(Flatten));
}
