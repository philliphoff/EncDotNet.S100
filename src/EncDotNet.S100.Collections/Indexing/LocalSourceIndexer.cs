using System.Globalization;
using System.IO.Compression;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S57.ExchangeSets;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes local sources — <see cref="LocalFolderSource"/> and
/// <see cref="ExchangeSetSource"/> — in place: exchange sets from their
/// catalogues (folder or ZIP), loose datasets through a
/// <see cref="DatasetProbe"/>.
/// </summary>
/// <remarks>
/// No dataset is loaded. S-100 items come entirely from <c>CATALOG.XML</c>
/// (including parsed coverage polygons); S-57 items come from
/// <c>CATALOG.031</c> plus a read of each cell's leading <c>DSID</c> record.
/// Per-file problems become <see cref="IndexDiagnostic"/>s; they never fail
/// the whole index.
/// </remarks>
public sealed class LocalSourceIndexer : ICollectionSourceIndexer
{
    private readonly DatasetProbe? _probe;

    /// <summary>Creates an indexer.</summary>
    /// <param name="probe">
    /// Reads metadata from loose dataset files. When <see langword="null"/>,
    /// only loose S-57 cells are recognised, and without bounds.
    /// </param>
    public LocalSourceIndexer(DatasetProbe? probe = null)
    {
        _probe = probe;
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) =>
        source is LocalFolderSource or ExchangeSetSource;

