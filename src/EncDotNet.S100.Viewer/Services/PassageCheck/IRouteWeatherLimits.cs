using EncDotNet.S100.Navigation;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The mariner's wind and wave limits for Check route's weather check
/// (#1093): Settings › Chart display › Mariner Settings › Weather on a
/// route. Kept with the mariner settings, not with a route.
/// </summary>
internal interface IRouteWeatherLimits
{
    /// <summary>The limits set, or <c>null</c> when neither is.</summary>
    WeatherLimits? Limits { get; }

    /// <summary>Raised after either limit changes.</summary>
    event Action? RouteWeatherLimitsChanged;
}

/// <summary>The route's own wind and wave limits, which the mariner's replace value by value.</summary>
internal static class RouteVesselLimits
{
    /// <summary>
    /// The limits a route carries: an RTZ route's <c>vesselMaxWind</c>
    /// (knots) and <c>vesselMaxWave</c> (metres), kept from its file. S-421
    /// has none.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>The limits, or <c>null</c>.</returns>
    public static WeatherLimits? Of(Route route) =>
        route.Preserved?.Model is RtzRoute rtz ? rtz.VesselLimits() : null;
}
