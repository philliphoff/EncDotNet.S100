using System.IO.Compression;
using System.Text.Json;

namespace EncDotNet.S100.Collections.Noaa;

/// <summary>
/// A NOAA ENC cell downloaded into a managed folder: what was downloaded and
/// where its exchange set now lies.
/// </summary>
/// <param name="Name">The cell name.</param>
/// <param name="Edition">The edition downloaded.</param>
/// <param name="Update">The update number downloaded.</param>
/// <param name="PublishedAt">When NOAA published the zip, if known.</param>
/// <param name="DownloadedAt">When the download completed.</param>
/// <param name="Location">The downloaded exchange set, ready to load.</param>
public sealed record DownloadedCell(
    string Name,
    int? Edition,
    int? Update,
    DateTimeOffset? PublishedAt,
    DateTimeOffset DownloadedAt,
    LocalItemLocation Location)
{
    /// <summary>
    /// True when <paramref name="item"/> (a feed item for the same cell)
    /// describes a newer edition or update than this download.
    /// </summary>
    public bool IsOlderThan(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.Edition ?? 0, item.Update ?? 0).CompareTo((Edition ?? 0, Update ?? 0)) > 0;
    }
}

/// <summary>
/// Downloads NOAA ENC cells (issue #655): each cell's zipped exchange set is
/// fetched from its <see cref="RemoteItemLocation"/> and extracted to
/// <c>&lt;root&gt;/&lt;CELL&gt;/</c>, with a <c>.source.json</c> record of the
/// edition and layout.
/// </summary>
/// <remarks>
/// <para>
/// Downloads are atomic from the reader's point of view: the zip streams to a
/// <c>.partial</c> file, is extracted into a staging folder, and only then
/// replaces any previous copy of the cell — an interrupted or failed download
/// never leaves a half-written cell behind or destroys a good one.
/// </para>
/// <para>
/// A NOAA cell zip holds <c>ENC_ROOT/CATALOG.031</c> and
/// <c>ENC_ROOT/&lt;CELL&gt;/&lt;CELL&gt;.000</c> plus its sequential updates; the
/// layout is discovered rather than assumed, and recorded relative to the
/// cell folder so the managed folder can move.
/// </para>
/// </remarks>
public sealed class NoaaEncCellDownloader
{
    /// <summary>The name of the per-cell record file.</summary>
    public const string RecordFileName = ".source.json";

    private static readonly JsonSerializerOptions RecordOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _time;

