using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Builds the Navigational warnings panel as the app does (#1397): over an
/// open-warning workflow and an Object Information selection, with stubs for
/// the pick, the loader and the map where a test doesn't care about them.
/// </summary>
internal static class TestNavigationalWarnings
{
    /// <summary>A panel over <paramref name="service"/>.</summary>
    public static NavigationalWarningsViewModel Panel(
        NavigationalWarningsService service,
        TimeProvider clock,
        IViewTime? viewTime = null,
        ITimeFormatProvider? timeFormat = null,
        IPickService? pick = null,
        IDatasetLoaderService? loader = null,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        ObjectInformationSelection? selection = null,
        RouteFindingsService? routeFindings = null,
        NavigationalWarningDrawingService? drawing = null,
        NavigationalWarningNewOptionsViewModel? newOptions = null) =>
        new(
            service,
            Open(service, pick, loader, viewport),
            selection ?? new ObjectInformationSelection(),
            routeFindings,
            drawing,
            newOptions,
            viewTime,
            timeFormat,
            clock);

    /// <summary>The workflow the panel opens warnings through.</summary>
    public static OpenNavigationalWarningWorkflow Open(
        NavigationalWarningsService service,
        IPickService? pick = null,
        IDatasetLoaderService? loader = null,
        ICapabilityAccessor<IMapViewportController>? viewport = null) =>
        new(pick ?? new StubPickService(), loader ?? new FakeDatasetLoaderService(), viewport ?? new NoViewport(), service);

    /// <summary>A host with no map.</summary>
    private sealed class NoViewport : ICapabilityAccessor<IMapViewportController>
    {
        public IMapViewportController? Current => null;
    }
}
