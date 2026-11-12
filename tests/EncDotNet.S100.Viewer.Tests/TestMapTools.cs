using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Builds a <see cref="MapToolsViewModel"/> over fresh tool state for tests.</summary>
internal static class TestMapTools
{
    public static MapToolsViewModel Create(ViewerSettingsService settings, RoutesService routes)
    {
        var measure = new MeasureSession(settings: null);
        return new MapToolsViewModel(
            new StubMeasureOverlayAppearanceProvider(),
            measure,
            new MeasureCardViewModel(measure),
            new RangeBearingState(settings: null),
            routes,
            settings);
    }
}
