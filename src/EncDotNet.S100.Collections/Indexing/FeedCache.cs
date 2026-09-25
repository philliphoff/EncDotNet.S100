using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// A local copy of a downloaded feed document, as returned by
/// <see cref="FeedCache.GetAsync"/>.
/// </summary>
/// <param name="FilePath">The cached document on disk.</param>
/// <param name="Version">
/// An opaque version token: the server's ETag, else its Last-Modified time,
/// else the document's length and hash.
/// </param>
/// <param name="FetchedAt">When the copy was last confirmed current with the server.</param>
/// <param name="StaleReason">
/// When the server could not be reached and an older copy is being served,
/// why; otherwise <see langword="null"/>.
/// </param>
internal sealed record FeedSnapshot(string FilePath, string Version, DateTimeOffset FetchedAt, string? StaleReason);

/// <summary>
/// Downloads feed documents (catalogues) over HTTP into a local cache and
/// revalidates them with conditional requests, so a large catalogue is only
/// transferred when it has changed.
/// </summary>
/// <remarks>
/// <para>
/// Each URL is cached as <c>&lt;hash&gt;.xml</c> plus a <c>&lt;hash&gt;.json</c>
/// record of its ETag, Last-Modified and fetch time. A copy confirmed within
/// <see cref="FeedCacheOptions.RevalidationInterval"/> is used without any
/// request; after that, an <c>If-None-Match</c> / <c>If-Modified-Since</c>
/// request is made and a <c>304 Not Modified</c> keeps the copy.
/// </para>
/// <para>
/// Downloads are written to a <c>.partial</c> file and moved into place only
/// when complete, so an interrupted transfer never replaces a good copy. When
/// the server cannot be reached, an existing copy is served with a
/// <see cref="FeedSnapshot.StaleReason"/>.
/// </para>
/// </remarks>
internal sealed class FeedCache
{
    private readonly HttpClient _httpClient;
    private readonly string _directory;
    private readonly FeedCacheOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FeedCache(HttpClient httpClient, string directory, FeedCacheOptions? options, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _httpClient = httpClient;
        _directory = directory;
        _options = options ?? FeedCacheOptions.Default;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns a current local copy of <paramref name="uri"/>, downloading or
    /// revalidating it as needed.
    /// </summary>
    /// <param name="uri">The document URL.</param>
    /// <param name="forceRevalidate">Revalidate even within the revalidation interval.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    public async Task<FeedSnapshot> GetAsync(Uri uri, bool forceRevalidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var key = Key(uri);
            var dataPath = Path.Combine(_directory, key + ".xml");
            var metaPath = Path.Combine(_directory, key + ".json");
            var cached = File.Exists(dataPath) ? ReadMeta(metaPath) : null;
            var now = _time.GetUtcNow();

            if (cached is not null && !forceRevalidate && now - cached.FetchedAt < _options.RevalidationInterval)
                return new FeedSnapshot(dataPath, cached.Version, cached.FetchedAt, null);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (cached?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var parsedTag))
                request.Headers.IfNoneMatch.Add(parsedTag);
            if (cached?.LastModified is { } lastModified)
                request.Headers.IfModifiedSince = lastModified;

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (cached is not null && IsTransient(ex, cancellationToken))
            {
                return new FeedSnapshot(dataPath, cached.Version, cached.FetchedAt, ex.Message);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
                {
                    var confirmed = cached with { FetchedAt = now };
                    WriteMeta(metaPath, confirmed);
                    return new FeedSnapshot(dataPath, confirmed.Version, now, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var reason = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
                    if (cached is not null)
                        return new FeedSnapshot(dataPath, cached.Version, cached.FetchedAt, reason);
                    throw new HttpRequestException($"GET {uri} failed: {reason}.", null, response.StatusCode);
                }

                var partialPath = dataPath + ".partial";
                string hash;
                try
                {
                    await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            sha.AppendData(buffer, 0, read);
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        }

                        hash = Convert.ToHexString(sha.GetHashAndReset());
                    }
                }
                catch (Exception ex) when (cached is not null && IsTransient(ex, cancellationToken))
                {
                    TryDelete(partialPath);
                    return new FeedSnapshot(dataPath, cached.Version, cached.FetchedAt, ex.Message);
                }
                catch
                {
                    TryDelete(partialPath);
                    throw;
                }

                File.Move(partialPath, dataPath, overwrite: true);

                var meta = new FeedMeta(
                    uri.AbsoluteUri,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified,
                    now,
                    "sha256:" + hash);
                WriteMeta(metaPath, meta);
                return new FeedSnapshot(dataPath, meta.Version, now, null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or IOException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string Key(Uri uri) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..16].ToLowerInvariant();

    private static FeedMeta? ReadMeta(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<FeedMeta>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static void WriteMeta(string path, FeedMeta meta)
    {
        var temp = path + ".partial";
        File.WriteAllText(temp, JsonSerializer.Serialize(meta));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The cache record kept beside each cached document.</summary>
    private sealed record FeedMeta(
        string Uri,
        string? ETag,
        DateTimeOffset? LastModified,
        DateTimeOffset FetchedAt,
        string ContentHash)
    {
        public string Version =>
            ETag ?? LastModified?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? ContentHash;
    }
}

/// <summary>Options for caching online catalogues.</summary>
public sealed record FeedCacheOptions
{
    /// <summary>The default options.</summary>
    public static FeedCacheOptions Default { get; } = new();

    /// <summary>
    /// How long a cached catalogue is used without asking the server whether
    /// it has changed. NOAA regenerates its ENC catalogue daily. Defaults to
    /// 15 minutes.
    /// </summary>
    public TimeSpan RevalidationInterval { get; init; } = TimeSpan.FromMinutes(15);
}
