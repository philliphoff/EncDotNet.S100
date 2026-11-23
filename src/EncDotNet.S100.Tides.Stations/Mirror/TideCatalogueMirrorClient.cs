using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>
/// Downloads a tide and current station catalogue from a mirror: one small
/// manifest to check for newer data, and one file, checked against the
/// manifest's size and SHA-256, to download it.
/// </summary>
/// <remarks>
/// <para>
/// This is the quick way to get NOAA CO-OPS's catalogue. By default it reads
/// the EncDotNet.S100 mirror
/// (<see cref="TideCatalogueMirrorClientOptions.DefaultManifestAddress"/>),
/// which downloads NOAA's stations every week with
/// <see cref="Noaa.NoaaTideStationClient"/> and publishes a new file only
/// when NOAA's data changed. A check is one request of a few hundred bytes
/// and a download one of about 400 KB, where a download from NOAA itself is
/// some 3,400 paced requests over several minutes.
/// </para>
/// <para>
/// The catalogue is NOAA's: its <see cref="TideStationCatalogue.Provider"/>
/// and <see cref="TideStationCatalogue.Retrieved"/> say where its stations
/// come from and when they were read from NOAA, not when the mirror
/// published them.
/// </para>
/// </remarks>
public sealed class TideCatalogueMirrorClient
{
    private readonly HttpClient _http;
    private readonly TideCatalogueMirrorClientOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Creates a client.</summary>
    /// <param name="httpClient">
    /// The client to fetch with. The caller owns it: its lifetime, timeout
    /// and <c>User-Agent</c>. It must follow redirects (the default), since
    /// GitHub release downloads redirect.
    /// </param>
    /// <param name="options">The options; <c>null</c> for the defaults.</param>
    /// <param name="timeProvider">The clock, for retry waits and the check's date; <c>null</c> for the system clock.</param>
    public TideCatalogueMirrorClient(HttpClient httpClient, TideCatalogueMirrorClientOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _options = options ?? new TideCatalogueMirrorClientOptions();
        _options.Validate();
        _http = httpClient;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Reads the mirror's manifest.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="HttpRequestException">The request failed after its retries, or the mirror has no manifest.</exception>
    /// <exception cref="FormatException">The manifest isn't valid.</exception>
    public Task<TideCatalogueManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            _options.ManifestAddress,
            notFoundIsTransient: false,
            async (content, token) =>
            {
                var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    return await TideCatalogueManifest.ReadAsync(stream, token).ConfigureAwait(false);
                }
            },
            cancellationToken);

    /// <summary>
    /// Checks whether the mirror has newer data than a catalogue: reads the
    /// manifest (one request) and compares its retrieval date with the
    /// catalogue's.
    /// </summary>
    /// <param name="catalogue">The catalogue to check; <c>null</c> when there's none yet.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The manifest and whether it's newer.</returns>
    /// <exception cref="HttpRequestException">The request failed after its retries, or the mirror has no manifest.</exception>
    /// <exception cref="FormatException">The manifest isn't valid.</exception>
    public async Task<TideCatalogueMirrorCheck> CheckForUpdatesAsync(TideStationCatalogue? catalogue, CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(cancellationToken).ConfigureAwait(false);
        return new TideCatalogueMirrorCheck
        {
            Checked = _time.GetUtcNow(),
            Manifest = manifest,
            HasNewerData = manifest.IsSupported && (catalogue?.Retrieved is not { } retrieved || manifest.Retrieved > retrieved),
        };
    }

    /// <summary>
    /// Downloads the mirror's catalogue: reads the manifest, then the
    /// catalogue file, and checks the file's size and SHA-256 against the
    /// manifest before reading it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The catalogue and its manifest.</returns>
    /// <exception cref="HttpRequestException">A request failed after its retries.</exception>
    /// <exception cref="InvalidDataException">The file didn't match its manifest after the retries, or is larger than <see cref="TideCatalogueMirrorClientOptions.MaxCatalogueBytes"/>.</exception>
    /// <exception cref="NotSupportedException">The catalogue is in a format or version this library can't read.</exception>
    /// <exception cref="FormatException">The manifest or the catalogue isn't valid.</exception>
    public async Task<TideCatalogueMirrorDownload> DownloadCatalogueAsync(CancellationToken cancellationToken = default)
    {
        var (_, download) = await DownloadVerifiedAsync(cancellationToken).ConfigureAwait(false);
        return download;
    }

    /// <summary>
    /// Downloads the mirror's catalogue as <see cref="DownloadCatalogueAsync(CancellationToken)"/>
    /// does and saves it, replacing the file in one step. A path ending in
    /// <c>.gz</c> gets the mirror's file exactly as published; any other
    /// path, the catalogue as uncompressed JSON.
    /// </summary>
    /// <param name="path">Where to save the catalogue.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The catalogue and its manifest.</returns>
    /// <exception cref="HttpRequestException">A request failed after its retries.</exception>
    /// <exception cref="InvalidDataException">The file didn't match its manifest after the retries, or is larger than <see cref="TideCatalogueMirrorClientOptions.MaxCatalogueBytes"/>.</exception>
    /// <exception cref="NotSupportedException">The catalogue is in a format or version this library can't read.</exception>
    /// <exception cref="FormatException">The manifest or the catalogue isn't valid.</exception>
    public async Task<TideCatalogueMirrorDownload> DownloadCatalogueAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var (content, download) = await DownloadVerifiedAsync(cancellationToken).ConfigureAwait(false);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && IsGzip(content))
        {
            await TideStationCatalogueFile.ReplaceFileAsync(
                path,
                (stream, token) => stream.WriteAsync(content, token).AsTask(),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await TideStationCatalogueFile.SaveAsync(path, download.Catalogue, cancellationToken).ConfigureAwait(false);
        }

        return download;
    }

    private async Task<(byte[] Content, TideCatalogueMirrorDownload Download)> DownloadVerifiedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var manifest = await GetManifestAsync(cancellationToken).ConfigureAwait(false);
            if (!manifest.IsSupported)
            {
                throw new NotSupportedException(
                    $"The mirror's catalogue is in format '{manifest.CatalogueFormat}' version {manifest.CatalogueVersion}, which this library can't read; it reads '{TideStationCatalogueFile.FormatName}' up to version {TideStationCatalogueFile.CurrentVersion}.");
            }

            if (manifest.Size > _options.MaxCatalogueBytes)
            {
                throw new InvalidDataException($"The mirror's catalogue is {manifest.Size} bytes, more than the {_options.MaxCatalogueBytes} allowed.");
            }

            var address = new Uri(_options.ManifestAddress, manifest.File);
            var content = await SendAsync(
                address,
                notFoundIsTransient: true,
                (body, token) => ReadLimitedAsync(body, manifest.Size, token),
                cancellationToken).ConfigureAwait(false);
            if (manifest.Matches(content))
            {
                return (content, new TideCatalogueMirrorDownload(await ReadCatalogueAsync(content, cancellationToken).ConfigureAwait(false), manifest));
            }

            // The mirror replaces the file and the manifest one after the
            // other: a download between the two sees a file its manifest
            // doesn't describe. Read both again.
            if (attempt >= _options.MaxRetries)
            {
                throw new InvalidDataException($"The catalogue at {address} doesn't match its manifest's size and SHA-256.");
            }

            await WaitAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<TideStationCatalogue> ReadCatalogueAsync(byte[] content, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream(content, writable: false);
        await using (stream.ConfigureAwait(false))
        {
            if (!IsGzip(content))
            {
                return await TideStationCatalogueFile.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            var gzip = new GZipStream(stream, CompressionMode.Decompress);
            await using (gzip.ConfigureAwait(false))
            {
                try
                {
                    return await TideStationCatalogueFile.ReadAsync(gzip, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException e)
                {
                    throw new FormatException($"The mirror's catalogue isn't valid gzip: {e.Message}", e);
                }
            }
        }
    }

    private static bool IsGzip(ReadOnlySpan<byte> content) => content.Length >= 2 && content[0] == 0x1f && content[1] == 0x8b;

    /// <summary>Reads a body of an expected size, refusing one that's larger: a mismatch is caught by the hash.</summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpContent body, long expected, CancellationToken cancellationToken)
    {
        if (body.Headers.ContentLength is { } length && length != expected)
        {
            // Read nothing: the caller sees a mismatch and tries again.
            return [];
        }

        var stream = await body.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            // One byte more than expected tells a longer file from the right one.
            var buffer = new byte[expected + 1];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            return read == expected ? buffer[..read] : [];
        }
    }

    private async Task<T> SendAsync<T>(
        Uri address,
        bool notFoundIsTransient,
        Func<HttpContent, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                // The file's bytes are checked against the manifest, so ask for them as published.
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await read(response.Content, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (attempt < _options.MaxRetries && IsTransient(e, notFoundIsTransient, cancellationToken))
            {
                await WaitAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task WaitAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = _options.RetryDelay * Math.Pow(2, attempt);
        return delay > TimeSpan.Zero ? Task.Delay(delay, _time, cancellationToken) : Task.CompletedTask;
    }

    private static bool IsTransient(Exception e, bool notFoundIsTransient, CancellationToken cancellationToken) => e switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => notFoundIsTransient,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500
            || status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout,
        TaskCanceledException => !cancellationToken.IsCancellationRequested,
        IOException => true,
        _ => false,
    };
}
