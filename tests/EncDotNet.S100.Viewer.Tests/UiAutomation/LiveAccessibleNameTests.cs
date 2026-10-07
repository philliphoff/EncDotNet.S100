using System.Text.RegularExpressions;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

/// <summary>
/// The live half of <see cref="AccessibilityGuardTests"/> (#784): the guard
/// reads XAML, so it cannot see a list row or tree item whose automation peer
/// falls back to its view model's type name. These show views with populated
/// rows and walk them through <see cref="ViewerUiAutomation"/>, as the
/// <c>ui_tree</c> tool and a screen reader see them.
/// </summary>
public sealed partial class LiveAccessibleNameTests
{
    private static readonly DateTime Run = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    [AvaloniaFact]
    public async Task Library_rows_and_tree_items_have_real_names()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        library.Initialize();
        library.AddCollection("Charts", [new ExchangeSetSource(Guid.NewGuid(), null, context.CreateS57ExchangeSet("Charts"))]);
        await library.WhenIdle();
        using var panel = new LibraryPanelViewModel(
            library, new RecordingLibraryImporter(), new FakeLibraryLoader(), new FakeLibraryDownloader(), action => action());
        using var host = ViewHost.Show(new LibraryPanelView { DataContext = panel }, width: 420, height: 900);
        Assert.NotEmpty(panel.Items);
        host.Click(host.Find<Avalonia.Controls.ListBoxItem>(r => ReferenceEquals(r.DataContext, panel.Items[0])));

