namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Where a weather grid set comes from: the model or centre that produced it
/// and the model run's reference time.
/// </summary>
/// <param name="Model">
/// The model name, for example <c>"GFS-Wave"</c>, or the originating centre's
/// name when the file doesn't name a model.
/// </param>
/// <param name="RunTime">
/// The reference time of the model run (GRIB2 Section 1 reference time, or
/// the CF <c>forecast_reference_time</c>), in UTC. For an analysis it's the
/// analysis time.
/// </param>
/// <param name="Centre">The originating centre, for example <c>"NCEP"</c>, when known.</param>
public sealed record WeatherOrigin(string Model, DateTime RunTime, string? Centre = null);
