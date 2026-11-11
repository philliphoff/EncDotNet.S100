using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The display settings tools: weather, bathymetry and AML display, route weather settings, display combinations and the depth basis.</summary>
internal sealed class DisplaySettingsMcpToolGroup : IViewerMcpToolGroup
{
    private readonly EncDotNet.S100.Viewer.ViewModels.EcdisDisplayPanelViewModel _ecdisDisplay;
    private readonly EncDotNet.S100.Viewer.ViewModels.SettingsViewModel _settingsViewModel;
    private readonly EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService _displayCombinations;
    private readonly EncDotNet.S100.Viewer.Services.Depth.DepthBasisService _depthBasis;

    public DisplaySettingsMcpToolGroup(
        EncDotNet.S100.Viewer.ViewModels.EcdisDisplayPanelViewModel ecdisDisplay,
        EncDotNet.S100.Viewer.ViewModels.SettingsViewModel settingsViewModel,
        EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService displayCombinations,
        EncDotNet.S100.Viewer.Services.Depth.DepthBasisService depthBasis)
    {
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(displayCombinations);
        ArgumentNullException.ThrowIfNull(depthBasis);

        _ecdisDisplay = ecdisDisplay;
        _settingsViewModel = settingsViewModel;
        _displayCombinations = displayCombinations;
        _depthBasis = depthBasis;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var weatherDisplay = new WeatherDisplayTools(_ecdisDisplay, new DispatcherRouteEditInvoker());
        yield return WeatherDisplayMcpAdapters.CreateGet(weatherDisplay);
        yield return WeatherDisplayMcpAdapters.CreateSet(weatherDisplay);
        var bathymetryDisplay = new BathymetryDisplayTools(_ecdisDisplay, new DispatcherRouteEditInvoker());
        yield return BathymetryDisplayMcpAdapters.CreateGet(bathymetryDisplay);
        yield return BathymetryDisplayMcpAdapters.CreateSet(bathymetryDisplay);

        if (_ecdisDisplay.Aml is { } aml)
        {
            var amlDisplay = new AmlDisplayTools(aml.Service, new DispatcherRouteEditInvoker());
            yield return AmlDisplayMcpAdapters.CreateGet(amlDisplay);
            yield return AmlDisplayMcpAdapters.CreateSet(amlDisplay);
        }

        var routeWeather = new RouteWeatherSettingsTools(_settingsViewModel, new DispatcherRouteEditInvoker());
        yield return RouteWeatherSettingsMcpAdapters.CreateGet(routeWeather);
        yield return RouteWeatherSettingsMcpAdapters.CreateSet(routeWeather);

        yield return DisplayCombinationMcpAdapters.Create(new ListDisplayCombinationsTool(_displayCombinations, UiThread.InvokeAsync));
        yield return DisplayCombinationMcpAdapters.Create(new SetDisplayCombinationTool(_displayCombinations, UiThread.InvokeAsync));

        yield return DepthBasisMcpAdapter.Create(new GetDepthBasisTool(_depthBasis, new DispatcherRouteEditInvoker()));
    }
}
