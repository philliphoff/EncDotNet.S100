namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Decodes one time step of one parameter from the file: a GRIB2 message's
/// data section, or one time slice of a NetCDF variable.
/// </summary>
/// <param name="cancellationToken">Signals that the caller no longer needs the step.</param>
/// <returns>
/// The step's values in the file's row-major order (see
/// <see cref="WeatherNativeGrid"/>), in the parameter's stored unit, with
/// packing (CF <c>scale_factor</c> and <c>add_offset</c>) applied. Points a
/// GRIB2 bitmap marks as missing are <see cref="float.NaN"/>; other missing
/// points may keep their sentinel, listed in
/// <see cref="WeatherParameter.MissingValues"/>.
/// </returns>
/// <remarks>
/// The model calls the decoder only when a step is needed, so a reader indexes
/// its messages or variables when the file opens and decodes later. The
/// decoder may be called more than once for the same step, after the step
/// has been evicted from a cache.
/// </remarks>
public delegate float[] WeatherStepDecoder(CancellationToken cancellationToken);
