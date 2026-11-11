using EncDotNet.S100.Nmea;

namespace EncDotNet.S100.DynamicSources.Nmea;

/// <summary>Configures an <see cref="NmeaWeatherSource"/>.</summary>
public sealed record NmeaWeatherSourceOptions
{
    /// <summary>The source's <see cref="IDynamicFeatureSource.Id"/>. Defaults to <c>"nmea-weather"</c>.</summary>
    public string Id { get; init; } = "nmea-weather";

    /// <summary>The source's display name. Defaults to <c>"Own-ship wind and weather (NMEA)"</c>.</summary>
    public string DisplayName { get; init; } = "Own-ship wind and weather (NMEA)";

    /// <summary>
    /// How old a heading, speed or course may be and still be combined with a
    /// wind reading to make true wind. Instruments send these once a second or
    /// faster, so the default is 10 seconds: older, and the vessel may have
    /// turned.
    /// </summary>
    public TimeSpan MaxMotionAge { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a value is kept without a new sentence: past this a wind,
    /// temperature, pressure or NMEA position is dropped, and when nothing is
    /// left the feature goes. Defaults to 1 minute.
    /// </summary>
    public TimeSpan DropAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The shortest time between two <see cref="IDynamicFeatureSource.Changed"/>
    /// events. Instruments send several sentences a second; the feature is
    /// republished at most this often. Defaults to 1 second.
    /// </summary>
    public TimeSpan PublishInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The wait before reading the input again after it fails. Defaults to 5 seconds.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The sentence parse options. Defaults to <see cref="NmeaParseOptions.Default"/>: checksums required.</summary>
    public NmeaParseOptions ParseOptions { get; init; } = NmeaParseOptions.Default;

    /// <summary>Throws when an option is out of range.</summary>
    /// <exception cref="ArgumentException">An option is out of range.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrEmpty(Id);
        ArgumentException.ThrowIfNullOrEmpty(DisplayName);
        ArgumentNullException.ThrowIfNull(ParseOptions);
        if (MaxMotionAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxMotionAge), MaxMotionAge, "Must be positive.");
        if (DropAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DropAfter), DropAfter, "Must be positive.");
        if (PublishInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PublishInterval), PublishInterval, "Can't be negative.");
        if (RetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RetryDelay), RetryDelay, "Must be positive.");
    }
}