    /// <inheritdoc/>
    public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var (path, recursive) = Resolve(source);
        return ValueTask.FromResult(LocalSourceScanner.Scan(path, recursive, cancellationToken)?.Fingerprint);
    }

    /// <inheritdoc/>
    public ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (path, recursive) = Resolve(source);

        return new ValueTask<SourceIndex>(Task.Run(
            () => Index(source.Id, path, recursive, progress, cancellationToken),
            cancellationToken));
    }

    private SourceIndex Index(
        Guid sourceId,
        string path,
        bool recursive,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<IndexDiagnostic>();
        var items = new List<CollectionItem>();

        var scan = LocalSourceScanner.Scan(path, recursive, cancellationToken);
        if (scan is null)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, "Source path not found.", path));
            return new SourceIndex(sourceId, DateTimeOffset.UtcNow, null, items, diagnostics);
        }

        foreach (var unit in scan.Units)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var unitPath = unit switch
            {
                S100FolderUnit f => f.Directory,
                S57FolderUnit f => f.Directory,
                ZipUnit z => z.Path,
                LooseFileUnit l => l.Path,
                _ => null,
            };
            progress?.Report(new IndexProgress(items.Count, unitPath));

            try
            {
                switch (unit)
                {
                    case S100FolderUnit folder:
                        items.AddRange(ReadS100Folder(scan.RootDirectory, folder, diagnostics));
                        break;
                    case S57FolderUnit folder:
                        items.AddRange(ReadS57Folder(scan.RootDirectory, folder, diagnostics, cancellationToken));
                        break;
                    case ZipUnit zip:
                        items.AddRange(ReadZip(scan.RootDirectory, zip, diagnostics, cancellationToken));
                        break;
                    case LooseFileUnit loose:
                        if (ReadLoose(scan.RootDirectory, loose, diagnostics, cancellationToken) is { } item)
                            items.Add(item);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, unitPath));
            }
        }

        progress?.Report(new IndexProgress(items.Count, null));
        return new SourceIndex(sourceId, DateTimeOffset.UtcNow, scan.Fingerprint, items, diagnostics);
    }

    private static IEnumerable<CollectionItem> ReadS100Folder(
        string sourceRoot, S100FolderUnit folder, List<IndexDiagnostic> diagnostics)
    {
        var catalogue = ExchangeCatalogueReader.Read(
            Path.Combine(folder.Directory, folder.CatalogueFileName), ExchangeCatalogueReadOptions.DiscoveryOnly);
        var context = new ExchangeSetContext(
            folder.Directory,
            IsZip: false,
            Prefix: string.Empty,
            folder.CatalogueFileName,
            LocalSourceScanner.RelativePath(sourceRoot, folder.Directory),
            relative => OpenFile(folder.Directory, relative));

        return ExchangeSetItemReader.ReadS100(catalogue, context, diagnostics).ToList();
    }

    private static IEnumerable<CollectionItem> ReadS57Folder(
        string sourceRoot, S57FolderUnit folder, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var cataloguePath = S57ExchangeSetCatalog.ResolveCataloguePath(folder.Directory);
        var catalogue = S57CatalogReader.ReadFromFile(cataloguePath, logger: null);
        var context = new ExchangeSetContext(
            folder.Directory,
            IsZip: false,
            Prefix: string.Empty,
            Path.GetFileName(cataloguePath),
            LocalSourceScanner.RelativePath(sourceRoot, folder.Directory),
            relative => OpenFile(folder.Directory, relative));

        return ExchangeSetItemReader.ReadS57(catalogue, context, diagnostics, cancellationToken).ToList();
    }

    /// <summary>
    /// Indexes every exchange set in a ZIP: each S-100 or S-57 catalogue entry,
    /// at any depth, roots one exchange set at its directory within the ZIP.
    /// </summary>
    private static List<CollectionItem> ReadZip(
        string sourceRoot, ZipUnit zip, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var items = new List<CollectionItem>();
        using var archive = ZipFile.OpenRead(zip.Path);

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
            entries.TryAdd(entry.FullName.Replace('\\', '/'), entry);

        var catalogues = entries.Keys
            .Select(name => (Name: name, Prefix: DirectoryPrefix(name), FileName: LeafName(name)))
            .Where(c => LocalSourceScanner.IsS100CatalogueName(c.FileName)
                || string.Equals(c.FileName, LocalSourceScanner.S57CatalogueName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var zipKey = LocalSourceScanner.RelativePath(sourceRoot, zip.Path);
        var found = false;

        foreach (var set in catalogues)
        {
            var prefix = set.Key;
            var groupKey = zipKey + "!/" + prefix.TrimEnd('/');
            Stream? Open(string relative) =>
                entries.TryGetValue(prefix + relative.Replace('\\', '/').TrimStart('/'), out var e) ? e.Open() : null;

            var s100 = LocalSourceScanner.PickS100Catalogue(set.Select(c => c.FileName));
            if (s100 is not null)
            {
                found = true;
                ExchangeCatalogue catalogue;
                using (var stream = entries[prefix + s100].Open())
                    catalogue = ExchangeCatalogueReader.Read(stream, ExchangeCatalogueReadOptions.DiscoveryOnly);

                var context = new ExchangeSetContext(zip.Path, IsZip: true, prefix, s100, groupKey, Open);
                items.AddRange(ExchangeSetItemReader.ReadS100(catalogue, context, diagnostics));
            }

            var s57 = set.FirstOrDefault(c =>
                string.Equals(c.FileName, LocalSourceScanner.S57CatalogueName, StringComparison.OrdinalIgnoreCase));
            if (s57.Name is not null)
            {
                found = true;
                S57Catalog catalogue;
                using (var stream = entries[s57.Name].Open())
                    catalogue = S57CatalogReader.Read(stream, logger: null);

                var context = new ExchangeSetContext(zip.Path, IsZip: true, prefix, s57.FileName, groupKey, Open);
                items.AddRange(ExchangeSetItemReader.ReadS57(catalogue, context, diagnostics, cancellationToken));
            }
        }

        if (!found)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Info, "ZIP does not contain an exchange-set catalogue.", zip.Path));
        }

        return items;
    }

    /// <summary>
    /// Indexes a loose dataset through the probe, adding the S-57
    /// <c>DSID</c> facts for <c>.000</c> cells. Returns <see langword="null"/>
    /// when the file is not a recognised dataset.
    /// </summary>
    private CollectionItem? ReadLoose(
        string sourceRoot, LooseFileUnit loose, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        Core.DatasetMetadata? metadata = null;
        if (_probe is not null)
        {
            try
            {
                metadata = _probe(loose.Path, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Warning, ex.Message, loose.Path));
            }
        }

        var isCell = string.Equals(Path.GetExtension(loose.Path), ".000", StringComparison.OrdinalIgnoreCase);
        var header = isCell && metadata?.Spec.Name is null or "S-57" ? TryReadHeader(loose.Path) : null;
        var latest = header is not null && loose.UpdatePaths.Count > 0 ? TryReadHeader(loose.UpdatePaths[^1]) : null;

        if (metadata is null && header is null)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Info, "Not a recognised dataset.", loose.Path));
            return null;
        }

        var spec = metadata?.Spec.Name ?? "S-57";
        var version = metadata is { Spec.Edition: var edition } && edition != default ? edition.ToString() : null;

        GeoBounds? bounds = null;
        if (metadata?.Extent is { } extent)
        {
            if (metadata.HorizontalCrsEpsg is null or 4326)
            {
                bounds = new GeoBounds(extent.SouthLatitude, extent.WestLongitude, extent.NorthLatitude, extent.EastLongitude);
            }
            else
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Info,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Extent is in EPSG:{metadata.HorizontalCrsEpsg}; bounds unavailable until loaded."),
                    loose.Path));
            }
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata?.TimeCoverage is { } time)
        {
            properties["timeStart"] = time.Start.ToString("O", CultureInfo.InvariantCulture);
            properties["timeEnd"] = time.End.ToString("O", CultureInfo.InvariantCulture);
        }
        if (header?.ProducingAgency is { } agency)
            properties["producingAgency"] = agency.ToString(CultureInfo.InvariantCulture);

        var directory = Path.GetDirectoryName(loose.Path)!;
        var name = Path.GetFileNameWithoutExtension(loose.Path);

        return new CollectionItem
        {
            Key = LocalSourceScanner.RelativePath(sourceRoot, loose.Path),
            ProductSpec = spec,
            ProductSpecVersion = version,
            Name = name,
            Edition = header?.EditionNumber,
            Update = latest?.UpdateNumber ?? header?.UpdateNumber,
            IssueDate = latest?.IssueDate ?? header?.IssueDate,
            UpdateApplicationDate = header?.UpdateApplicationDate,
            CompilationScale = header?.CompilationScale,
            MinimumDisplayScale = metadata?.DisplayScale?.Minimum,
            MaximumDisplayScale = metadata?.DisplayScale?.Maximum,
            UsageBand = spec == "S-57" ? UsageBand.FromCellName(name) : null,
            Bounds = bounds,
            Location = new LocalItemLocation(
                directory,
                Path.GetFileName(loose.Path),
                loose.UpdatePaths.Select(Path.GetFileName).OfType<string>().ToArray()),
            Properties = properties,
        };
    }

    private static S57DatasetHeader? TryReadHeader(string path)
    {
        try
        {
            return S57DatasetHeader.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Stream? OpenFile(string directory, string relativePath)
    {
        var full = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    private static (string Path, bool Recursive) Resolve(CollectionSource source) => source switch
    {
        LocalFolderSource folder => (folder.Path, folder.Recursive),
        ExchangeSetSource set => (set.Path, true),
        _ => throw new NotSupportedException($"{nameof(LocalSourceIndexer)} cannot index {source.GetType().Name}."),
    };

    private static string DirectoryPrefix(string entryName)
    {
        var slash = entryName.LastIndexOf('/');
        return slash < 0 ? string.Empty : entryName[..(slash + 1)];
    }

    private static string LeafName(string entryName)
    {
        var slash = entryName.LastIndexOf('/');
        return slash < 0 ? entryName : entryName[(slash + 1)..];
    }
}
