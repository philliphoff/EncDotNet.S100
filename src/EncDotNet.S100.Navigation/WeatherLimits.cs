using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The wind and sea a vessel is limited to, which the <see cref="WeatherCheck"/>
/// compares the forecast along a route with. A value left <c>null</c> isn't
/// checked.
/// </summary>
/// <remarks>
/// A plan carries the vessel's own limits (<see cref="PassagePlan.VesselLimits"/>,
/// RTZ <c>vesselMaxWind</c> and <c>vesselMaxWave</c>); the settings of a run
/// (<see cref="RouteCheckSettings.WeatherLimits"/>) replace them value by value.
/// </remarks>
public sealed record WeatherLimits
{
    /// <summary>No limits.</summary>
    public static WeatherLimits None { get; } = new();

    /// <summary>The highest wind speed at 10 m, or <c>null</c> to leave wind unchecked.</summary>
    public Speed? MaxWind { get; init; }

    /// <summary>The highest significant wave height, or <c>null</c> to leave waves unchecked.</summary>
    public Length? MaxWaveHeight { get; init; }

    /// <summary>Whether neither value is set.</summary>
    public bool IsEmpty => MaxWind is null && MaxWaveHeight is null;

    /// <summary>The limit for a quantity, in its SI unit (m/s, m), or <c>null</c> when it isn't set.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The limit, or <c>null</c>.</returns>
    public double? Limit(WeatherQuantity quantity) => quantity switch
    {
        WeatherQuantity.WindSpeed => MaxWind?.TotalMetresPerSecond,
        WeatherQuantity.SignificantWaveHeight => MaxWaveHeight?.TotalMetres,
        _ => null,
    };
}

/// <summary>Where a limit a <see cref="WeatherCheck"/> used came from.</summary>
public enum WeatherLimitOrigin
{
    /// <summary>The settings of the run (<see cref="RouteCheckSettings.WeatherLimits"/>).</summary>
    Settings,

    /// <summary>The plan's vessel limits (<see cref="PassagePlan.VesselLimits"/>), such as an RTZ route's.</summary>
    Route,
}

/// <summary>A limit a <see cref="WeatherCheck"/> used, and where it came from.</summary>
/// <param name="Quantity">The quantity limited.</param>
/// <param name="Value">The limit, in the quantity's SI unit (m/s, m).</param>
/// <param name="Origin">Where it came from.</param>
public sealed record WeatherLimitUsed(WeatherQuantity Quantity, double Value, WeatherLimitOrigin Origin);
