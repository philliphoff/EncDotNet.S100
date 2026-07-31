using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;
using IDatasetCatalog = EncDotNet.S100.Datasets.Pipelines.Catalog.IDatasetCatalog;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Adapts the viewer's <see cref="IDatasetLoaderService"/> to the MCP
/// <see cref="IDatasetCatalog"/> contract, exposing each successfully
/// loaded dataset as a typed <see cref="LoadedDataset"/> for the
/// MCP tool surface.
/// </summary>
/// <remarks>
/// <para>
/// The viewer's loader keeps spec-specific <c>IDatasetProcessor</c>s
/// privately. Rather than widen that contract, this adapter re-opens
/// each loaded dataset via the per-spec <c>Open(string)</c> helpers
/// so the catalog snapshot is fully self-contained — read-only and
/// independent of the loader's rendering state. Re-opening doubles
/// memory for in-process datasets, which is acceptable for an
/// off-by-default tool surface.
/// </para>
/// <para>
/// Entries are cached by <see cref="DatasetEntry"/> identity so each
/// file is parsed only once per dataset lifetime. Cache entries are
/// evicted on <see cref="IDatasetLoaderService.DatasetRemoved"/>.
/// </para>
/// <para>
/// Exchange-set entries (where <see cref="DatasetEntry.Source"/> is
/// non-null) and specs without a path-based <c>Open</c> helper are
/// silently skipped — the MCP surface only ever sees datasets it can
/// fully model.
/// </para>
/// </remarks>
internal sealed class ViewerDatasetCatalog : IDatasetCatalog, IDisposable
{
    // LoadedDataset.Bounds is contractually WGS-84; an S-102 tile may be in a
    // projected CRS (e.g. a UTM zone) whose grid georeferencing is native
    // metres, so the projector reprojects its extent through this factory.
    private static readonly ICrsTransformFactory CrsTransforms = new ProjNetCrsTransformFactory();

    private readonly IDatasetLoaderService _loader;
    private readonly Dictionary<DatasetEntry, LoadedDataset> _cache = new();
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset> _snapshot = [];
    private bool _disposed;

    public ViewerDatasetCatalog(IDatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _loader.DatasetLoaded += OnDatasetLoaded;
        _loader.DatasetRemoved += OnDatasetRemoved;
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets => _snapshot;

    /// <inheritdoc />
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    private void OnDatasetLoaded(DatasetEntry entry)
    {
        if (_disposed) return;

        // Plain on-disk entries: require the file to be present so any
        // downstream consumer that DOES need a path can find one.
        // Exchange-set entries instead carry an IAssetSource +
        // RelativePath and are read via streams further down — no on-disk
        // path is required.
        if (!entry.IsFromExchangeSet
            && (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)))
        {
            return;
        }

        if (entry.IsFromExchangeSet
            && (entry.Source is null || string.IsNullOrEmpty(entry.RelativePath)))
        {
            return;
        }

        LoadedDataset? projected;
        try
        {
            projected = TryProject(entry);
        }
        catch
        {
            // A malformed dataset shouldn't poison the catalog. The
            // viewer will already have surfaced the load error via its
            // own diagnostics; we just skip it here.
            return;
        }

        if (projected is null) return;

        IReadOnlyList<LoadedDataset> next;
        lock (_gate)
        {
            _cache[entry] = projected;
            next = _cache.Values.ToArray();
            _snapshot = next;
        }
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
        {
            Kind = DatasetCatalogChangeKind.Added,
            DatasetId = projected.Id,
        });
    }

    private void OnDatasetRemoved(DatasetEntry entry)
    {
        if (_disposed) return;

        DatasetId? removedId = null;
        lock (_gate)
        {
            if (!_cache.TryGetValue(entry, out var prev)) return;
            removedId = prev.Id;
            _cache.Remove(entry);
            _snapshot = _cache.Values.ToArray();
        }
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
        {
            Kind = DatasetCatalogChangeKind.Removed,
            DatasetId = removedId,
        });
    }

    private static LoadedDataset? TryProject(DatasetEntry entry)
    {
        var id = new DatasetId(entry.DisplayName);

        // DatasetPipelineFactory.DetectProductSpec returns the literal
        // string "S-57" for ENC .000 files that pass the S-57 DSPM
        // discriminator (see DatasetPipelineFactory.cs:94) and "S-101"
        // for everything else with that extension. The MCP surface
        // treats both as S-101 — LoadedDatasetProjector maps them to a
        // single canonical spec name so the tool surface stays
        // predictable.
        var spec = entry.ProductSpec;
        using var stream = OpenEntryStream(entry);
        return LoadedDatasetProjector.Project(id, spec, stream, BuildExternalTextResolver(entry), CrsTransforms);
    }

    /// <summary>
    /// <summary>
    /// Opens the dataset bytes for <paramref name="entry"/> — either from
    /// disk (plain entry) or from its <see cref="DatasetEntry.Source"/>
    /// asset source (exchange-set entry). The returned stream must be
    /// disposed by the caller.
    /// </summary>
    private static Stream OpenEntryStream(DatasetEntry entry)
    {
        if (entry.IsFromExchangeSet)
        {
            // SYNC BRIDGE: IAssetSource.OpenAsync for FileSystem / Zip
            // backings is effectively synchronous (no real I/O latency),
            // and TryProject is called from the synchronous DatasetLoaded
            // event chain. Async-up-the-stack would require flipping the
            // event signature, which is not justified for a one-shot open.
            return entry.Source!.OpenAsync(entry.RelativePath!).GetAwaiter().GetResult();
        }
        return File.OpenRead(entry.FilePath);
    }


    /// <summary>
    /// Builds a file-name → text resolver for an S-101 cell's
    /// <c>fileReference</c> attributes (S-101 Feature Catalogue, aliases
    /// <c>TXTDSC</c> / <c>NTXTDS</c>) when the cell was loaded from an
    /// exchange set, so MCP consumers (<c>identify_features</c> /
    /// <c>pick_features</c>) can surface the referenced text. Returns
    /// <c>null</c> for loose cells with no asset source.
    /// </summary>
    private static Func<string, string?>? BuildExternalTextResolver(DatasetEntry entry)
    {
        if (!entry.IsFromExchangeSet || entry.Source is null)
            return null;

        return new ExternalTextFileResolver(entry.Source, entry.RelativePath).AsDelegate();
    }


    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loader.DatasetLoaded -= OnDatasetLoaded;
        _loader.DatasetRemoved -= OnDatasetRemoved;
        lock (_gate)
        {
            _cache.Clear();
            _snapshot = [];
        }
    }
}
