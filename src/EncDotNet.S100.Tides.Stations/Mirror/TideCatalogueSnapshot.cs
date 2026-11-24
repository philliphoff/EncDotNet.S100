namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>
/// A copy of a mirror's catalogue shipped with an app: the catalogue file
/// and its manifest in one folder, used to fill the app's offline cache when
/// the cache is missing or older, so stations work on first launch with no
/// network.
/// </summary>
/// <remarks>
/// <para>
/// The folder holds <see cref="TideCatalogueManifest.DefaultFileName"/> and
/// the catalogue file it names, as a mirror publishes them. SoundCharts and
/// the <c>s100</c> CLI ship one in <see cref="DirectoryName"/> beside their
/// executables.
/// </para>
/// <para>
/// <see cref="SeedAsync"/> never replaces a cache that is as new or newer, by
/// <see cref="TideStationCatalogue.Retrieved"/>, nor one it can't date: an
/// undated cache, one from another provider, or one it can't read may be
/// stations someone wrote themselves.
/// </para>
/// </remarks>
public static class TideCatalogueSnapshot
{
    /// <summary>The folder name an app ships its snapshot in, beside its executable.</summary>
    public const string DirectoryName = "TideStations";

    /// <summary>
    /// The snapshot folder beside the running app,
    /// <c><see cref="AppContext.BaseDirectory"/>/<see cref="DirectoryName"/></c>.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    /// <summary>
    /// Copies the snapshot over a cache when the cache is missing or its
    /// stations were retrieved before the snapshot's.
    /// </summary>
    /// <param name="snapshotDirectory">The folder with the snapshot's manifest and catalogue file.</param>
    /// <param name="cachePath">The cache to fill: a <c>.json.gz</c> file gets the snapshot's bytes, any other path uncompressed JSON.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>
    /// <c>true</c> when the cache was filled from the snapshot; <c>false</c>
    /// when there's no usable snapshot (no folder, a manifest this library
    /// can't read, or a file that doesn't match it) or the cache was kept.
    /// </returns>
    /// <exception cref="IOException">The cache can't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The cache can't be written.</exception>
    public static async Task<bool> SeedAsync(string snapshotDirectory, string cachePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshotDirectory);
        ArgumentException.ThrowIfNullOrEmpty(cachePath);

        var manifestPath = Path.Combine(snapshotDirectory, TideCatalogueManifest.DefaultFileName);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        TideCatalogueManifest manifest;
        try
        {
            manifest = await TideCatalogueManifest.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (FormatException)
        {
            return false;
        }

        var snapshotFile = Path.Combine(snapshotDirectory, manifest.File);
        if (!manifest.IsSupported || !File.Exists(snapshotFile))
        {
            return false;
        }

        if (File.Exists(cachePath) && !await IsOlderAsync(cachePath, manifest, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var content = await File.ReadAllBytesAsync(snapshotFile, cancellationToken).ConfigureAwait(false);
        if (!manifest.Matches(content))
        {
            return false;
        }

        if (cachePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && content is [0x1f, 0x8b, ..])
        {
            await TideStationCatalogueFile.ReplaceFileAsync(
                cachePath,
                (stream, token) => stream.WriteAsync(content, token).AsTask(),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var catalogue = await TideStationCatalogueFile.LoadAsync(snapshotFile, cancellationToken).ConfigureAwait(false);
            await TideStationCatalogueFile.SaveAsync(cachePath, catalogue, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Whether a cache is the snapshot's provider's and was retrieved before the snapshot.</summary>
    private static async Task<bool> IsOlderAsync(string cachePath, TideCatalogueManifest manifest, CancellationToken cancellationToken)
    {
        (string? Provider, DateTimeOffset? Retrieved) header;
        try
        {
            header = await TideStationCatalogueFile.ReadHeaderAsync(cachePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException)
        {
            return false;
        }

        return header.Retrieved is { } retrieved
            && retrieved < manifest.Retrieved
            && string.Equals(header.Provider, manifest.Provider, StringComparison.Ordinal);
    }
}
