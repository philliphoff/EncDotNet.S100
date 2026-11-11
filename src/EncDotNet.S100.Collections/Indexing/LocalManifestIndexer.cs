using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Manifests;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes <see cref="LocalManifestSource"/>s: reads the manifest, then
/// indexes each path of each selected group in place, as a
/// <see cref="LocalSourceIndexer"/> would.
/// </summary>
/// <remarks>
/// <para>
/// Every item is tagged with its group (<see cref="GroupProperty"/> and
/// <see cref="GroupNameProperty"/>), and its key is prefixed with the group id
/// and the manifest-relative path it was found under, so keys stay unique
/// when the same folder is listed by more than one group.
/// </para>
/// <para>
/// Problems never fail the index: an unreadable manifest yields an error
/// diagnostic and no items (with no fingerprint, so the next refresh retries),
/// and a missing path yields a warning.
/// </para>
/// </remarks>
public sealed class LocalManifestIndexer : ICollectionSourceIndexer
{
    /// <summary>The item property holding the id of the manifest group an item belongs to.</summary>
    public const string GroupProperty = "group";

    /// <summary>The item property holding the display name of the manifest group an item belongs to.</summary>
    public const string GroupNameProperty = "groupName";

    private const string FingerprintVersion = "manifest-v1";

    private readonly LocalSourceIndexer _local;

    /// <summary>Creates an indexer that indexes manifest paths with <paramref name="local"/>.</summary>
    public LocalManifestIndexer(LocalSourceIndexer local)
    {
        ArgumentNullException.ThrowIfNull(local);
        _local = local;
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is LocalManifestSource;

    /// <inheritdoc/>
    public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var manifestSource = Cast(source);
        return new ValueTask<string?>(Task.Run(
            () => TryRead(manifestSource.Path, out var manifest, out var contentHash, out _)
                ? Fingerprint(manifestSource, manifest, contentHash, cancellationToken)
                : null,
            cancellationToken));
    }

    /// <inheritdoc/>
    public ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifestSource = Cast(source);
        return new ValueTask<SourceIndex>(Task.Run(
            () => Index(manifestSource, progress, cancellationToken),
            cancellationToken));
    }

    private SourceIndex Index(
        LocalManifestSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
    {
        var items = new List<CollectionItem>();
        var diagnostics = new List<IndexDiagnostic>();

        if (!TryRead(source.Path, out var manifest, out var contentHash, out var error))
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, error, source.Path));
            return new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, items, diagnostics);
        }

        foreach (var unknown in source.Filter.Groups.Where(
            id => !manifest.Groups.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase))))
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning, $"Group '{unknown}' is no longer in the manifest.", source.Path));
        }

        var manifestDirectory = ManifestDirectory(source.Path);
        var groups = new List<SourceIndexGroup>();
        foreach (var group in SelectedGroups(source, manifest))
        {
            var missing = 0;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in group.ResolvePaths(manifestDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    diagnostics.Add(new IndexDiagnostic(
                        IndexDiagnosticSeverity.Warning, $"Group '{group.DisplayName}': path not found.", path));
                    missing++;
                    continue;
                }

                var offset = items.Count;
                var pathProgress = progress is null
                    ? null
                    : new SyncProgress(p => progress.Report(p with { ItemsIndexed = offset + p.ItemsIndexed }));
                var index = _local.IndexPath(source.Id, path, group.Recursive, pathProgress, cancellationToken);
                diagnostics.AddRange(index.Diagnostics);

                var prefix = group.Id + ":" + LocalSourceScanner.RelativePath(manifestDirectory, path) + "/";
                foreach (var item in index.Items)
                {
                    var tagged = Tag(item, group, prefix);
                    if (keys.Add(tagged.Key))
                        items.Add(tagged);
                }
            }

            groups.Add(new SourceIndexGroup(group.Id, group.DisplayName, missing));
        }

        progress?.Report(new IndexProgress(items.Count, null));
        return new SourceIndex(
            source.Id,
            DateTimeOffset.UtcNow,
            Fingerprint(source, manifest, contentHash, cancellationToken),
            items,
            diagnostics)
        {
            Groups = groups,
        };
    }

    private static CollectionItem Tag(CollectionItem item, CollectionManifestGroup group, string prefix)
    {
        var properties = new Dictionary<string, string>(item.Properties, StringComparer.Ordinal)
        {
            [GroupProperty] = group.Id,
            [GroupNameProperty] = group.DisplayName,
        };

        return item with
        {
            Key = prefix + item.Key,
            GroupKey = item.GroupKey is null ? null : prefix + item.GroupKey,
            Properties = properties,
        };
    }

    /// <summary>
    /// Fingerprints the manifest's content, the filter, and the current state
    /// of every path of every selected group.
    /// </summary>
    private static string Fingerprint(
        LocalManifestSource source,
        CollectionManifestDocument manifest,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var manifestDirectory = ManifestDirectory(source.Path);
        var parts = new List<string> { contentHash, source.Filter.ToCanonicalString() };
        foreach (var group in SelectedGroups(source, manifest))
        {
            foreach (var path in group.ResolvePaths(manifestDirectory))
            {
                var scan = LocalSourceScanner.Scan(path, group.Recursive, cancellationToken);
                parts.Add(group.Id + "|" + path + "=" + (scan?.Fingerprint ?? "missing"));
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts)));
        return FingerprintVersion + ":" + Convert.ToHexString(hash);
    }

    private static IEnumerable<CollectionManifestGroup> SelectedGroups(
        LocalManifestSource source, CollectionManifestDocument manifest) =>
        manifest.Groups.Where(g => source.Filter.Matches(g.Id));

    /// <summary>Reads the manifest, hashing its content (sync clients touch timestamps, so content is what counts).</summary>
    private static bool TryRead(
        string path, out CollectionManifestDocument manifest, out string contentHash, out string error)
    {
        manifest = null!;
        contentHash = string.Empty;
        try
        {
            var bytes = File.ReadAllBytes(path);
            contentHash = Convert.ToHexString(SHA256.HashData(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            manifest = CollectionManifest.Read(stream);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex is FileNotFoundException or DirectoryNotFoundException
                ? "Manifest not found."
                : $"Manifest could not be read: {ex.Message}";
            return false;
        }
    }

    private static string ManifestDirectory(string manifestPath) =>
        Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;

    private static LocalManifestSource Cast(CollectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source as LocalManifestSource
            ?? throw new NotSupportedException($"{nameof(LocalManifestIndexer)} cannot index {source.GetType().Name}.");
    }

    /// <summary>Reports synchronously (unlike <see cref="Progress{T}"/>, which posts to a captured context).</summary>
    private sealed class SyncProgress(Action<IndexProgress> report) : IProgress<IndexProgress>
    {
        public void Report(IndexProgress value) => report(value);
    }
}
