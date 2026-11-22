namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// One time step of a weather parameter: its valid time, the interval its
/// values are a statistic over when they are one, and a callback that decodes
/// its values when they're needed.
/// </summary>
public sealed class WeatherTimeStep
{
    private readonly WeatherStepDecoder _decoder;

    /// <summary>Creates a time step whose values are decoded on demand.</summary>
    /// <param name="validTime">The time the values are valid for, in UTC.</param>
    /// <param name="decoder">Decodes the step's values in the file's order.</param>
    /// <param name="interval">
    /// The interval the values are a statistic over (an accumulation, a
    /// maximum), or <see langword="null"/> for values at <paramref name="validTime"/>.
    /// Its end should be <paramref name="validTime"/>.
    /// </param>
    public WeatherTimeStep(DateTime validTime, WeatherStepDecoder decoder, WeatherInterval? interval = null)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ValidTime = validTime;
        Interval = interval;
        _decoder = decoder;
    }

    /// <summary>The time the values are valid for, in UTC.</summary>
    public DateTime ValidTime { get; }

    /// <summary>
    /// The interval the values are a statistic over, for example the
    /// precipitation accumulated in the 3 hours to <see cref="ValidTime"/>;
    /// <see langword="null"/> for values at <see cref="ValidTime"/>.
    /// </summary>
    public WeatherInterval? Interval { get; }

    /// <summary>
    /// Creates a time step over values already in memory, for example a grid
    /// built in a test.
    /// </summary>
    /// <param name="validTime">The time the values are valid for, in UTC.</param>
    /// <param name="nativeValues">The values in the file's row-major order. The array isn't copied.</param>
    /// <param name="interval">The interval the values are a statistic over, or <see langword="null"/>.</param>
    public static WeatherTimeStep FromValues(DateTime validTime, float[] nativeValues, WeatherInterval? interval = null)
    {
        ArgumentNullException.ThrowIfNull(nativeValues);
        return new WeatherTimeStep(validTime, _ => nativeValues, interval);
    }

    /// <summary>
    /// Decodes the step's values in the file's order. Prefer
    /// <see cref="WeatherGridSet.ReadStep"/>, which also normalises them.
    /// </summary>
    /// <param name="cancellationToken">Signals that the caller no longer needs the step.</param>
    /// <returns>The values in the file's row-major order.</returns>
    public float[] Decode(CancellationToken cancellationToken = default) => _decoder(cancellationToken);
}