        await AssertRealNamesAsync(host, minimumRows: panel.Items.Count + 2);
    }

    [AvaloniaFact]
    public async Task Dataset_rows_have_real_names()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5OTHER.000", "S-57");
        datasets.SelectDataset(datasets.Add("/data/US5SEAFL.000", "S-57"));
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        host.Click(host.Find<Avalonia.Controls.TabItem>("Datasets.DatasetsTab"));

        await AssertRealNamesAsync(host, minimumRows: 2);
    }

    [AvaloniaFact]
    public async Task Exchange_set_tree_items_have_real_names()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/ENC_ROOT/US5SEAFL.000", "S-57");
        datasets.RegisterExchangeSetHeader(new EmptyAssetSource(), "/data/ENC_ROOT", "ACME", "2026-10-01", 1, _ => { });
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        host.Click(host.Find<Avalonia.Controls.TabItem>("Datasets.ExchangeSetsTab"));

        await AssertRealNamesAsync(host, minimumRows: 1);
    }

    [AvaloniaFact]
    public async Task Layer_stack_rows_have_real_names()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(
            StackEntry("US5SEAFL.000", S98DisplayPlane.BaseChartUnder, 10),
            StackEntry("102US00_BATHY.h5", S98DisplayPlane.Bathymetry, 20));
        var stack = new LayerStackViewModel(loader);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);

        await AssertRealNamesAsync(host, minimumRows: 0);
    }

    [AvaloniaFact]
    public async Task Route_rows_have_real_names()
    {
        var service = new RoutesService();
        var routes = new RoutesPanelViewModel(service);
        var route = service.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));
        service.Routes.CreateRoute("R2");
        using var host = ViewHost.Show(new RoutesView { DataContext = routes }, width: 420, height: 800);

        await AssertRealNamesAsync(host, minimumRows: 2);
    }

    [AvaloniaFact]
    public async Task Timeline_lanes_have_real_names()
    {
        var service = new GlobalTimeService();
        var timeline = new TimelineViewModel(
            service, null, new FakeTimeProvider(new DateTimeOffset(Run.AddHours(5))), action => action());
        var samples = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets =
            [
                new MapsuiMapTimedDataset("104US004SC1BO_20261002T00Z", samples[0], samples[^1]) { ProductSpec = "S-104", Samples = samples },
                new MapsuiMapTimedDataset("111US00_CBOFS_20261002T00Z_US4MD1DD", samples[0], samples[^1]) { ProductSpec = "S-111", Samples = samples },
            ],
        });
        using var host = ViewHost.Show(new TimelineView { DataContext = timeline }, width: 900, height: 400);

        await AssertRealNamesAsync(host, minimumRows: 0);
    }

    [AvaloniaFact]
    public async Task Catalogue_rows_have_real_names()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        library.Initialize();
        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
                Task.FromResult(new CatalogueProbe(null, null, null))),
            () => new AddToLibraryDialogViewModel(library, loadNoaaCatalog: null));
        wizard.Start(null);
        using var host = ViewHost.Show(new AddOnlineCatalogueWizardView { DataContext = wizard }, width: 640, height: 760);

        await AssertRealNamesAsync(host, minimumRows: 2);
    }

    /// <summary>
    /// Fails on any element whose name is a type name, and on any element a
    /// user acts on (a row, a button, a tree item) that has no name at all:
    /// <see cref="ViewerUiAutomation"/> drops a type-name name, reporting the
    /// visible text or tooltip instead, so a missing name is how a row named
    /// after its view model shows up there.
    /// </summary>
    private static async Task AssertRealNamesAsync(ViewHost host, int minimumRows)
    {
        var automation = new ViewerUiAutomation(() => [host.Window]);
        var tree = await automation.GetTreeAsync(new UiTreeQuery(null, Depth: 64, InteractiveOnly: true, MaxNodes: 5000));
        Assert.False(tree.Truncated);

        var elements = tree.Roots.SelectMany(r => Flatten(r.Element)).ToList();
        var rows = elements.Count(e => e.Role is "listItem" or "treeItem");
        Assert.True(rows >= minimumRows, $"Expected at least {minimumRows} rows, found {rows}.");

        var bad = elements
            .Where(e => e.Name is { } name ? LooksLikeTypeName(name) : HasAction(e))
            .Select(e => $"{e.Role} ({e.ClassName}) id={e.Id ?? "-"} name={e.Name ?? "(none)"} text={e.Text ?? "-"}")
            .ToList();

        Assert.True(bad.Count == 0,
            "These elements have no real accessible name (a row's or item's peer falls back to its view model's type name); "
            + "set AutomationProperties.Name, for rows on the item container style from the row's display name:\n"
            + string.Join('\n', bad));
    }

    /// <summary>
    /// Every element under <paramref name="element"/>, but not the parts of a
    /// scroll bar or slider: their line and track buttons are the theme's,
    /// unnamed in every Avalonia app, and a screen reader moves them through
    /// the control's own range value pattern. The slider itself is checked.
    /// </summary>
    private static IEnumerable<UiElementSnapshot> Flatten(UiElementSnapshot element)
        => element.Role == "scrollBar"
            ? []
            : element.Children is { } children && element.Role != "slider"
                ? children.SelectMany(Flatten).Prepend(element)
                : [element];

    private static bool HasAction(UiElementSnapshot element)
        => element.Patterns.Any(p => p is "invoke" or "toggle" or "selectionItem" or "expandCollapse");

    private static bool LooksLikeTypeName(string name)
        => name.EndsWith("ViewModel", StringComparison.Ordinal)
            || name.Contains("FluentIcons.Avalonia.FluentIcon", StringComparison.Ordinal)
            || NamespacedName().IsMatch(name);

    /// <summary>A dotted identifier such as <c>EncDotNet.S100.Viewer.ViewModels.Foo</c>.</summary>
    [GeneratedRegex(@"^[A-Za-z_]\w*(\.[A-Za-z_][\w`]*){2,}$")]
    private static partial Regex NamespacedName();

    private sealed class EmptyAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public void Dispose() { }
    }

    private static LayerStackEntry StackEntry(string id, S98DisplayPlane plane, int priority)
        => new(new MemoryLayer(id), new SubLayerStackItem(new SyntheticStackPayload(id), plane, priority, id));
}
