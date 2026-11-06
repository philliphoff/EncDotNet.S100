using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Settings › Chart display › Mariner Settings › Weather on a route (#1093).</summary>
[Description("Check route's weather limits (Settings › Chart display › Mariner Settings › Weather on a route), kept with the mariner settings and after a restart. A change re-runs an open route check.")]
internal sealed record RouteWeatherSettingsDto(
    [property: Description("The wind limit in knots: stretches where the loaded GRIB2 or NetCDF forecast has a higher 10 m wind at the ETA are weather.wind_above_limit findings (check). Null when off (the default): wind is then checked only against an RTZ route's own vesselMaxWind.")] double? MaxWindKnots,
    [property: Description("The significant wave height limit in metres (weather.waves_above_limit). Null when off (the default): waves are then checked only against an RTZ route's own vesselMaxWave.")] double? MaxWaveHeightMeters);

/// <summary>Reads and sets Check route's weather limits, as the Settings boxes do.</summary>
internal sealed class RouteWeatherSettingsTools
{
    /// <summary>The read tool's name.</summary>
    public const string GetName = "get_route_weather_settings";

    /// <summary>The set tool's name.</summary>
    public const string SetName = "set_route_weather_settings";

    private static readonly string[] Names = ["maxWindKnots", "maxWaveHeightMeters"];

    private readonly SettingsViewModel _settings;
    private readonly IRouteEditInvoker _invoker;

    public RouteWeatherSettingsTools(SettingsViewModel settings, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(invoker);
        _settings = settings;
        _invoker = invoker;
    }

    /// <summary>Returns the limits.</summary>
    public Task<ToolResult<RouteWeatherSettingsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<RouteWeatherSettingsDto>.Ok(Dto(_settings)));
    }

    /// <summary>Sets the given limits, clears the named ones, and returns them all.</summary>
    /// <param name="maxWindKnots">The wind limit in knots, or <c>null</c> to leave it.</param>
    /// <param name="maxWaveHeightMeters">The wave limit in metres, or <c>null</c> to leave it.</param>
    /// <param name="clear">Names of limits to turn off.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ToolResult<RouteWeatherSettingsDto>> SetAsync(
        double? maxWindKnots,
        double? maxWaveHeightMeters,
        IReadOnlyList<string>? clear,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxWindKnots is { } wind && (!double.IsFinite(wind) || wind <= 0))
            return Task.FromResult(ToolResult<RouteWeatherSettingsDto>.Err(new InvalidArgument("maxWindKnots", "must be a positive number of knots")));
        if (maxWaveHeightMeters is { } wave && (!double.IsFinite(wave) || wave <= 0))
            return Task.FromResult(ToolResult<RouteWeatherSettingsDto>.Err(new InvalidArgument("maxWaveHeightMeters", "must be a positive number of metres")));
        if (clear?.FirstOrDefault(n => !Names.Contains(n, StringComparer.Ordinal)) is { } unknown)
            return Task.FromResult(ToolResult<RouteWeatherSettingsDto>.Err(new InvalidArgument("clear", $"'{unknown}' isn't a limit: {string.Join(", ", Names)}")));

        return _invoker.InvokeAsync(() =>
        {
            if (clear?.Contains("maxWindKnots", StringComparer.Ordinal) == true) _settings.RouteMaxWindKnots = null;
            if (clear?.Contains("maxWaveHeightMeters", StringComparer.Ordinal) == true) _settings.RouteMaxWaveHeightMetres = null;
            if (maxWindKnots is { } w) _settings.RouteMaxWindKnots = w;
            if (maxWaveHeightMeters is { } h) _settings.RouteMaxWaveHeightMetres = h;
            return ToolResult<RouteWeatherSettingsDto>.Ok(Dto(_settings));
        });
    }

    internal static RouteWeatherSettingsDto Dto(SettingsViewModel settings) =>
        new(settings.RouteMaxWindKnots, settings.RouteMaxWaveHeightMetres);
}

/// <summary>Wraps <see cref="RouteWeatherSettingsTools"/> as <see cref="McpServerTool"/>s.</summary>
internal static class RouteWeatherSettingsMcpAdapters
{
    public static McpServerTool CreateGet(RouteWeatherSettingsTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.GetAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = RouteWeatherSettingsTools.GetName,
            Description =
                "Returns Check route's weather limits (Settings › Chart display › Mariner Settings › Weather on a route): " +
                "maxWindKnots and maxWaveHeightMeters, null when off. The weather check compares the loaded GRIB2 or NetCDF " +
                "forecast along the route at each ETA with them; where one is off, an RTZ route's own vesselMaxWind or " +
                "vesselMaxWave is used. Change them with set_route_weather_settings.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool CreateSet(RouteWeatherSettingsTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The wind limit in knots (positive); omit to leave it.")] double? maxWindKnots = null,
            [Description("The significant wave height limit in metres (positive); omit to leave it.")] double? maxWaveHeightMeters = null,
            [Description("Limits to turn off: maxWindKnots, maxWaveHeightMeters.")] IReadOnlyList<string>? clear = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.SetAsync(maxWindKnots, maxWaveHeightMeters, clear, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = RouteWeatherSettingsTools.SetName,
            Description =
                "Sets Check route's weather limits, as the Settings boxes do: values you pass overwrite, values you omit are " +
                "unchanged, and names in clear are turned off. They're kept with the mariner settings and after a restart, and " +
                "a change re-runs an open route check. Returns what get_route_weather_settings returns. Viewer-injected tool — " +
                "mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
