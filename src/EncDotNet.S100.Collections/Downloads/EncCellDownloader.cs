using System.IO.Compression;
using System.Text.Json;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Downloads;

/// <summary>
/// An ENC cell — or a package of cells — downloaded into a managed folder:
/// what was downloaded and where its exchange set now lies.
/// </summary>
/// <param name="Name">The cell name, or the package name.</param>
/// <param name="Edition">The edition downloaded (none for a package).</param>
/// <param name="Update">The update number downloaded (none for a package).</param>
/// <param name="PublishedAt">When the provider published the download, if known.</param>
/// <param name="DownloadedAt">When the download completed.</param>
/// <param name="Location">The downloaded cell (for a package, its first cell), ready to load.</param>
public sealed record DownloadedCell(
    string Name,
    int? Edition,
    int? Update,
    DateTimeOffset? PublishedAt,
    DateTimeOffset DownloadedAt,
    LocalItemLocation Location)
{
    /// <summary>True when this is a package: a download holding any number of cells.</summary>
    public bool IsPackage { get; init; }

    /// <summary>
    /// The data signature check made when a SECOM object was downloaded
    /// (issue #804); <see langword="null"/> for other downloads.
    /// </summary>
    public SecomSignatureCheck? Signature { get; init; }

    /// <summary>
    /// The downloaded base cells by name (case-insensitive); for a single
    /// cell, just <see cref="Location"/>.
    /// </summary>
    public IReadOnlyDictionary<string, LocalItemLocation> Datasets { get; init; } =
        new Dictionary<string, LocalItemLocation>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="item"/> (a feed item for the same cell or
    /// package) describes a newer download: a newer edition or update, or —
    /// for a package, or a dataset without editions such as a forecast run
    /// (#685) — a later publication date.
    /// </summary>
    public bool IsOlderThan(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IsPackage || (Edition is null && item.Edition is null))
        {
            return item.Location is RemoteItemLocation { LastModified: { } published }
                && PublishedAt is { } downloaded
                && published > downloaded;
        }

        return (item.Edition ?? 0, item.Update ?? 0).CompareTo((Edition ?? 0, Update ?? 0)) > 0;
    }
}

/// <summary>
/// Downloads ENC cells from online catalogues (NOAA, USACE, …; issue #655):
/// each cell's zip is fetched from its <see cref="RemoteItemLocation"/> and extracted to
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
/// Producers lay cell zips out differently — NOAA uses
/// <c>ENC_ROOT/CATALOG.031</c> plus <c>ENC_ROOT/&lt;CELL&gt;/&lt;CELL&gt;.000</c>,
/// USACE keeps the catalogue in the cell folder or the cell directly in
/// <c>ENC_ROOT</c>, and some zips hold a bare <c>.000</c> — so the layout is
/// discovered rather than assumed, and recorded relative to the cell folder
/// so the managed folder can move.
/// </para>
/// <para>
/// An item whose <see cref="RemoteItemLocation.Package"/> is set is a
/// <em>package</em> (community chart lists, issue #670): it is saved under
/// <c>&lt;root&gt;/&lt;package&gt;/</c> and every <c>.000</c> it holds is
/// recorded. A download that is not a zip is kept as a bare cell file, or as
/// a bare dataset file when its <see cref="RemoteItemLocation.Layout"/> names
/// that file (remote S-100 catalogues, issue #685).
/// </para>
/// <para>
/// An item whose <see cref="RemoteItemLocation.Envelope"/> is
/// <see cref="RemoteEnvelope.Secom"/> downloads a SECOM <c>Get</c> response
/// (issue #804): its base64 data is decoded, its signature checked — a
/// signature that does not match refuses the download — and the data saved
/// as the file its <see cref="RemoteItemLocation.Layout"/> names. Encrypted
/// SECOM data is refused; it needs a certificate.
/// </para>
/// </remarks>
public sealed partial class EncCellDownloader
{
    /// <summary>The name of the per-cell record file.</summary>
    public const string RecordFileName = ".source.json";

