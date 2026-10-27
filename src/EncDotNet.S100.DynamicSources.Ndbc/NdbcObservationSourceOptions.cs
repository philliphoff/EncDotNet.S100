namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>Configures an <see cref="NdbcObservationSource"/>.</summary>
public sealed record NdbcObservationSourceOptions
{
    /// <summary>The default NDBC web site, <c>https://www.ndbc.noaa.gov/</c>.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://www.ndbc.noaa.gov/");

    /// <summary>The source's <see cref="IDynamicFeatureSource.Id"/>. Defaults to <c>"ndbc"</c>.</summary>
    public string Id { get; init; } = "ndbc";

    /// <summary>The source's display name. Defaults to <c>"NDBC buoy observations"</c>.</summary>
    public string DisplayName { get; init; } = "NDBC buoy observations";

    /// <summary>The site the station table and observation paths are relative to. Defaults to <see cref="DefaultBaseAddress"/>.</summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>The station table's path, relative to <see cref="BaseAddress"/>. Defaults to <c>activestations.xml</c>.</summary>
    public string StationTablePath { get; init; } = "activestations.xml";

    /// <summary>
    /// The path of a station's standard meteorological file, relative to
    /// <see cref="BaseAddress"/>, with <c>{0}</c> for the station id. Defaults
    /// to <c>data/realtime2/{0}.txt</c>.
    /// </summary>
    public string ObservationPathFormat { get; init; } = "data/realtime2/{0}.txt";

    /// <summary>
    /// How often a station is fetched again: 10 minutes by default, the
    /// shortest interval NDBC updates its files at.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The age past which an observation is drawn as stale: 3 hours by default.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromHours(3);

    /// <summary>The age past which an observation is dropped: 12 hours by default.</summary>
    public TimeSpan DropAfter { get; init; } = TimeSpan.FromHours(12);

    /// <summary>How long the station table is kept before it is fetched again: 1 day by default.</summary>
    public TimeSpan StationTableRefreshInterval { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The longest wait after repeated errors. A station that fails waits
    /// <see cref="PollInterval"/>, then twice that, and so on, up to this; so
    /// does the station table. Defaults to 2 hours.
    /// </summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// The first wait after the station table fails to load, doubled on each
    /// failure up to <see cref="MaxBackoff"/>. Defaults to 1 minute.
    /// </summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How far beyond the viewport stations are fetched, in degrees on every
    /// side, so a short pan doesn't leave the edges empty. Defaults to 0.5°.
    /// </summary>
    public double MarginDegrees { get; init; } = 0.5;

    /// <summary>
    /// The widest viewport, in degrees of latitude or longitude, that stations
    /// are fetched for. Zoomed out further, the source fetches nothing new (as
    /// the AIS source waits, ais-zoom-gated-subscription.md) and keeps what it
    /// has until it ages out. <see langword="null"/> for no limit. Defaults to 25°.
    /// </summary>
    public double? MaxViewportSpanDegrees { get; init; } = 25;

    /// <summary>
    /// The most stations fetched per poll; the ones nearest the viewport's
    /// centre go first. Defaults to 60.
    /// </summary>
    public int MaxStationsPerPoll { get; init; } = 60;

    /// <summary>The most requests in flight at once. Defaults to 4.</summary>
    public int MaxConcurrentRequests { get; init; } = 4;

    /// <summary>
    /// How many bytes of each file to ask for with an HTTP <c>Range</c> header.
    /// The files list 45 days newest first, so the head holds the latest hours.
    /// A server that ignores the range sends the whole file; the source stops
    /// reading once rows are older than <see cref="DropAfter"/>. <see langword="null"/>
    /// sends no range. Defaults to 16 KiB, about a day of 10-minute rows.
    /// </summary>
    public int? RangeBytes { get; init; } = 16 * 1024;

    /// <summary>
    /// How much older than a station's newest row its other values may be
    /// (see <see cref="NdbcObservation.Combine"/>). Defaults to 1 hour, the
    /// wave reporting interval.
    /// </summary>
    public TimeSpan CombineWindow { get; init; } = TimeSpan.FromHours(1);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentNullException.ThrowIfNull(BaseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(StationTablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ObservationPathFormat);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StaleAfter, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(DropAfter, StaleAfter);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(InitialBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBackoff, InitialBackoff);
        ArgumentOutOfRangeException.ThrowIfNegative(MarginDegrees);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStationsPerPoll);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConcurrentRequests);
        if (RangeBytes is { } range)
            ArgumentOutOfRangeException.ThrowIfLessThan(range, 512);
    }
}
