using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Rendered tiles kept on disk across <c>tiles serve</c> runs (issue #865), up
/// to a byte budget, evicting the least recently used first. Thread-safe, and
/// safe for several servers to share one folder.
/// </summary>
/// <remarks>
/// <para>
/// Tiles are stored as <c>&lt;root&gt;/&lt;fingerprint&gt;/&lt;palette&gt;/&lt;z&gt;/&lt;x&gt;/&lt;y&gt;.&lt;ext&gt;</c>
/// (<c>&lt;palette&gt;-&lt;yyyyMMddTHHmmssZ&gt;</c> for a time step asked for by URL),
/// where the fingerprint (<see cref="TileRenderSession.Fingerprint"/>) names
/// the data and settings they were rendered from, so a tile is only reused for
/// the same ones. A tile with nothing on it is an empty file.
/// </para>
/// <para>
/// Only fingerprint folders (32 hexadecimal characters) are read, counted,
/// evicted or cleared, so pointing the cache at a folder that holds other files
/// leaves them alone. The budget covers every fingerprint folder, so tiles of
/// data no longer served age out. Reading a tile refreshes its write time, so
/// the eviction order survives a restart. Tiles are written to a temporary
/// file and renamed into place, so a reader never sees half a tile.
/// </para>
/// </remarks>
internal sealed partial class DiskTileCache
{
    // Files take whole blocks on disk; count each tile at least this much, so a
    // flood of empty tiles is bounded too.
    private const long EntryOverhead = 512;

    private readonly string _root;
    private readonly string _folder;
    private readonly string _extension;
    private readonly long _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Path, long Cost)>> _entries = new(StringComparer.Ordinal);

    // Least recently used first.
    private readonly LinkedList<(string Path, long Cost)> _recency = new();
    private long _size;

    /// <param name="root">The cache folder; created when missing.</param>
    /// <param name="fingerprint">The fingerprint of the data and settings tiles are rendered from.</param>
    /// <param name="format">The tile encoding, for the file extension.</param>
    /// <param name="capacityBytes">The most bytes the cache folder holds.</param>
    public DiskTileCache(string root, string fingerprint, TileImageFormat format, long capacityBytes)
    {
        if (!IsFingerprint(fingerprint))
            throw new ArgumentException("A fingerprint is 32 lowercase hexadecimal characters.", nameof(fingerprint));

        _root = Path.GetFullPath(root);
        _folder = Path.Combine(_root, fingerprint);
        _extension = "." + TileSetMetadata.FormatToken(format);
        _capacity = Math.Max(0, capacityBytes);
        Directory.CreateDirectory(_folder);

        // Index what earlier runs (of any fingerprint) left, oldest first.
        foreach (var file in CachedFiles(_root).OrderBy(f => f.LastWriteTimeUtc))
            Track(file.FullName, Cost(file.Length));
        Evict();
    }

    /// <summary>The folder this cache's tiles go in.</summary>
    public string Folder => _folder;

    /// <summary>The bytes counted against the budget, across every fingerprint folder.</summary>
    public long Size
    {
        get
        {
            lock (_gate)
                return _size;
        }
    }

    /// <summary>Deletes every fingerprint folder under <paramref name="root"/>, and nothing else.</summary>
    public static void Clear(string root)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var folder in Directory.EnumerateDirectories(root).Where(d => IsFingerprint(Path.GetFileName(d))))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Whether <paramref name="name"/> is a fingerprint folder name.</summary>
    public static bool IsFingerprint(string name) => FingerprintPattern().IsMatch(name);

    public bool TryGet(TileKey key, out byte[] data)
    {
        var path = PathFor(key);
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            data = [];
            return false;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var node))
            {
                _recency.Remove(node);
                _recency.AddLast(node);
            }
            else
            {
                // Written by another server sharing the folder.
                Track(path, Cost(data.Length));
            }
        }

        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only the order of the next run's index suffers.
        }

        return true;
    }

    public void Add(TileKey key, byte[] data)
    {
        long cost = Cost(data.Length);
        if (cost > _capacity)
            return;

        var path = PathFor(key);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A full or read-only disk only loses the cached copy.
            TryDelete(temporary);
            return;
        }

        lock (_gate)
        {
            Track(path, cost);
            Evict();
        }
    }

    private string PathFor(TileKey key) => Path.Combine(
        _folder,
        key.Time is { } time
            ? key.Palette + "-" + TileRenderSession.AsUtc(time).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
            : key.Palette,
        key.Zoom.ToString(CultureInfo.InvariantCulture),
        key.X.ToString(CultureInfo.InvariantCulture),
        key.Y.ToString(CultureInfo.InvariantCulture) + _extension);

    /// <summary>Records <paramref name="path"/> as the most recently used. Call under the lock (or from the constructor).</summary>
    private void Track(string path, long cost)
    {
        if (_entries.Remove(path, out var existing))
        {
            _recency.Remove(existing);
            _size -= existing.Value.Cost;
        }

        _entries[path] = _recency.AddLast((path, cost));
        _size += cost;
    }

    /// <summary>Deletes the least recently used tiles until the cache fits its budget. Call under the lock (or from the constructor).</summary>
    private void Evict()
    {
        while (_size > _capacity && _recency.First is { } oldest)
        {
            _recency.RemoveFirst();
            _entries.Remove(oldest.Value.Path);
            _size -= oldest.Value.Cost;
            TryDelete(oldest.Value.Path);
        }
    }

    private static long Cost(long length) => length + EntryOverhead;

    /// <summary>The tile files under the fingerprint folders of <paramref name="root"/>.</summary>
    private static IEnumerable<FileInfo> CachedFiles(string root) =>
        new DirectoryInfo(root).EnumerateDirectories()
            .Where(d => IsFingerprint(d.Name))
            .SelectMany(d => d.EnumerateFiles("*", SearchOption.AllDirectories))
            .Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex FingerprintPattern();
}
