namespace EncDotNet.S100.Collections;

/// <summary>
/// One forecast model of an S-100 forecast feed (#685), for example NOAA's
/// Chesapeake Bay Operational Forecast System (<c>cbofs</c>) for S-111 surface
/// currents. Its catalogue lists only its latest run.
/// </summary>
/// <param name="Id">The model's folder under the feed (e.g. <c>cbofs</c>).</param>
/// <param name="Name">The water body it covers (e.g. "Chesapeake Bay").</param>
/// <param name="CadenceHours">How often a new run is published, in hours.</param>
/// <param name="HorizonHours">How far ahead of its run time a run forecasts, in hours (its valid window).</param>
public sealed record ForecastModel(string Id, string Name, int CadenceHours, int HorizonHours);

/// <summary>How a forecast feed's runs are downloaded.</summary>
public enum ForecastShape
{
    /// <summary>One file per tile of the run, on the same tile grid as S-102.</summary>
    Tiles,

    /// <summary>One file per model run, covering the model's whole domain.</summary>
    Regional,
}
