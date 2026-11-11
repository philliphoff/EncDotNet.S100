using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Core.Caching;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Rendered tiles kept on disk across <c>tiles serve</c> runs (issue #865), up
/// to a byte budget, evicting the least recently used first. Thread-safe, and
/// safe for several servers to share one folder.
/// </summary>
/// <remarks>
/// <para>
/// The tiles are the <see cref="KindName"/> kind of a <see cref="ContentStore"/>
/// rooted at the cache folder (map rendering slice 11), stored as
/// <c>&lt;root&gt;/served/&lt;fingerprint&gt;/&lt;palette&gt;/&lt;z&gt;/&lt;x&gt;/&lt;y&gt;.&lt;ext&gt;</c>
/// (<c>&lt;palette&gt;-&lt;yyyyMMddTHHmmssZ&gt;</c> for a time step asked for by
/// URL), where the fingerprint (<see cref="TileRenderSession.Fingerprint"/>)
/// names the data and settings they were rendered from, so a tile is only
/// reused for the same ones. A tile with nothing on it is an empty file.
/// </para>
/// <para>
/// The store only reads, counts, evicts or clears its own kind folder, so the
/// cache folder may hold other files. The budget covers every fingerprint, so
/// tiles of data no longer served age out. Tiles are written by background
/// jobs, through a temporary file renamed into place, so a reader never sees
/// half a tile; until a write lands, <see cref="TryGet"/> sees the queued tile.
/// </para>
/// </remarks>
internal sealed partial class DiskTileCache : IDisposable
{
    /// <summary>The store kind name, which is also its folder name under the cache folder.</summary>
    public const string KindName = "served";

    // Files take whole blocks on disk; count each tile at least this much, so a
    // flood of empty tiles is bounded too.
    private const long EntryOverhead = 512;

    private readonly ContentStore _store;
    private readonly ContentKind _kind;
    private readonly string _fingerprint;
    private readonly string _extension;

    /// <param name="root">The cache folder; created when missing.</param>
    /// <param name="fingerprint">The fingerprint of the data and settings tiles are rendered from.</param>
    /// <param name="format">The tile encoding, for the file extension.</param>
    /// <param name="capacityBytes">The most bytes the cache folder holds.</param>
    public DiskTileCache(string root, string fingerprint, TileImageFormat format, long capacityBytes)
    {
        if (!IsFingerprint(fingerprint))
            throw new ArgumentException("A fingerprint is 32 lowercase hexadecimal characters.", nameof(fingerprint));

        _store = new ContentStore(root);
        _kind = _store.Open(
            KindName,
            Math.Max(1, capacityBytes),
            new ContentKindOptions { Extension = "", NestedKeys = true, EntryOverheadBytes = EntryOverhead });
        _fingerprint = fingerprint;
        _extension = "." + TileSetMetadata.FormatToken(format);
        Directory.CreateDirectory(Folder);
        _kind.EnsureIndexed();
    }

    /// <summary>The folder this cache's tiles go in.</summary>
    public string Folder => Path.Combine(_kind.Directory, _fingerprint);

    /// <summary>The bytes counted against the budget, across every fingerprint folder.</summary>
    public long Size => _kind.TotalBytes;

    /// <summary>Deletes the cached tiles under <paramref name="root"/>, and nothing else.</summary>
    public static void Clear(string root)
    {
        var folder = Path.Combine(root, KindName);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Whether <paramref name="name"/> is a fingerprint folder name.</summary>
    public static bool IsFingerprint(string name) => FingerprintPattern().IsMatch(name);

    /// <summary>The fingerprint folders under the cache folder <paramref name="root"/>.</summary>
    public static IEnumerable<string> FingerprintFolders(string root)
    {
        var folder = Path.Combine(root, KindName);
        return Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Where(d => IsFingerprint(Path.GetFileName(d)))
            : [];
    }

    public bool TryGet(TileKey key, out byte[] data)
    {
        var entry = _kind.Read(KeyFor(key));
        data = entry.Bytes ?? [];
        return entry.IsHit;
    }

    public void Add(TileKey key, byte[] data)
    {
        if (data.Length + EntryOverhead > _kind.ShareBytes)
            return;

        // Writing on a request's thread only if the background queue is full.
        var entryKey = KeyFor(key);
        if (_kind.QueueWrite(entryKey, data) is ContentWriteResult.Full or ContentWriteResult.Stopped)
            _kind.Write(entryKey, data);
    }

    /// <summary>Waits until every queued write has reached the disk.</summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <returns><see langword="true"/> when the queue drained within <paramref name="timeout"/>.</returns>
    public bool WaitForWrites(TimeSpan timeout) => _kind.WaitForWritesIdle(timeout);

    /// <summary>Waits briefly for queued writes, so they reach the disk.</summary>
    public void Dispose() => _store.Dispose();

    private string KeyFor(TileKey key) => string.Join('/',
        _fingerprint,
        key.Time is { } time
            ? key.Palette + "-" + TileRenderSession.AsUtc(time).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
            : key.Palette,
        key.Zoom.ToString(CultureInfo.InvariantCulture),
        key.X.ToString(CultureInfo.InvariantCulture),
        key.Y.ToString(CultureInfo.InvariantCulture) + _extension);

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex FingerprintPattern();
}
