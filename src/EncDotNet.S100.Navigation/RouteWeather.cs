using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// A value a <see cref="IRouteWeatherSource"/> reads along a route, in SI
/// units. The weather check reads wind speed and significant wave height; a
/// later check may read more, such as surface currents for ETAs (#1033).
/// </summary>
public enum WeatherQuantity
{
    /// <summary>Wind speed at 10 m, in metres per second.</summary>
    WindSpeed,

    /// <summary>Significant height of combined wind waves and swell, in metres.</summary>
    SignificantWaveHeight,
}

/// <summary>Where a <see cref="IRouteWeatherSeries"/> comes from.</summary>
/// <param name="DatasetId">The catalog id of the weather dataset (a GRIB2 or NetCDF file).</param>
/// <param name="ModelRun">The model run's reference time (UTC), or <c>null</c> when the data doesn't give one.</param>
/// <param name="GridCell">The grid node read, as <c>row,col</c>, or <c>null</c> for a source without one.</param>
public sealed record RouteWeatherSourceInfo(string DatasetId, DateTimeOffset? ModelRun, string? GridCell = null);

/// <summary>
/// The values of one quantity at one place, over a weather dataset's time
/// steps: a grid node of a forecast model.
/// </summary>
/// <remarks>
/// A series answers at its time steps only. A time between two steps reads
/// the nearer one (no interpolation), as the weather layers and picks show
/// it, so the check agrees with what's drawn.
/// </remarks>
public interface IRouteWeatherSeries
{
    /// <summary>Where the series comes from.</summary>
    RouteWeatherSourceInfo Source { get; }

    /// <summary>The series' time steps (UTC), ascending; at least one.</summary>
    IReadOnlyList<DateTimeOffset> Steps { get; }

    /// <summary>
    /// The value at a step, in the quantity's SI unit, or <c>null</c> when
    /// the step has no value there (a masked cell, such as waves over land).
    /// </summary>
    /// <param name="stepIndex">Index into <see cref="Steps"/>.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    double? ValueAt(int stepIndex);
}

/// <summary>
/// Supplies the weather a <see cref="WeatherCheck"/> checks a route against:
/// for a position and a quantity, the series of every dataset that covers
/// it. <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over loaded
/// GRIB2 and NetCDF weather grids.
/// </summary>
/// <remarks>
/// <para>
/// Weather model output isn't official S-100 data. Findings from it are
/// advisory, and like the weather layers they aren't for navigation.
/// </para>
/// <para>Implementations must be safe to call from several threads at once.</para>
/// </remarks>
public interface IRouteWeatherSource
{
    /// <summary>Whether any dataset carries the quantity, so a check can tell "no wave data" from "no wave data here".</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns><c>true</c> when some dataset carries it.</returns>
    bool Carries(WeatherQuantity quantity);

    /// <summary>
    /// The series of every dataset that covers a position for a quantity,
    /// best first: the finest grid, then the newest model run, then the
    /// latest last step. The check reads the first one whose time steps
    /// cover the time it needs.
    /// </summary>
    /// <param name="position">The position, in WGS-84.</param>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The series, or an empty list when no dataset covers the position.</returns>
    IReadOnlyList<IRouteWeatherSeries> Locate(GeoPosition position, WeatherQuantity quantity);
}
