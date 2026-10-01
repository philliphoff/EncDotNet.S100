using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Collections.RemoteCatalogues;

/// <summary>One object in an S3 bucket listing.</summary>
/// <param name="Uri">The object's URL.</param>
/// <param name="SizeBytes">The object's size.</param>
/// <param name="LastModified">When the object was last written.</param>
public sealed record S3Object(Uri Uri, long SizeBytes, DateTimeOffset? LastModified);

/// <summary>
/// Lists objects under a prefix of a public (anonymous) Amazon S3 bucket
/// with <c>ListObjectsV2</c>, for the sizes and dates a remote exchange
/// catalogue does not carry. Works for virtual-hosted URLs
/// (<c>https://bucket.s3.amazonaws.com/key</c>, optionally with a region)
/// and path-style ones (<c>https://s3.amazonaws.com/bucket/key</c>).
/// </summary>
public static class S3ObjectListing
{
    private static readonly XNamespace S3 = "http://s3.amazonaws.com/doc/2006-03-01/";

    /// <summary>
    /// Returns true when <paramref name="uri"/> addresses an object (or
    /// prefix) in an S3 bucket, with the bucket's base URL (ending in
    /// <c>/</c>) and the key.
    /// </summary>
    public static bool TryParse(Uri uri, out Uri bucketUri, out string key)
    {
        ArgumentNullException.ThrowIfNull(uri);
        bucketUri = uri;
        key = string.Empty;
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
            return false;

        var host = uri.Host;
        if (!host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var labels = host.Split('.');
        var s3Label = Array.FindIndex(labels, l => l.Equals("s3", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("s3-", StringComparison.OrdinalIgnoreCase));
        if (s3Label < 0)
            return false;

        if (s3Label == 0)
        {
            // Path style: the first path segment is the bucket.
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
                return false;
            bucketUri = new Uri($"{uri.Scheme}://{uri.Authority}/{path[..slash]}/");
            key = path[(slash + 1)..];
            return true;
        }

        bucketUri = new Uri($"{uri.Scheme}://{uri.Authority}/");
        key = path;
        return true;
    }

    /// <summary>
    /// Lists every object whose URL lies under <paramref name="prefixUri"/>
    /// (a "folder" URL ending in <c>/</c>, or any key prefix), following
    /// continuation tokens.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="prefixUri"/> is not an S3 URL.</exception>
    /// <exception cref="HttpRequestException">The bucket could not be listed.</exception>
    public static async Task<IReadOnlyList<S3Object>> ListAsync(
        HttpClient httpClient, Uri prefixUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(prefixUri);
        if (!TryParse(prefixUri, out var bucketUri, out var prefix))
            throw new ArgumentException($"'{prefixUri}' is not an Amazon S3 URL.", nameof(prefixUri));

        var objects = new List<S3Object>();
        string? continuation = null;
        do
        {
            var query = "?list-type=2&prefix=" + Uri.EscapeDataString(prefix)
                + (continuation is null ? string.Empty : "&continuation-token=" + Uri.EscapeDataString(continuation));
            using var response = await httpClient.GetAsync(new Uri(bucketUri, query), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            continuation = ReadPage(document, bucketUri, objects);
        }
        while (continuation is not null);

        return objects;
    }

    /// <summary>Adds one <c>ListBucketResult</c> page's objects; returns the next continuation token, if any.</summary>
    internal static string? ReadPage(XDocument document, Uri bucketUri, List<S3Object> objects)
    {
        var root = document.Root ?? throw new HttpRequestException("The bucket listing is empty.");
        var ns = root.Name.Namespace == XNamespace.None ? XNamespace.None : S3;
        foreach (var content in root.Elements(ns + "Contents"))
        {
            var key = (string?)content.Element(ns + "Key");
            if (string.IsNullOrEmpty(key) || key.EndsWith('/'))
                continue;
            if (!long.TryParse((string?)content.Element(ns + "Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                continue;
            DateTimeOffset? modified = DateTimeOffset.TryParse(
                (string?)content.Element(ns + "LastModified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var m)
                ? m
                : null;
            objects.Add(new S3Object(new Uri(bucketUri, EscapeKey(key)), size, modified));
        }

        return string.Equals((string?)root.Element(ns + "IsTruncated"), "true", StringComparison.OrdinalIgnoreCase)
            ? (string?)root.Element(ns + "NextContinuationToken")
            : null;
    }

    private static string EscapeKey(string key) =>
        string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
}