    private static readonly RecordJsonContext RecordJson = new(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _time;

    /// <summary>Creates a downloader writing under <paramref name="root"/>.</summary>
    public EncCellDownloader(HttpClient httpClient, string root, TimeProvider? timeProvider = null)
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
    /// The anchors SECOM signers are trusted against (issue #823); by default
    /// <see cref="SecomTrustAnchors.BuiltIn"/>. A downloaded object's signer is
    /// judged against these each time its record is read, so changing them
    /// needs no re-download.
    /// </summary>
    public SecomTrustAnchors TrustAnchors
    {
        get => TrustAnchorsSource();
        init => TrustAnchorsSource = () => value;
    }

    /// <summary>
    /// Where <see cref="TrustAnchors"/> come from, read each time a signer is
    /// judged: a host whose anchors change (a root turned off or added, #845)
    /// passes e.g. <c>() =&gt; serverTrust.Anchors</c>.
    /// </summary>
    public Func<SecomTrustAnchors> TrustAnchorsSource { get; init; } = static () => SecomTrustAnchors.BuiltIn;

    /// <summary>
    /// Checks a trusted SECOM signer for revocation (issue #833); <see langword="null"/>
    /// skips the check. A download fetches the CRLs it needs. Reading a record
    /// judges revocation again from CRLs already held
    /// (<see cref="SecomRevocation.CacheOnly"/>), unless the caller asks to fetch
    /// them, and a signer once found revoked stays revoked.
    /// </summary>
    public SecomRevocation? Revocation { get; init; }

    /// <summary>
    /// The client SECOM objects (<see cref="RemoteEnvelope.Secom"/>) are
    /// downloaded with: one whose handler trusts MCP-issued server certificates
    /// (<see cref="SecomServerTrust.CreateHandler"/>, #829). Other downloads,
    /// and SECOM ones when this is <see langword="null"/>, use the client given
    /// to the constructor.
    /// </summary>
    public HttpClient? SecomHttpClient { get; init; }

    /// <summary>
    /// The signer for <see cref="RemoteEnvelope.SecomPost"/> downloads: this
    /// client's MCP identity when one is set (#838). Without one, such a
    /// download fails with <see cref="SecomIdentityRequiredException"/>.
    /// </summary>
    public Func<SecomEnvelopeSigner?>? SecomSigner { get; init; }

    /// <summary>
    /// Returns the downloaded copy of <paramref name="cellName"/> (a cell or
    /// package name), or <see langword="null"/> when it has not been
    /// downloaded (or its record is unreadable or its files are gone).
    /// </summary>
    public DownloadedCell? TryGetDownloaded(string cellName) => TryGetDownloaded(cellName, fetchRevocation: false);

    /// <summary>
    /// <see cref="TryGetDownloaded(string)"/>, fetching the CRLs a SECOM
    /// signer's revocation check needs when <paramref name="fetchRevocation"/>
    /// (index runs, off the UI thread).
    /// </summary>
    internal DownloadedCell? TryGetDownloaded(string cellName, bool fetchRevocation)
    {
        ArgumentException.ThrowIfNullOrEmpty(cellName);

        var cellFolder = CellFolder(cellName);
        var recordPath = Path.Combine(cellFolder, RecordFileName);
        try
        {
            if (!File.Exists(recordPath))
                return null;

            var record = JsonSerializer.Deserialize(File.ReadAllText(recordPath), RecordJson.CellRecord);
            if (record is null)
                return null;

            // Records written before packages hold a single, top-level dataset.
            var datasets = new Dictionary<string, LocalItemLocation>(StringComparer.OrdinalIgnoreCase);
            foreach (var dataset in record.Datasets ?? [new DatasetRecord(
                record.Name, record.RootRelativePath, record.BaseRelativePath, record.UpdateRelativePaths, record.CatalogueRelativePath)])
            {
                var location = new LocalItemLocation(
                    Path.GetFullPath(Path.Combine(cellFolder, dataset.RootRelativePath)),
                    dataset.BaseRelativePath,
                    dataset.UpdateRelativePaths,
                    dataset.CatalogueRelativePath);
                if (!File.Exists(Path.Combine(location.RootPath, location.RelativePath)))
                    return null;
                datasets.TryAdd(dataset.Name, location);
            }

            if (datasets.Count == 0)
                return null;

            return new DownloadedCell(
                record.Name, record.Edition, record.Update, record.PublishedAt, record.DownloadedAt, datasets.Values.First())
            {
                IsPackage = record.IsPackage,
                Datasets = datasets,
                Signature = record.Signature?.ToCheck(TrustAnchors, fetchRevocation ? Revocation : Revocation?.CacheOnly, _time.GetUtcNow()),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the cell (or package) described by <paramref name="item"/>,
    /// replacing any previous copy.
    /// </summary>
    /// <param name="item">
    /// A feed item with a <see cref="RemoteItemLocation"/>; its <see cref="CollectionItem.Name"/> is the
    /// cell name, unless the location names a <see cref="RemoteItemLocation.Package"/>.
    /// </param>
    /// <param name="bytesProgress">Receives the number of bytes received so far.</param>
    /// <param name="cancellationToken">Cancels the download; nothing is left behind.</param>
    /// <exception cref="ArgumentException">The item has no remote location.</exception>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="InvalidDataException">
    /// The download holds no <c>.000</c> base cell for the item (or, for a
    /// package, none at all); or a SECOM object is encrypted, malformed, or
    /// its signature does not match.
    /// </exception>
    public async Task<DownloadedCell> DownloadAsync(
        CollectionItem item,
        IProgress<long>? bytesProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Location is not RemoteItemLocation remote)
            throw new ArgumentException($"{item.Name} has no download location.", nameof(item));

        var folderName = remote.Package ?? item.Name;
        if (!IsSafeName(folderName))
            throw new InvalidDataException($"'{folderName}' is not a safe download name.");
        Directory.CreateDirectory(Root);
        var token = Guid.NewGuid().ToString("N");
        var zipPath = Path.Combine(Root, $".{folderName}.{token}.zip.partial");
        var staging = Path.Combine(Root, $".{folderName}.{token}.staging");

        try
        {
            var isSecom = remote.Envelope is RemoteEnvelope.Secom or RemoteEnvelope.SecomPost;
            var http = isSecom ? SecomHttpClient ?? _httpClient : _httpClient;
            using var request = remote.Envelope == RemoteEnvelope.SecomPost
                ? SecomClient.CreatePostGetRequest(
                    remote.Uri,
                    SecomSigner?.Invoke() ?? throw new SecomIdentityRequiredException(
                        $"{item.Name} can only be fetched with a signed SECOM 2.0 request, which needs an MCP identity."),
                    _time.GetUtcNow())
                : new HttpRequestMessage(HttpMethod.Get, remote.Uri);
            using (var response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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
            SignatureRecord? signature = null;
            if (isSecom)
            {
                signature = UnwrapSecom(item, remote, zipPath, staging, TrustAnchors, Revocation, _time.GetUtcNow());
            }
            else if (IsZip(zipPath))
            {
                ZipFile.ExtractToDirectory(zipPath, staging);
            }
            else
            {
                // A bare cell (some community lists link .000 files directly), or a
                // bare dataset whose stated layout is the file itself (remote S-100
                // catalogues: one HDF5 file per dataset).
                var fileName = Path.GetFileName(Uri.UnescapeDataString(remote.Uri.AbsolutePath));
                var isStatedFile = remote.Layout is { UpdateRelativePaths.Count: 0 } stated
                    && string.Equals(stated.RelativePath, fileName, StringComparison.Ordinal)
                    && IsSafeName(fileName);
                if (!isStatedFile && !string.Equals(Path.GetExtension(fileName), ".000", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The download for {folderName} is neither a zip nor a .000 cell.");
                Directory.CreateDirectory(staging);
                File.Move(zipPath, Path.Combine(staging, fileName));
            }

            var record = Describe(item, remote, staging) with { Signature = signature };
            File.WriteAllText(Path.Combine(staging, RecordFileName), JsonSerializer.Serialize(record, RecordJson.CellRecord));

            Replace(CellFolder(folderName), staging);
            return TryGetDownloaded(folderName)
                ?? throw new InvalidDataException($"Downloaded {folderName} could not be read back.");
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>The names of the cells and packages downloaded here (folders holding a record).</summary>
    public IReadOnlyList<string> ListDownloaded()
    {
        try
        {
            return Directory.Exists(Root)
                ? Directory.EnumerateDirectories(Root)
                    .Where(d => File.Exists(Path.Combine(d, RecordFileName)))
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(n => !n.StartsWith('.'))
                    .Order(StringComparer.Ordinal)
                    .ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Deletes the downloaded copy of <paramref name="cellName"/> (a cell or
    /// package name). The folder is first moved aside, so a reader never sees
    /// it half deleted.
    /// </summary>
    /// <returns>True when a copy was deleted; false when there was none.</returns>
    /// <exception cref="ArgumentException">The name is not a single, safe folder name.</exception>
    public bool Delete(string cellName)
    {
        ArgumentException.ThrowIfNullOrEmpty(cellName);
        if (!IsSafeName(cellName))
            throw new ArgumentException($"'{cellName}' is not a safe download name.", nameof(cellName));

        var folder = CellFolder(cellName);
        if (!Directory.Exists(folder) || !File.Exists(Path.Combine(folder, RecordFileName)))
            return false;

        var retired = Path.Combine(Root, $".{cellName}.{Guid.NewGuid():N}.deleted");
        Directory.Move(folder, retired);
        TryDeleteDirectory(retired);
        return true;
    }

    private string CellFolder(string cellName) => Path.Combine(Root, cellName);

    /// <summary>
    /// Decodes a downloaded SECOM <c>Get</c> response into the file named by
    /// the item's layout, in <paramref name="staging"/>, after checking its signature.
    /// </summary>
    private static SignatureRecord UnwrapSecom(
        CollectionItem item,
        RemoteItemLocation remote,
        string responsePath,
        string staging,
        SecomTrustAnchors trust,
        SecomRevocation? revocation,
        DateTimeOffset now)
    {
        if (remote.Layout is not { UpdateRelativePaths.Count: 0, CatalogueRelativePath: null } layout || !IsSafeName(layout.RelativePath))
            throw new InvalidDataException($"The SECOM object {item.Name} has no single-file layout to save its data as.");

        SecomDataObject secom;
        using (var response = File.OpenRead(responsePath))
            secom = SecomClient.ReadDataObject(response);

        if (secom.Metadata is { DataProtection: true })
            throw new InvalidDataException($"The SECOM object {item.Name} is encrypted; reading it needs a certificate, which is not supported yet.");

        var signature = SecomSignatureVerifier.Verify(secom.Data, secom.Metadata, trust, now, revocation);
        if (signature.Status == SecomSignatureStatus.Invalid)
            throw new InvalidDataException($"The signature of the SECOM object {item.Name} does not match its data.");

        var data = secom.Metadata is { Compressed: true } ? Decompress(secom.Data, item.Name) : secom.Data;
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, layout.RelativePath), data);
        return SignatureRecord.From(signature, secom.Metadata);
    }

    /// <summary>SECOM compresses as ZIP; a single-file archive is the object's data.</summary>
    private static byte[] Decompress(byte[] data, string name)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (files.Length != 1)
                throw new InvalidDataException($"The compressed SECOM object {name} holds {files.Length} files; one was expected.");
            using var entry = files[0].Open();
            using var buffer = new MemoryStream();
            entry.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (InvalidDataException) when (data.Length < 4 || data[0] != (byte)'P' || data[1] != (byte)'K')
        {
            throw new InvalidDataException($"The compressed SECOM object {name} is not a zip archive.");
        }
    }

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    /// <summary>Finds the catalogue, base cell(s) and updates in an extracted download.</summary>
    private CellRecord Describe(CollectionItem item, RemoteItemLocation remote, string staging)
    {
        var catalogues = Directory.EnumerateFiles(staging, ExchangeSetLayout.S57CatalogueName, Recursive).ToArray();
        DatasetRecord[] datasets;
        if (remote.Layout is { } layout)
        {
            // The publisher says where the files are (S-100 feeds): any product, no discovery.
            datasets = [DescribeLayout(item, staging, layout)];
        }
        else if (remote.Package is { } package)
        {
            datasets = Directory.EnumerateFiles(staging, "*.000", Recursive)
                .Order(StringComparer.Ordinal)
                .Select(c => DescribeCell(staging, c, catalogues))
                .DistinctBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (datasets.Length == 0)
                throw new InvalidDataException($"The download for {package} contains no .000 base cells.");
        }
        else
        {
            var baseCell = Directory.EnumerateFiles(staging, item.Name + ".000", Recursive).FirstOrDefault()
                ?? throw new InvalidDataException($"The download for {item.Name} contains no {item.Name}.000.");
            datasets = [DescribeCell(staging, baseCell, catalogues)];
        }

        var first = datasets[0];
        // A discovered package has no single edition; a stated layout is one dataset.
        var isPackage = remote.Package is not null && remote.Layout is null;
        return new CellRecord(
            remote.Package ?? item.Name,
            isPackage ? null : item.Edition,
            isPackage ? null : item.Update,
            remote.LastModified,
            _time.GetUtcNow(),
            remote.Uri.AbsoluteUri,
            first.RootRelativePath,
            first.BaseRelativePath,
            first.UpdateRelativePaths,
            first.CatalogueRelativePath,
            isPackage,
            remote.Package is null ? null : datasets);
    }

    /// <summary>Records a dataset at a publisher-stated layout, keeping only files that arrived.</summary>
    private static DatasetRecord DescribeLayout(CollectionItem item, string staging, PackageLayout layout)
    {
        string? Existing(string? relative)
        {
            if (relative is null)
                return null;
            var full = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"'{relative}' leaves the download.");
            return File.Exists(full) ? relative.Replace('\\', '/') : null;
        }

        var baseFile = Existing(layout.RelativePath)
            ?? throw new InvalidDataException($"The download for {item.Name} contains no {layout.RelativePath}.");
        return new DatasetRecord(
            item.Name,
            string.Empty,
            baseFile,
            layout.UpdateRelativePaths.Select(Existing).OfType<string>().ToArray(),
            Existing(layout.CatalogueRelativePath));
    }

    /// <summary>A single, ordinary folder name: no separators, no <c>.</c>/<c>..</c>, no invalid characters.</summary>
    private static bool IsSafeName(string name) =>
        name.Length > 0
        && name != "." && name != ".."
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Records one base cell: its exchange-set root, updates and catalogue.</summary>
    private static DatasetRecord DescribeCell(string staging, string baseCell, IReadOnlyList<string> catalogues)
    {
        // Prefer the (nearest) folder holding CATALOG.031 above the cell as the
        // root (the exchange set); fall back to the base cell's own folder.
        var catalogue = catalogues
            .Where(c => Path.GetFullPath(baseCell).StartsWith(
                Path.GetDirectoryName(Path.GetFullPath(c))! + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .MaxBy(c => c.Length);
        var root = catalogue is not null ? Path.GetDirectoryName(catalogue)! : Path.GetDirectoryName(baseCell)!;

        var stem = Path.GetFileNameWithoutExtension(baseCell);
        var updates = Directory.EnumerateFiles(Path.GetDirectoryName(baseCell)!)
            .Select(f => (Path: f, Number: UpdateNumber(f, stem)))
            .Where(u => u.Number > 0)
            .OrderBy(u => u.Number)
            .Select(u => Relative(root, u.Path))
            .ToArray();

        return new DatasetRecord(
            stem,
            Relative(staging, root),
            Relative(root, baseCell),
            updates,
            catalogue is null ? null : Path.GetFileName(catalogue));
    }

    private static bool IsZip(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) == magic.Length
            && magic[0] == (byte)'P' && magic[1] == (byte)'K';
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

    /// <summary>
    /// The <c>.source.json</c> record; paths are relative to the cell folder /
    /// exchange-set root. The top-level dataset fields describe the (first)
    /// cell; <see cref="Datasets"/> lists every cell of a package.
    /// </summary>
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
        string? CatalogueRelativePath,
        bool IsPackage = false,
        IReadOnlyList<DatasetRecord>? Datasets = null,
        SignatureRecord? Signature = null);

    /// <summary>
    /// The signature check of a SECOM download, with the certificates the
    /// object carried so its signer can be judged again on read (#823).
    /// Records written before #823 have no certificates and keep their
    /// recorded trust (none) until the object is downloaded again; records
    /// written before #833 have no <see cref="Revocation"/>.
    /// </summary>
    private sealed record SignatureRecord(
        string Status,
        string? Signer,
        bool? SignerTrusted,
        bool SignerExpired,
        string? Detail,
        string? TrustAnchor = null,
        IReadOnlyList<string>? Certificates = null,
        string? RootThumbprint = null,
        string? Revocation = null)
    {
        public static SignatureRecord From(SecomSignatureCheck check, SecomExchangeMetadata? metadata) => new(
            check.Status.ToString(), check.Signer, check.SignerTrusted, check.SignerExpired, check.Detail, check.TrustAnchor,
            metadata is { PublicCertificates.Count: > 0 } ? metadata.PublicCertificates : null,
            metadata?.RootCertificateThumbprint,
            check.SignerRevocation.ToString());

        public SecomSignatureCheck ToCheck(SecomTrustAnchors trust, SecomRevocation? revocation, DateTimeOffset now)
        {
            var recorded = Enum.TryParse<SecomRevocationStatus>(Revocation, out var revoked) ? revoked : SecomRevocationStatus.NotChecked;
            var check = new SecomSignatureCheck(
                Enum.TryParse<SecomSignatureStatus>(Status, out var status) ? status : SecomSignatureStatus.Unsigned,
                Signer, SignerTrusted, SignerExpired, Detail)
            { TrustAnchor = TrustAnchor, SignerRevocation = recorded };
            if (Certificates is not { Count: > 0 } certificates)
                return check;

            // Revocation is permanent: a signer found revoked stays so when its CRL is not at hand now.
            var rechecked = SecomSignatureVerifier.Recheck(check, certificates, RootThumbprint, trust, now, revocation);
            return recorded == SecomRevocationStatus.Revoked && rechecked is { SignerTrusted: true, SignerRevocation: SecomRevocationStatus.NotChecked }
                ? rechecked with { SignerRevocation = SecomRevocationStatus.Revoked }
                : rechecked;
        }
    }

    /// <summary>One base cell of a download.</summary>
    private sealed record DatasetRecord(
        string Name,
        string RootRelativePath,
        string BaseRelativePath,
        IReadOnlyList<string> UpdateRelativePaths,
        string? CatalogueRelativePath);

    [System.Text.Json.Serialization.JsonSerializable(typeof(CellRecord))]
    private sealed partial class RecordJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
}
