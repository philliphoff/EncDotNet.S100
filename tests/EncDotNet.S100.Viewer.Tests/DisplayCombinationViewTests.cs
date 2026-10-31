using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DisplayCombinations;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The View section of the Display controls panel (#1192), driven through its
/// automation ids: apply a combination, see the modified marker, reapply, and
/// save, rename and delete a view of your own.
/// </summary>
public sealed class DisplayCombinationViewTests
{
    private sealed class Panel : IDisposable
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public EcdisDisplayState State { get; } = new();
        public DatasetsViewModel Datasets { get; }
        public DisplayCombinationService Service { get; }
        public DisplayCombinationsViewModel Combinations { get; }
        public EcdisDisplayPanelViewModel ViewModel { get; }

        public Panel()
        {
            Datasets = new DatasetsViewModel(Loader);
            Datasets.Entries.Add(new DatasetEntry("/data/US5WA31M.000", "S-57") { IsLoaded = true });
            Datasets.Entries.Add(new DatasetEntry("/data/102US00_US4WA1PB.h5", "S-102") { IsLoaded = true });
            // A weather dataset gives the panel a section of its own, so it
            // shows its controls rather than the empty state.
            Datasets.Entries.Add(new DatasetEntry("/data/gfs.grib2", "GRIB2") { IsLoaded = true });
            Service = new DisplayCombinationService(State, Loader, Datasets, new ViewerSettings { IsReadOnly = true });
            Combinations = new DisplayCombinationsViewModel(Service);
            ViewModel = new EcdisDisplayPanelViewModel(State, new PortrayalCatalogueManager(), Datasets, displayCombinations: Combinations);
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            Combinations.Dispose();
            Service.Dispose();
        }
    }

    [AvaloniaFact]
    public void Choosing_a_view_applies_it_and_a_change_marks_it_modified()
    {
        using var panel = new Panel();
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel.ViewModel }, width: 380, height: 1200);

        var rows = host.FindAll<Button>("DisplayCombination.Row.Apply");
        var charted = rows.Single(b => AutomationProperties.GetName(b) == Strings.DisplayCombination_Charted);
        host.Click(charted);
        host.Settle();

        Assert.Equal(BuiltInDisplayCombinations.ChartedId, panel.Service.Active?.Id);
        Assert.False(panel.Loader.GetActive(panel.Datasets.Entries[1].Id.Value));
        Assert.False(host.IsShown("DisplayCombination.Reapply"));

        host.Click(host.Find<RadioButton>("Ecdis.Category.All"));
        host.Settle();

        Assert.True(panel.Service.IsModified);
        Assert.True(host.IsShown("DisplayCombination.Changes"));
        Assert.Equal("Changed: Display category Standard → All.", host.Find<TextBlock>("DisplayCombination.Changes").Text);

        host.Click(host.Find<Button>("DisplayCombination.Reapply"));
        host.Settle();

        Assert.False(panel.Service.IsModified);
        Assert.Equal(EncDotNet.S100.Datasets.Pipelines.EcdisDisplayCategory.Standard, panel.State.Category);
    }

    [AvaloniaFact]
    public void The_view_section_shows_above_the_empty_state()
    {
        var loader = new FakeDatasetLoaderService();
        var datasets = new DatasetsViewModel(loader);
        using var service = new DisplayCombinationService(new EcdisDisplayState(), loader, datasets, new ViewerSettings { IsReadOnly = true });
        using var combinations = new DisplayCombinationsViewModel(service);
        using var vm = new EcdisDisplayPanelViewModel(new EcdisDisplayState(), new PortrayalCatalogueManager(), datasets, displayCombinations: combinations);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = vm }, width: 380, height: 1200);

        Assert.True(vm.IsEmpty);
        Assert.True(host.IsShown("Ecdis.View"));
        Assert.True(host.IsShown("DisplayCombination.SaveAs"));
    }

    [AvaloniaFact]
    public void Save_rename_and_delete_a_view_of_your_own()
    {
        using var panel = new Panel();
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel.ViewModel }, width: 380, height: 1400);

        host.Click(host.Find<Button>("DisplayCombination.SaveAs"));
        host.Settle();
        Assert.True(host.IsShown("DisplayCombination.Edit.Name"));
        host.Find<TextBox>("DisplayCombination.Edit.Name").Text = "Night approach";
        host.Settle();
        host.Click(host.Find<Button>("DisplayCombination.Edit.Save"));
        host.Settle();

        var mine = Assert.Single(panel.Service.UserCombinations);
        Assert.Equal("Night approach", mine.Label);
        Assert.Equal(mine.Id, panel.Service.Active?.Id);

        host.Click(host.Find<Button>("DisplayCombination.Row.Rename"));
        host.Settle();
        host.Find<TextBox>("DisplayCombination.Edit.Name").Text = "Elliott Bay";
        host.Settle();
        host.Click(host.Find<Button>("DisplayCombination.Edit.Save"));
        host.Settle();
        Assert.Equal("Elliott Bay", Assert.Single(panel.Service.UserCombinations).Label);

        host.Click(host.Find<Button>("DisplayCombination.Row.Delete"));
        host.Settle();
        Assert.Empty(panel.Service.UserCombinations);
        Assert.Null(panel.Service.Active);
        Assert.False(host.IsShown("DisplayCombination.Row.Delete"));
    }
}
