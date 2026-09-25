using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Persistence;

namespace EncDotNet.S100.Collections.Feeds;

/// <summary>
/// An S-100 feed (issue #680): this library's own, simple JSON format for
/// publishing datasets to other machines — served by <c>s100 feed serve</c>
/// or exported as static files by <c>s100 feed export</c>, and read by the
/// viewer's Library as an online source.
/// </summary>
/// <remarks>
/// <para>
/// Items have the shape of the Library index (<see cref="CollectionItem"/>),
/// so a feed carries everything the publisher indexed — coverage polygons,
/// bounds, editions, display scales — and a reader can show coverage before
/// anything is downloaded. Each item's location is a
/// <see cref="RemoteItemLocation"/> whose URL is <em>relative to the feed</em>
/// (<c>items/&lt;id&gt;.zip</c>), whose <see cref="RemoteItemLocation.Package"/>
/// is the item's opaque id, and whose <see cref="RemoteItemLocation.Layout"/>
/// says where the item's files lie within that zip.
/// </para>
/// <para>See <c>docs/s100-feed-format.md</c>.</para>
/// </remarks>
/// <param name="Format">Always <see cref="S100Feed.FormatName"/>.</param>
/// <param name="Version">The format version.</param>
/// <param name="Title">A title for the feed, e.g. the published folder's name.</param>
/// <param name="GeneratedAt">When the feed was generated.</param>
/// <param name="Fingerprint">Changes whenever the published data changes (servers use it as the ETag).</param>
/// <param name="Items">The published datasets.</param>
public sealed record S100FeedDocument(
    string Format,
    int Version,
    string? Title,
    DateTimeOffset GeneratedAt,
    string? Fingerprint,
    IReadOnlyList<CollectionItem> Items);

/// <summary>Builds, writes and reads <see cref="S100FeedDocument"/>s.</summary>
public static class S100Feed
{
    /// <summary>The value of a feed's <c>format</c> property.</summary>
    public const string FormatName = "encdotnet-s100-feed";

    /// <summary>The format version this library writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The feed document's conventional file name.</summary>
    public const string FileName = "feed.json";

    /// <summary>
    /// Builds a feed from a local <paramref name="index"/>: every item with a
    /// <see cref="LocalItemLocation"/> whose base file is present is published
    /// as <c>items/&lt;id&gt;.zip</c>; other items are left out.
    /// </summary>
    /// <param name="index">The index of the published folder, exchange set or dataset.</param>
    /// <param name="title">The feed title.</param>
    /// <param name="generatedAt">When the feed is generated; defaults to now.</param>
    public static S100FeedDocument FromIndex(SourceIndex index, string? title, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(index);

        var items = index.Items
            .Where(i => i.Location is LocalItemLocation local && S100FeedPackager.Exists(local))
            .DistinctBy(i => i.Key, StringComparer.Ordinal)
            .Select(Publish)
            .ToArray();
        return new S100FeedDocument(
            FormatName, CurrentVersion, title, generatedAt ?? DateTimeOffset.UtcNow, index.Fingerprint, items);
    }

    /// <summary>
    /// The opaque id an item is published under: a hash of its key, so the
    /// id never reveals (or can be used to reach) a path on the publisher.
    /// </summary>
    public static string ItemId(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Key)))[..20].ToLowerInvariant();
    }

    /// <summary>The download path of an item with <paramref name="id"/>, relative to the feed.</summary>
    public static string ItemPath(string id) => $"items/{id}.zip";

    /// <summary>Writes <paramref name="feed"/> as JSON, compact unless <paramref name="indented"/>.</summary>
    public static void Write(Stream stream, S100FeedDocument feed, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(feed);
        JsonSerializer.Serialize(stream, feed, indented ? CollectionJson.StoreOptions : CollectionJson.IndexOptions);
    }

    /// <summary>
    /// Reads a feed, resolving its item URLs against <paramref name="feedUri"/>
    /// (where the feed was fetched from).
    /// </summary>
    /// <exception cref="JsonException">The content is not an S-100 feed.</exception>
    /// <exception cref="NotSupportedException">The feed is from a newer, unknown format version.</exception>
    public static S100FeedDocument Read(Stream stream, Uri? feedUri = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var feed = JsonSerializer.Deserialize<S100FeedDocument>(stream, CollectionJson.IndexOptions)
            ?? throw new JsonException("The feed is empty.");
        if (feed.Format != FormatName)
            throw new JsonException($"Expected an '{FormatName}' document, found '{feed.Format}'.");
        if (feed.Version > CurrentVersion)
            throw new NotSupportedException($"Feed version {feed.Version} is newer than supported version {CurrentVersion}.");

        var items = (feed.Items ?? []).Select(i => Resolve(i, feedUri)).ToArray();
        return feed with { Items = items };
    }

    private static CollectionItem Publish(CollectionItem item)
    {
        var local = (LocalItemLocation)item.Location;
        var id = ItemId(item);
        return item with
        {
            Location = new RemoteItemLocation(
                new Uri(ItemPath(id), UriKind.Relative),
                S100FeedPackager.EstimateSize(local),
                S100FeedPackager.LastModified(local),
                Package: id,
                Layout: new PackageLayout(local.RelativePath, local.UpdateRelativePaths, local.CatalogueRelativePath)),
        };
    }

    private static CollectionItem Resolve(CollectionItem item, Uri? feedUri) =>
        item.Location is RemoteItemLocation { Uri.IsAbsoluteUri: false } remote && feedUri is not null
            ? item with { Location = remote with { Uri = new Uri(feedUri, remote.Uri) } }
            : item;
}
