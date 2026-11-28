using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

/// <summary>The pane chips while the map is split (split map step 2 §2.6, D18, #1769).</summary>
public sealed partial class LiveAccessibleNameTests
{
    [AvaloniaFact]
    public async Task Dataset_rows_have_real_names_while_split()
    {
        using var f = new DatasetsPaneChipsViewTests.Fixture();
        f.Datasets.Add("/data/US5OTHER.000", "S-57");
        f.Datasets.SelectDataset(f.Datasets.Add("/data/US5SEAFL.000", "S-57"));
        f.Split.OpenSplit();
        using var host = ViewHost.Show(new DatasetsView { DataContext = f.Datasets }, width: 420, height: 900);
        host.Click(host.Find<Avalonia.Controls.TabItem>("Datasets.DatasetsTab"));

        Assert.Equal(4, host.FindAll<PaneChip>("Datasets.Row.Chip").Count(c => c.IsEffectivelyVisible));
        await AssertRealNamesAsync(host, minimumRows: 2);
    }

    [AvaloniaFact]
    public async Task Exchange_set_tree_items_have_real_names_while_split()
    {
        using var f = new DatasetsPaneChipsViewTests.Fixture();
        var source = new EmptyAssetSource();
        f.Datasets.Entries.Add(new DatasetEntry("/data/ENC_ROOT/US5SEAFL.000", "S-57", source, "US5SEAFL.000", displayName: null));
        f.Datasets.RegisterExchangeSetHeader(source, "/data/ENC_ROOT", "ACME", "2026-10-01", 1, _ => { });
        f.Split.OpenSplit();
        using var host = ViewHost.Show(new DatasetsView { DataContext = f.Datasets }, width: 420, height: 900);
        host.Click(host.Find<Avalonia.Controls.TabItem>("Datasets.ExchangeSetsTab"));

        Assert.Equal(2, host.FindAll<PaneChip>("Datasets.Source.Chip").Count(c => c.IsEffectivelyVisible));
        await AssertRealNamesAsync(host, minimumRows: 1);
    }

    [AvaloniaFact]
    public async Task Layer_stack_rows_have_real_names_while_split()
    {
        using var f = new DatasetsPaneChipsViewTests.Fixture();
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(StackEntry(cell.Id.Value, S98DisplayPlane.BaseChartUnder, 10));
        var stack = new LayerStackViewModel(loader, dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: f.Datasets, legend: null);
        f.Split.OpenSplit();
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);

        Assert.Equal(2, host.FindAll<PaneChip>("LayerStack.Entry.Chip").Count(c => c.IsEffectivelyVisible));
        await AssertRealNamesAsync(host, minimumRows: 0);
    }
}