    /// <summary>Creates a downloader writing under <paramref name="root"/>.</summary>
    public NoaaEncCellDownloader(HttpClient httpClient, string root, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(root);
        _httpClient = httpClient;
        Root = Path.GetFullPath(root);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The managed folder cells are downloaded into.</summary>
    public string Root { get; }

    /// <summary>
    /// Returns the downloaded copy of <paramref name="cellName"/>, or
    /// <see langword="null"/> when it has not been downloaded (or its record
    /// is unreadable or its files are gone).
    /// </summary>
    public DownloadedCell? TryGetDownloaded(string cellName)
    {
        ArgumentException.ThrowIfNullOrEmpty(cellName);

        var cellFolder = CellFolder(cellName);
        var recordPath = Path.Combine(cellFolder, RecordFileName);
        try
        {
            if (!File.Exists(recordPath))
                return null;

            var record = JsonSerializer.Deserialize<CellRecord>(File.ReadAllText(recordPath), RecordOptions);
            if (record is null)
                return null;

            var location = new LocalItemLocation(
                Path.GetFullPath(Path.Combine(cellFolder, record.RootRelativePath)),
                record.BaseRelativePath,
                record.UpdateRelativePaths,
                record.CatalogueRelativePath);
            if (!File.Exists(Path.Combine(location.RootPath, location.RelativePath)))
                return null;

            return new DownloadedCell(
                record.Name, record.Edition, record.Update, record.PublishedAt, record.DownloadedAt, location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the cell described by <paramref name="item"/>, replacing any
    /// previous copy.
    /// </summary>
    /// <param name="item">A NOAA feed item with a <see cref="RemoteItemLocation"/>.</param>
    /// <param name="bytesProgress">Receives the number of bytes received so far.</param>
    /// <param name="cancellationToken">Cancels the download; nothing is left behind.</param>
    /// <exception cref="ArgumentException">The item has no remote location.</exception>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="InvalidDataException">The zip holds no <c>.000</c> base cell for the item.</exception>
    public async Task<DownloadedCell> DownloadAsync(
        CollectionItem item,
        IProgress<long>? bytesProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Location is not RemoteItemLocation remote)
            throw new ArgumentException($"{item.Name} has no download location.", nameof(item));

        Directory.CreateDirectory(Root);
        var token = Guid.NewGuid().ToString("N");
        var zipPath = Path.Combine(Root, $".{item.Name}.{token}.zip.partial");
        var staging = Path.Combine(Root, $".{item.Name}.{token}.staging");

        try
        {
            using (var response = await _httpClient
                .GetAsync(remote.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    bytesProgress?.Report(total);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            ZipFile.ExtractToDirectory(zipPath, staging);

            var record = Describe(item, remote, staging);
            File.WriteAllText(Path.Combine(staging, RecordFileName), JsonSerializer.Serialize(record, RecordOptions));

            Replace(CellFolder(item.Name), staging);
            return TryGetDownloaded(item.Name)
                ?? throw new InvalidDataException($"Downloaded {item.Name} could not be read back.");
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(staging);
        }
    }

    private string CellFolder(string cellName) => Path.Combine(Root, cellName);

    /// <summary>Finds the catalogue, base cell and updates in an extracted zip.</summary>
    private CellRecord Describe(CollectionItem item, RemoteItemLocation remote, string staging)
    {
        var baseCell = Directory
            .EnumerateFiles(staging, item.Name + ".000", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            })
            .FirstOrDefault()
            ?? throw new InvalidDataException($"The download for {item.Name} contains no {item.Name}.000.");

        // Prefer the folder holding CATALOG.031 as the root (the exchange set);
        // fall back to the base cell's own folder.
        var catalogue = Directory
            .EnumerateFiles(staging, ExchangeSetLayout.S57CatalogueName, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            })
            .FirstOrDefault(c => Path.GetFullPath(baseCell).StartsWith(
                Path.GetDirectoryName(Path.GetFullPath(c))! + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        var root = catalogue is not null ? Path.GetDirectoryName(catalogue)! : Path.GetDirectoryName(baseCell)!;

        var stem = Path.GetFileNameWithoutExtension(baseCell);
        var updates = Directory.EnumerateFiles(Path.GetDirectoryName(baseCell)!)
            .Select(f => (Path: f, Number: UpdateNumber(f, stem)))
            .Where(u => u.Number > 0)
            .OrderBy(u => u.Number)
            .Select(u => Relative(root, u.Path))
            .ToArray();

        return new CellRecord(
            item.Name,
            item.Edition,
            item.Update,
            remote.LastModified,
            _time.GetUtcNow(),
            remote.Uri.AbsoluteUri,
            Relative(staging, root),
            Relative(root, baseCell),
            updates,
            catalogue is null ? null : Path.GetFileName(catalogue));
    }

    /// <summary>
    /// Moves <paramref name="staging"/> to <paramref name="target"/>, replacing
    /// any existing copy only once the new one is in place.
    /// </summary>
    private static void Replace(string target, string staging)
    {
        string? retired = null;
        if (Directory.Exists(target))
        {
            retired = target + ".old-" + Guid.NewGuid().ToString("N");
            Directory.Move(target, retired);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (retired is not null)
                Directory.Move(retired, target);
            throw;
        }

        if (retired is not null)
            TryDeleteDirectory(retired);
    }

    private static int UpdateNumber(string path, string stem)
    {
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), stem, StringComparison.OrdinalIgnoreCase))
            return -1;
        var ext = Path.GetExtension(path);
        return ext.Length == 4 && int.TryParse(ext.AsSpan(1), out var n) ? n : -1;
    }

    private static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative == "." ? string.Empty : relative;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The <c>.source.json</c> record; paths are relative to the cell folder / exchange-set root.</summary>
    private sealed record CellRecord(
        string Name,
        int? Edition,
        int? Update,
        DateTimeOffset? PublishedAt,
        DateTimeOffset DownloadedAt,
        string Source,
        string RootRelativePath,
        string BaseRelativePath,
        IReadOnlyList<string> UpdateRelativePaths,
        string? CatalogueRelativePath);
}
