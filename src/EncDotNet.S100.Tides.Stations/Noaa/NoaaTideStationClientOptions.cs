namespace EncDotNet.S100.Tides.Stations.Noaa;

/// <summary>Options for a <see cref="NoaaTideStationClient"/>.</summary>
public sealed class NoaaTideStationClientOptions
{
    /// <summary>The CO-OPS metadata API's address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.tidesandcurrents.noaa.gov/mdapi/prod/webapi/");

    /// <summary>
    /// The metadata API's address, ending in <c>/</c>. Defaults to
    /// <see cref="DefaultBaseAddress"/>.
    /// </summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>
    /// The most requests a catalogue download makes at once. Defaults to 2:
    /// the metadata API refuses a client that asks too fast (HTTP 403) for
    /// some minutes.
    /// </summary>
    public int MaxConcurrency { get; init; } = 2;

    /// <summary>
    /// The pause after each request of a catalogue download, per concurrent
    /// request. Defaults to 250 ms, so a download asks at most about eight
    /// times a second.
    /// </summary>
    public TimeSpan RequestInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often a request that fails with a server error, a refusal or a
    /// timeout is tried again before the download fails. Defaults to 4,
    /// waiting 5, 10, 20 and 40 seconds.
    /// </summary>
    public int MaxRetries { get; init; } = 4;

    /// <summary>The wait before the first retry; each further retry waits twice as long.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The reference stations whose datums <see cref="NoaaTideStationClient.CheckForUpdatesAsync"/>
    /// reads to see whether NOAA has released a new tidal datum epoch:
    /// long-running stations around the US. Defaults to Seattle (9447130),
    /// The Battery (8518750), San Francisco (9414290), Honolulu (1612340)
    /// and Key West (8724580).
    /// </summary>
    public IReadOnlyList<string> SentinelStationIds { get; init; } = ["9447130", "8518750", "9414290", "1612340", "8724580"];

    /// <summary>Checks the options.</summary>
    /// <exception cref="ArgumentException">An option is out of range.</exception>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(BaseAddress);
        ArgumentNullException.ThrowIfNull(SentinelStationIds);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(RequestInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(RetryDelay, TimeSpan.Zero);
        if (!BaseAddress.IsAbsoluteUri || !BaseAddress.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("The base address must be absolute and end in '/'.", nameof(BaseAddress));
        }
    }
}
