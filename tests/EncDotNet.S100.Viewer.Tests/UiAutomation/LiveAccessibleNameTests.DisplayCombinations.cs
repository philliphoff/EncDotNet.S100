using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DisplayCombinations;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

public sealed partial class LiveAccessibleNameTests
{
    [AvaloniaFact]
    public async Task Display_combination_rows_have_real_names()
    {
        var loader = new FakeDatasetLoaderService();
        var datasets = new DatasetsViewModel(loader);
        datasets.Entries.Add(new DatasetEntry("/data/US5WA31M.000", "S-57") { IsLoaded = true });
        using var service = new DisplayCombinationService(new EcdisDisplayState(), loader, datasets, new ViewerSettings { IsReadOnly = true });
        service.SaveCurrent("Night approach");
        using var combinations = new DisplayCombinationsViewModel(service);
        using var host = ViewHost.Show(new DisplayCombinationsView { DataContext = combinations }, width: 380, height: 1000);

        await AssertRealNamesAsync(host, minimumRows: 0);
    }
}
