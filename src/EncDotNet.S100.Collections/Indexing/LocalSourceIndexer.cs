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
/// <para>
/// A ZIP is a dataset when it is one (#1688): an NHC tropical cyclone
/// advisory, or a folder of one storm's zips, planned by
/// <see cref="LocalSourceScanner"/>, is one item probed as a loose dataset
/// is. Any other ZIP is offered to the probe first, so a zipped dataset under
/// a name of its own is still found, and is otherwise read for exchange-set
/// catalogues as before.
/// </para>
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
            () => IndexPath(source.Id, path, recursive, progress, cancellationToken),
            cancellationToken));
    }

    /// <summary>
    /// Indexes the folder, exchange set, ZIP or loose dataset at <paramref name="path"/>
    /// (shared with <see cref="LocalManifestIndexer"/>, which indexes each manifest path).
    /// </summary>
    internal SourceIndex IndexPath(
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
                AdvisoryUnit a => a.Path,
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
                    case AdvisoryUnit advisory:
                        if (ReadAdvisory(scan.RootDirectory, advisory, diagnostics, cancellationToken) is { } advisoryItem)
                            items.Add(advisoryItem);
                        break;
                    case LooseFileUnit loose:
                        if (ReadLoose(scan.RootDirectory, loose, diagnostics, cancellationToken) is { } item)
                            items.Add(item);
                        break;
                    case RunFolderUnit run:
                        items.AddRange(ReadRunFolder(scan.RootDirectory, run, diagnostics, cancellationToken));
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
    /// Indexes a ZIP: as one dataset when the probe names its product (a
    /// zipped dataset, #1688), else every exchange set in it: each S-100 or
    /// S-57 catalogue entry, at any depth, roots one exchange set at its
    /// directory within the ZIP.
    /// </summary>
    private List<CollectionItem> ReadZip(
        string sourceRoot, ZipUnit zip, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (TryProbe(zip.Path, diagnostics, cancellationToken) is { } metadata)
            return [LooseItem(sourceRoot, new LooseFileUnit(zip.Path, []), metadata, header: null, latest: null, diagnostics)];

        return ReadExchangeSetZip(sourceRoot, zip, diagnostics, cancellationToken);
    }

    private static List<CollectionItem> ReadExchangeSetZip(
        string sourceRoot, ZipUnit zip, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var items = new List<CollectionItem>();
        using var archive = ZipFile.OpenRead(zip.Path);

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
            entries.TryAdd(entry.FullName.Replace('\\', '/'), entry);

        var catalogues = entries.Keys
            .Select(name => (Name: name, Prefix: DirectoryPrefix(name), FileName: LeafName(name)))
            .Where(c => ExchangeSetLayout.IsS100CatalogueName(c.FileName) || ExchangeSetLayout.IsS57CatalogueName(c.FileName))
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

            var s100 = ExchangeSetLayout.PickS100Catalogue(set.Select(c => c.FileName));
            if (s100 is not null)
            {
                found = true;
                ExchangeCatalogue catalogue;
                using (var stream = entries[prefix + s100].Open())
                    catalogue = ExchangeCatalogueReader.Read(stream, ExchangeCatalogueReadOptions.DiscoveryOnly);

                var context = new ExchangeSetContext(zip.Path, IsZip: true, prefix, s100, groupKey, Open);
                items.AddRange(ExchangeSetItemReader.ReadS100(catalogue, context, diagnostics));
            }

            var s57 = set.FirstOrDefault(c => ExchangeSetLayout.IsS57CatalogueName(c.FileName));
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
        var metadata = TryProbe(loose.Path, diagnostics, cancellationToken);

        var isCell = string.Equals(Path.GetExtension(loose.Path), ".000", StringComparison.OrdinalIgnoreCase);
        var header = isCell && metadata?.Spec.Name is null or "S-57" or AmlDatasetInfo.SpecName ? TryReadHeader(loose.Path) : null;
        var latest = header is not null && loose.UpdatePaths.Count > 0 ? TryReadHeader(loose.UpdatePaths[^1]) : null;

        if (metadata is null && header is null)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Info, "Not a recognised dataset.", loose.Path));
            return null;
        }

        return LooseItem(sourceRoot, loose, metadata, header, latest, diagnostics);
    }

    /// <summary>Asks the probe for a dataset's metadata; a probe failure is a warning, not an error.</summary>
    private Core.DatasetMetadata? TryProbe(string path, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (_probe is null)
            return null;

        try
        {
            return _probe(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Warning, ex.Message, path));
            return null;
        }
    }

    /// <summary>The item of a loose dataset, from its probed metadata and, for an S-57 cell, its <c>DSID</c>.</summary>
    private static CollectionItem LooseItem(
        string sourceRoot,
        LooseFileUnit loose,
        Core.DatasetMetadata? metadata,
        S57DatasetHeader? header,
        S57DatasetHeader? latest,
        List<IndexDiagnostic> diagnostics)
    {
        var spec = header?.Aml is not null ? AmlDatasetInfo.SpecName : metadata?.Spec.Name ?? "S-57";
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
        if (header?.Aml is { } aml)
            AmlLibraryProperties.Write(aml, properties);

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

    /// <summary>
    /// Indexes a folder of GRIB2 files (#1075): when the probe reads the folder
    /// as one dataset (a model run published one file per forecast hour), one
    /// item whose location is the folder itself; otherwise each file as a loose
    /// dataset, as before.
    /// </summary>
    private IEnumerable<CollectionItem> ReadRunFolder(
        string sourceRoot, RunFolderUnit run, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (ReadLoose(sourceRoot, new LooseFileUnit(run.Directory, []), [], cancellationToken) is { } folder)
            return [AsFolderItem(sourceRoot, folder, run.Directory, run.Files.Count)];

        return run.Files
            .Select(file => ReadLoose(sourceRoot, new LooseFileUnit(file, []), diagnostics, cancellationToken))
            .OfType<CollectionItem>()
            .ToArray();
    }

    /// <summary>
    /// Indexes an NHC advisory (#1688) as a loose dataset: from its forecast
    /// zip, keyed and located as that file, or from its folder, keyed and
    /// located as a folder, as a GRIB2 run is.
    /// </summary>
    private CollectionItem? ReadAdvisory(
        string sourceRoot, AdvisoryUnit advisory, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var item = ReadLoose(sourceRoot, new LooseFileUnit(advisory.Path, []), diagnostics, cancellationToken);
        return item is not null && Directory.Exists(advisory.Path)
            ? AsFolderItem(sourceRoot, item, advisory.Path, advisory.Files.Count)
            : item;
    }

    /// <summary>
    /// Re-keys an item read from a folder: keyed by the folder's relative path
    /// with a trailing slash, named after it, and located at it.
    /// </summary>
    private static CollectionItem AsFolderItem(string sourceRoot, CollectionItem folder, string directory, int files)
    {
        var parent = Path.GetDirectoryName(directory) ?? directory;
        var name = Path.GetFileName(directory);
        var properties = new Dictionary<string, string>(folder.Properties, StringComparer.Ordinal)
        {
            ["files"] = files.ToString(CultureInfo.InvariantCulture),
        };
        return folder with
        {
            Key = LocalSourceScanner.RelativePath(sourceRoot, directory) is { Length: > 0 } relative && relative != "."
                ? relative + "/"
                : name + "/",
            Name = name,
            Location = new LocalItemLocation(parent, name, []),
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
