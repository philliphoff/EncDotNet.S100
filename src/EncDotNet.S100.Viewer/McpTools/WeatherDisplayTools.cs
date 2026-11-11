using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The Display controls Weather section (#1072, #1298 V26).</summary>
[Description("The Display controls Weather section: map-wide switches for the optional weather layers, applied to every weather dataset and kept after a restart.")]
internal sealed record WeatherDisplayDto(
    [property: Description("True while the Weather section shows: a weather dataset (GRIB2, NetCDF, a tropical cyclone or a surface analysis) is loaded. The switches apply and can be set either way.")] bool SectionShown,
    [property: Description("True while a tropical cyclone is loaded, so the Wind radii, Past track and Forecast times switches show.")] bool CycloneSwitchesShown,
    [property: Description("Isobars: mean sea level pressure isobars on every weather dataset with pressure. On by default.")] bool Isobars,
    [property: Description("Wind speed fill: the Beaufort fill on every weather dataset with wind. Off by default.")] bool WindSpeedFill,
    [property: Description("Wave arrows: wave direction arrows on every weather dataset with waves. Off by default.")] bool WaveArrows,
    [property: Description("Isotherms: sea surface temperature isotherms. Off by default.")] bool Isotherms,
    [property: Description("Currents: surface current arrows on every weather dataset with currents. On by default.")] bool Currents,
    [property: Description("Air temperature: dashed 2 m air temperature isotherms. Off by default.")] bool AirTemperature,
    [property: Description("Precipitation: the precipitation rate fill in rain radar colours. Off by default.")] bool Precipitation,
    [property: Description("Thunderstorm risk: the CAPE fill from 1000 J/kg. Off by default.")] bool ThunderstormRisk,
    [property: Description("Cloud cover: the total cloud cover veil from 25 %. Off by default.")] bool CloudCover,
    [property: Description("Wind radii: the 34, 50 and 64 kt wind radii of every tropical cyclone. On by default. The cone and forecast track have no switch.")] bool WindRadii,
    [property: Description("Past track: every tropical cyclone's best-track line and points. On by default.")] bool PastTrack,
    [property: Description("Forecast times: the time and intensity beside each tropical cyclone's forecast points. On by default.")] bool ForecastTimes);

/// <summary>Reads and sets the Weather section's switches, as its toggles do.</summary>
internal sealed class WeatherDisplayTools
{
    /// <summary>The read tool's name.</summary>
    public const string GetName = "get_weather_display";

    /// <summary>The set tool's name.</summary>
    public const string SetName = "set_weather_display";

    private readonly EcdisDisplayPanelViewModel _panel;
    private readonly IRouteEditInvoker _invoker;

    public WeatherDisplayTools(EcdisDisplayPanelViewModel panel, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(invoker);
        _panel = panel;
        _invoker = invoker;
    }

    /// <summary>Returns the switches.</summary>
    public Task<ToolResult<WeatherDisplayDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<WeatherDisplayDto>.Ok(Dto(_panel)));
    }

    /// <summary>Sets the given switches (a null one is left as it is) and returns them all.</summary>
    public Task<ToolResult<WeatherDisplayDto>> SetAsync(
        bool? isobars = null,
        bool? windSpeedFill = null,
        bool? waveArrows = null,
        bool? isotherms = null,
        bool? currents = null,
        bool? airTemperature = null,
        bool? precipitation = null,
        bool? thunderstormRisk = null,
        bool? cloudCover = null,
        bool? windRadii = null,
        bool? pastTrack = null,
        bool? forecastTimes = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() =>
        {
            if (isobars is { } a) _panel.IsIsobarsVisible = a;
            if (windSpeedFill is { } b) _panel.IsWindSpeedFillVisible = b;
            if (waveArrows is { } c) _panel.IsWaveArrowsVisible = c;
            if (isotherms is { } d) _panel.IsIsothermsVisible = d;
            if (currents is { } e) _panel.IsCurrentArrowsVisible = e;
            if (airTemperature is { } f) _panel.IsAirTemperatureVisible = f;
            if (precipitation is { } j) _panel.IsPrecipitationVisible = j;
            if (thunderstormRisk is { } k) _panel.IsThunderstormRiskVisible = k;
            if (cloudCover is { } l) _panel.IsCloudCoverVisible = l;
            if (windRadii is { } g) _panel.IsWindRadiiVisible = g;
            if (pastTrack is { } h) _panel.IsPastTrackVisible = h;
            if (forecastTimes is { } i) _panel.IsForecastTimesVisible = i;
            return ToolResult<WeatherDisplayDto>.Ok(Dto(_panel));
        });
    }

    internal static WeatherDisplayDto Dto(EcdisDisplayPanelViewModel panel) => new(
        panel.HasWeatherData,
        panel.HasTropicalCyclone,
        panel.IsIsobarsVisible,
        panel.IsWindSpeedFillVisible,
        panel.IsWaveArrowsVisible,
        panel.IsIsothermsVisible,
        panel.IsCurrentArrowsVisible,
        panel.IsAirTemperatureVisible,
        panel.IsPrecipitationVisible,
        panel.IsThunderstormRiskVisible,
        panel.IsCloudCoverVisible,
        panel.IsWindRadiiVisible,
        panel.IsPastTrackVisible,
        panel.IsForecastTimesVisible);
}

/// <summary>Wraps <see cref="WeatherDisplayTools"/> as <see cref="McpServerTool"/>s.</summary>
internal static class WeatherDisplayMcpAdapters
{
    public static McpServerTool CreateGet(WeatherDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.GetAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = WeatherDisplayTools.GetName,
            Description =
                "Returns the Display controls Weather section's switches: isobars, wind speed fill, wave arrows, " +
                "isotherms, currents, air temperature, precipitation, thunderstorm risk and cloud cover for weather grids, and wind radii, past track and forecast " +
                "times for tropical cyclones, with whether the section and the cyclone switches show. Change them with " +
                "set_weather_display.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool CreateSet(WeatherDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Isobars on or off; omit to leave it.")] bool? isobars = null,
            [Description("Wind speed fill on or off; omit to leave it.")] bool? windSpeedFill = null,
            [Description("Wave arrows on or off; omit to leave it.")] bool? waveArrows = null,
            [Description("Sea temperature isotherms on or off; omit to leave it.")] bool? isotherms = null,
            [Description("Current arrows on or off; omit to leave it.")] bool? currents = null,
            [Description("Air temperature isotherms on or off; omit to leave it.")] bool? airTemperature = null,
            [Description("Precipitation rate fill on or off; omit to leave it.")] bool? precipitation = null,
            [Description("Thunderstorm risk (CAPE) fill on or off; omit to leave it.")] bool? thunderstormRisk = null,
            [Description("Cloud cover veil on or off; omit to leave it.")] bool? cloudCover = null,
            [Description("Tropical cyclone wind radii (34, 50 and 64 kt) on or off; omit to leave it.")] bool? windRadii = null,
            [Description("Tropical cyclone past track on or off; omit to leave it.")] bool? pastTrack = null,
            [Description("Tropical cyclone forecast time labels on or off; omit to leave it.")] bool? forecastTimes = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.SetAsync(
                isobars, windSpeedFill, waveArrows, isotherms, currents, airTemperature, precipitation, thunderstormRisk, cloudCover, windRadii, pastTrack, forecastTimes, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = WeatherDisplayTools.SetName,
            Description =
                "Sets Display controls Weather switches, as their toggles do; an omitted one is left as it is, and all are " +
                "kept after a restart. They apply to every weather dataset, whether or not the section shows. Returns what " +
                "get_weather_display returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
