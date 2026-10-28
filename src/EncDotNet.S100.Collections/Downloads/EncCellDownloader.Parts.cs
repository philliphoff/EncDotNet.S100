using System.Net;
using EncDotNet.S100.Collections.Net;
using EncDotNet.S100.Collections.Weather;

namespace EncDotNet.S100.Collections.Downloads;

/// <summary>Downloads assembled from several requests, and kept copies (#1075).</summary>
public sealed partial class EncCellDownloader
{
    /// <summary>
    /// The <c>User-Agent</c> sent with downloads whose client sets none, so a
    /// service that asks clients to identify themselves (the NWS API) can;
    /// <see cref="LibraryHttp.DefaultUserAgent"/> by default.
    /// </summary>
    public string UserAgent { get; init; } = LibraryHttp.DefaultUserAgent;

    /// <summary>
    /// Copies the downloaded copy of <paramref name="cellName"/> to
    /// <paramref name="copyName"/> beside it (a pinned weather run, #1075), so
    /// a later download of <paramref name="cellName"/> doesn't replace it. An
    /// existing copy of that name is replaced.
    /// </summary>
    /// <returns>True when copied; false when <paramref name="cellName"/> has no downloaded copy.</returns>
    /// <exception cref="ArgumentException">A name is not a single, safe folder name.</exception>
    public bool Copy(string cellName, string copyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(cellName);
        ArgumentException.ThrowIfNullOrEmpty(copyName);
        if (!IsSafeName(cellName) || !IsSafeName(copyName))
            throw new ArgumentException($"'{cellName}' or '{copyName}' is not a safe download name.");

        var folder = CellFolder(cellName);
        if (!File.Exists(Path.Combine(folder, RecordFileName)))
            return false;

        var staging = Path.Combine(Root, $".{copyName}.{Guid.NewGuid():N}.staging");
        try
        {
            CopyDirectory(folder, staging);
            Replace(CellFolder(copyName), staging);
            return true;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private void Identify(HttpClient http, HttpRequestMessage request, RemoteItemLocation remote)
    {
        if (http.DefaultRequestHeaders.UserAgent.Count == 0 && request.Headers.UserAgent.Count == 0)
            request.Headers.UserAgent.TryParseAdd(UserAgent);
        if (remote.Accept is { Length: > 0 } accept)
            request.Headers.Accept.TryParseAdd(accept);
    }

    /// <summary>
    /// Appends each part's response to <paramref name="path"/>: a whole
    /// response, or, for a part naming GRIB2 fields, the byte ranges its
    /// <c>.idx</c> inventory gives for them. A part whose fields aren't in its
    /// inventory adds nothing; a download that adds nothing at all fails.
    /// </summary>
    private async Task FetchPartsAsync(
        RemoteItemLocation remote, IReadOnlyList<RemotePart> parts, string path, IProgress<long>? bytesProgress, CancellationToken cancellationToken)
    {
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        long total = 0;
        var missing = new List<string>();
        foreach (var part in parts)
        {
            if (part.GribFields is not { } fields)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, part.Uri);
                Identify(_httpClient, request, remote);
                total = await CopyResponseAsync(request, target, total, null, bytesProgress, cancellationToken).ConfigureAwait(false);
                continue;
            }

            GribIndex index;
            using (var request = new HttpRequestMessage(HttpMethod.Get, WeatherRequests.IndexUriOf(part.Uri)))
            {
                Identify(_httpClient, request, remote);
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    index = GribIndex.Parse(text);
                }
                catch (FormatException ex)
                {
                    throw new InvalidDataException($"The inventory of {Path.GetFileName(part.Uri.AbsolutePath)} is unreadable: {ex.Message}", ex);
                }
            }

            var ranges = index.RangesFor([.. fields]);
            if (ranges.Count == 0)
                missing.Add(Path.GetFileName(part.Uri.AbsolutePath));
            foreach (var range in ranges)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, part.Uri);
                Identify(_httpClient, request, remote);
                request.Headers.TryAddWithoutValidation("Range", range.HeaderValue);
                total = await CopyResponseAsync(request, target, total, range, bytesProgress, cancellationToken).ConfigureAwait(false);
            }
        }

        if (total == 0)
        {
            throw new InvalidDataException(missing.Count > 0
                ? $"None of the chosen fields are in {string.Join(", ", missing.Take(3))}."
                : "The download is empty.");
        }
    }

    /// <summary>
    /// Appends a response to <paramref name="target"/>; for a byte range, only
    /// that range, even from a server that ignored the <c>Range</c> header and
    /// sent the whole file.
    /// </summary>
    private async Task<long> CopyResponseAsync(
        HttpRequestMessage request, Stream target, long total, GribByteRange? range, IProgress<long>? bytesProgress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long skip = range is { } whole && response.StatusCode != HttpStatusCode.PartialContent ? whole.First : 0;
        long? take = range is { Last: { } last } bounded ? last - bounded.First + 1 : null;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[81920];
        int read;
        while (take is not 0 && (read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var offset = (int)Math.Min(skip, read);
            skip -= offset;
            var count = read - offset;
            if (take is { } remaining)
            {
                count = (int)Math.Min(count, remaining);
                take = remaining - count;
            }

            if (count <= 0)
                continue;
            await target.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            total += count;
            bytesProgress?.Report(total);
        }

        return total;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
