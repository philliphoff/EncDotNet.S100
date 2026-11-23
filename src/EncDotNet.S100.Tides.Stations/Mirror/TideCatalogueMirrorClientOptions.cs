namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>Options for a <see cref="TideCatalogueMirrorClient"/>.</summary>
public sealed class TideCatalogueMirrorClientOptions
{
    /// <summary>
    /// The EncDotNet.S100 mirror's manifest: a rolling GitHub release asset,
    /// refreshed from NOAA CO-OPS every week.
    /// </summary>
    public static readonly Uri DefaultManifestAddress =
        new("https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/" + TideCatalogueManifest.DefaultFileName);

    /// <summary>
    /// The manifest's address. The catalogue file is fetched from the
    /// manifest's <see cref="TideCatalogueManifest.File"/>, relative to it.
    /// Defaults to <see cref="DefaultManifestAddress"/>.
    /// </summary>
    public Uri ManifestAddress { get; init; } = DefaultManifestAddress;

    /// <summary>
    /// How often a request that fails with a server error, a refusal, a
    /// timeout, or a catalogue file that doesn't match its manifest is tried
    /// again. Defaults to 2. A mirror replaces its two files one after the
    /// other, so a download that lands between them fetches the manifest
    /// again.
    /// </summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>The wait before the first retry; each further retry waits twice as long. Defaults to 2 seconds.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The largest catalogue file to download, in bytes. Defaults to 64 MB;
    /// NOAA's catalogue is under 1 MB.
    /// </summary>
    public long MaxCatalogueBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Checks the options.</summary>
    /// <exception cref="ArgumentException">An option is out of range.</exception>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(ManifestAddress);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(RetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCatalogueBytes, 1);
        if (!ManifestAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("The manifest address must be absolute.", nameof(ManifestAddress));
        }
    }
}
