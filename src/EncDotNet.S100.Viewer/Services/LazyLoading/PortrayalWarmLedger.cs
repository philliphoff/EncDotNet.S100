using System.Text.Json;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// What the portrayal-cache warmer has already portrayed, kept across restarts
/// so it does not re-open every cell of a collection to find out (#1266).
/// </summary>
/// <remarks>
/// <para>
/// Each record maps a cell (its file, size, modification time and updates) to
/// the presentation signature it was portrayed under and the cache keys
/// (portrayal and pattern clip) that render used. A record counts only while
/// its signature is the current one and every key is still cached, so a
/// settings change, a rebuilt viewer or an evicted entry sends the cell back
/// to the queue.
/// </para>
/// <para>
/// The ledger lives beside the cache entries it describes, so clearing the
/// caches clears it too. All members are thread-safe. Saving never throws: a
/// ledger that cannot be written only costs a re-check after a restart.
/// </para>
/// </remarks>
internal sealed class PortrayalWarmLedger
{
    private const int FormatVersion = 1;

    private readonly string? _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, Record> _records = new(StringComparer.Ordinal);
    private bool _dirty;

    /// <summary>
    /// Creates a ledger persisted at <paramref name="path"/>, loading what is
    /// there. A missing, unreadable or older-format file starts empty.
    /// </summary>
    /// <param name="path">
    /// The ledger file, or <see langword="null"/> to keep it in memory only
    /// (a read-only profile).
    /// </param>
    public PortrayalWarmLedger(string? path)
    {
        _path = path;
        Load();
    }

    /// <summary>The number of cells recorded.</summary>
    public int Count
    {
        get { lock (_gate) return _records.Count; }
    }

    /// <summary>
    /// Identifies a cell's content for the ledger: the full path, size and
    /// modification time of the base cell and each update. Editing or replacing
    /// a file changes the key, so a record never vouches for different bytes.
    /// </summary>
    /// <param name="entry">The cell.</param>
    /// <returns>
    /// The cell's ledger key, or <see langword="null"/> when its files are not
    /// plain files on disk (a ZIP exchange set): such cells are not recorded,
    /// because a changed archive could not be told apart.
    /// </returns>
    public static string? CellKey(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string root;
        if (entry.Source is EncDotNet.S100.Core.FileSystemAssetSource folder && entry.RelativePath is { } relative)
            root = folder.RootPath;
        else if (entry.Source is null && Path.IsPathFullyQualified(entry.FilePath))
            return Stamp(new System.Text.StringBuilder(), entry.FilePath)?.ToString();
        else
            return null;

        var key = Stamp(new System.Text.StringBuilder(), Path.Combine(root, relative));
        foreach (var update in entry.UpdateRelativePaths)
        {
            if (key is null)
                return null;
            key = Stamp(key.Append('|'), Path.Combine(root, update));
        }

        return key?.ToString();
    }

    /// <summary>
    /// Whether <paramref name="cellKey"/> was portrayed under
    /// <paramref name="signature"/> and every cache key it wrote is still
    /// cached.
    /// </summary>
    /// <param name="cellKey">The cell, from <see cref="CellKey"/>.</param>
    /// <param name="signature">The current presentation signature.</param>
    /// <param name="isCached">Whether a recorded cache key still has an entry.</param>
    /// <returns><see langword="true"/> when the cell needs no portrayal.</returns>
    public bool IsWarm(string cellKey, string signature, Func<string, bool> isCached)
    {
        ArgumentNullException.ThrowIfNull(cellKey);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(isCached);

        Record? record;
        lock (_gate)
        {
            if (!_records.TryGetValue(cellKey, out record))
                return false;
        }

        return string.Equals(record.Signature, signature, StringComparison.Ordinal)
            && record.Keys.Length > 0
            && record.Keys.All(isCached);
    }

    /// <summary>
    /// Records that <paramref name="cellKey"/> was portrayed under
    /// <paramref name="signature"/>, writing <paramref name="cacheKeys"/>.
    /// </summary>
    /// <param name="cellKey">The cell, from <see cref="CellKey"/>.</param>
    /// <param name="signature">The presentation signature it was portrayed under.</param>
    /// <param name="cacheKeys">The cache keys the render used.</param>
    public void MarkWarm(string cellKey, string signature, IReadOnlyList<string> cacheKeys)
    {
        ArgumentNullException.ThrowIfNull(cellKey);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(cacheKeys);

        lock (_gate)
        {
            _records[cellKey] = new Record(signature, cacheKeys.ToArray());
            _dirty = true;
        }
    }

    /// <summary>
    /// Writes the ledger if it changed since the last save. Atomic (temp file
    /// then rename); never throws.
    /// </summary>
    public void Save()
    {
        if (_path is null)
            return;

        byte[] bytes;
        lock (_gate)
        {
            if (!_dirty)
                return;
            bytes = JsonSerializer.SerializeToUtf8Bytes(
                new LedgerFile(FormatVersion, new Dictionary<string, Record>(_records, StringComparer.Ordinal)),
                PortrayalWarmLedgerJsonContext.Default.LedgerFile);
            _dirty = false;
        }

        var temp = _path + "." + Path.GetRandomFileName() + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            lock (_gate)
                _dirty = true;
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
            return;

        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllBytes(_path), PortrayalWarmLedgerJsonContext.Default.LedgerFile);
            if (file is null || file.Version != FormatVersion || file.Cells is null)
                return;
            foreach (var (key, record) in file.Cells)
            {
                if (record?.Signature is not null && record.Keys is not null)
                    _records[key] = record;
            }
        }
        catch
        {
            // A corrupt ledger only means the cells are checked again.
        }
    }

    private static System.Text.StringBuilder? Stamp(System.Text.StringBuilder key, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return null;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return key.Append(info.FullName)
                .Append('@').Append(info.Length.ToString(c))
                .Append(':').Append(info.LastWriteTimeUtc.Ticks.ToString(c));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>One cell's record.</summary>
    /// <param name="Signature">The presentation signature it was portrayed under.</param>
    /// <param name="Keys">The cache keys the render used.</param>
    internal sealed record Record(string Signature, string[] Keys);

    /// <summary>The ledger file's shape.</summary>
    /// <param name="Version">The file format version.</param>
    /// <param name="Cells">The records by cell key.</param>
    internal sealed record LedgerFile(int Version, Dictionary<string, Record>? Cells);
}

/// <summary>Trim-safe JSON for <see cref="PortrayalWarmLedger"/>.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(PortrayalWarmLedger.LedgerFile))]
internal sealed partial class PortrayalWarmLedgerJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
