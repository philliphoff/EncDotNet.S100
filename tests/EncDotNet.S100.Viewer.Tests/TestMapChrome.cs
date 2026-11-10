using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DisplayCombinations;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Builds a <see cref="MapChromeViewModel"/> over a fresh display state and no warning datasets.</summary>
internal static class TestMapChrome
{
    public static MapChromeViewModel Create(PortrayalCatalogueManager catalogues, DatasetsViewModel datasets)
    {
        var display = new EcdisDisplayState();
        var loader = new FakeDatasetLoaderService();
        var settings = new ViewerSettings { IsReadOnly = true };
        var warnings = new NavigationalWarningsService(
            loader,
            classifications: new NavigationalWarningClassificationStore(null),
            datasets: null,
            sources: null,
            seen: new NavigationalWarningSeenStore(null),
            clock: TimeProvider.System,
            ownShip: null,
            activeRoute: null);
        var viewTime = new ClockViewTime(TimeProvider.System);
        var drawing = new NavigationalWarningDrawingService(warnings, viewTime, settings, loader, time: null, dispatch: action => action(), display: display);
        return new MapChromeViewModel(
            new DisplayToolbarViewModel(display),
            new TextGroupToolbarViewModel(display, catalogues, datasets),
            new DisplayModeToolbarViewModel(display, loader),
            new DisplayCombinationsViewModel(new DisplayCombinationService(display, loader, datasets, settings, post: null), ui: null),
            new NavigationalWarningsViewModel(warnings, pick: null, pickReport: null, viewTime: viewTime, timeFormat: null, viewport: null, loader: null, clock: TimeProvider.System) { Drawing = drawing },
            drawing);
    }
}
