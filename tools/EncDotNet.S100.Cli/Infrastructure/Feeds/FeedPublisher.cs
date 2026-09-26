using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Cli.Infrastructure.Feeds;

/// <summary>A published feed at one point in time: the document, its JSON, its ETag and its items by id.</summary>
internal sealed record PublishedFeed(
    S100FeedDocument Document,
    byte[] Json,
    string ETag,
    IReadOnlyDictionary<string, (CollectionItem Item, LocalItemLocation Location)> Items);

/// <summary>
/// Keeps an S-100 feed of a folder, exchange set or dataset up to date
/// (issue #680): the path is indexed in place, and re-checked at most once
/// per refresh interval — cheaply, since an unchanged fingerprint reuses the
/// previous index and feed.
/// </summary>
internal sealed class FeedPublisher
{
    private readonly CollectionSource _source;
    private readonly string _title;
    private readonly CollectionIndexer _indexer;
    private readonly TimeProvider _time;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SourceIndex? _index;
    private PublishedFeed? _feed;
    private DateTimeOffset _checkedAt;

    public FeedPublisher(string path, string? title = null, TimeSpan? refreshInterval = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        _source = Directory.Exists(fullPath) && !LooksLikeExchangeSetRoot(fullPath)
            ? new LocalFolderSource(Guid.NewGuid(), null, fullPath)
            : new ExchangeSetSource(Guid.NewGuid(), null, fullPath);
        _title = string.IsNullOrWhiteSpace(title) ? DefaultTitle(fullPath) : title.Trim();
        _indexer = CollectionIndexer.CreateDefault(TryReadMetadata);
        _time = timeProvider ?? TimeProvider.System;
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(10);
        Path = fullPath;
    }

    /// <summary>The published path.</summary>
    public string Path { get; }

    /// <summary>Warnings and errors from the latest index (unreadable files and the like).</summary>
    public IReadOnlyList<IndexDiagnostic> Diagnostics => _index?.Diagnostics ?? [];

    /// <summary>
    /// Returns the current feed, re-indexing first when the last check is
    /// older than the refresh interval (or <paramref name="force"/>).
    /// </summary>
    public async Task<PublishedFeed> GetAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && _feed is { } current && _time.GetUtcNow() - _checkedAt < _refreshInterval)
            return current;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _feed is { } fresh && _time.GetUtcNow() - _checkedAt < _refreshInterval)
                return fresh;

            var index = await _indexer.IndexAsync(_source, _index, cancellationToken: cancellationToken).ConfigureAwait(false);
            _checkedAt = _time.GetUtcNow();
            if (_feed is not null && ReferenceEquals(index, _index))
                return _feed;

            _index = index;
            _feed = await Task.Run(() => Publish(index), cancellationToken).ConfigureAwait(false);
            return _feed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private PublishedFeed Publish(SourceIndex index)
    {
        var document = S100Feed.FromIndex(index, _title, _time.GetUtcNow());
        using var json = new MemoryStream();
        S100Feed.Write(json, document);
        var bytes = json.ToArray();

        var tag = Convert.ToHexString(SHA256.HashData(
            index.Fingerprint is { } fingerprint ? Encoding.UTF8.GetBytes(fingerprint) : bytes))[..32];

        // The document carries remote locations; keep the local ones to serve.
        var items = index.Items
            .Where(i => i.Location is LocalItemLocation)
            .DistinctBy(i => i.Key, StringComparer.Ordinal)
            .ToDictionary(S100Feed.ItemId, i => (i, (LocalItemLocation)i.Location), StringComparer.Ordinal);
        return new PublishedFeed(document, bytes, $"\"{tag}\"", items);
    }

    /// <summary>Reads a loose dataset's metadata (bounds, display scales) as the viewer does.</summary>
    internal static DatasetMetadata? TryReadMetadata(string path, CancellationToken cancellationToken)
    {
        Func<string, DatasetMetadata>? read = DatasetPipelineFactory.DetectProductSpec(path) switch
        {
            "S-101" => S101Dataset.ReadMetadata,
            "S-57" => S57Dataset.ReadMetadata,
            _ => null,
        };

        try
        {
            return read?.Invoke(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>A folder whose top level holds a catalogue is published as one exchange set.</summary>
    private static bool LooksLikeExchangeSetRoot(string directory) =>
        Directory.EnumerateFiles(directory).Select(System.IO.Path.GetFileName).OfType<string>()
            .Any(f => ExchangeSetLayout.IsS100CatalogueName(f) || ExchangeSetLayout.IsS57CatalogueName(f));

    private static string DefaultTitle(string fullPath)
    {
        var trimmed = fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
