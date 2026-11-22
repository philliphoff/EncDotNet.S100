using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// The viewer's one place to get a <see cref="ChartReferenceIndex"/>: it
/// builds each product's index on first use, off the UI thread, keeps it
/// while the product's catalogues are unchanged, and drops it when a
/// catalogue is replaced (symbol dictionary design note §3.4, R2).
/// </summary>
/// <remarks>
/// <para>
/// An index is keyed by the feature and portrayal catalogue content hashes
/// (<see cref="FeatureCatalogueManager.GetCatalogueHashAsync"/>,
/// <see cref="PortrayalCatalogueManager.GetCatalogueHashAsync"/>) and
/// <see cref="IndexFormatVersion"/>, so a catalogue whose files change is
/// rebuilt even when its declared version doesn't.
/// </para>
/// <para>
/// The Chart reference activity and MCP <c>search_chart_reference</c> and
/// <c>get_chart_reference_entry</c> read it. Nothing here needs the UI, so
/// the MCP tools work without the activity open.
/// </para>
/// </remarks>
internal sealed class ChartReferenceService : IDisposable
{
    /// <summary>
    /// The version of the index's shape. Bump it when
    /// <see cref="ChartReferenceIndex"/> reads the catalogues differently,
    /// so indexes built by the old reader aren't reused.
    /// </summary>
    public const int IndexFormatVersion = 1;

    private readonly FeatureCatalogueManager _features;
    private readonly PortrayalCatalogueManager _portrayals;
    private readonly Func<IEnumerable<string>> _loadedSpecs;
    private readonly IReadOnlyList<string> _bundledSpecs;
    private readonly Lock _gate = new();
    private readonly Dictionary<SpecRef, Slot> _slots = [];
    private readonly Dictionary<SpecRef, ChartReferenceArtwork> _artwork = [];
    private bool _disposed;

    /// <summary>Creates the service and subscribes to both catalogue managers' changes.</summary>
    /// <param name="features">The feature catalogues; a product without one is indexed from its rules alone.</param>
    /// <param name="portrayals">The portrayal catalogues and their rule files.</param>
    /// <param name="loadedSpecs">
    /// The portrayal specs of the datasets open now, such as <c>S-101</c> for
    /// a maritime S-57 cell and <c>S-401</c> for an inland one. The viewer
    /// reads the Datasets panel's rows, so call <see cref="Products"/> on the
    /// UI thread.
    /// </param>
    /// <param name="bundledSpecs">
    /// The products whose catalogues ship with the viewer; defaults to
    /// <see cref="Specifications.Specification.AvailableSpecs"/>.
    /// </param>
    public ChartReferenceService(
        FeatureCatalogueManager features,
        PortrayalCatalogueManager portrayals,
        Func<IEnumerable<string>> loadedSpecs,
        IReadOnlyList<string>? bundledSpecs = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(portrayals);
        ArgumentNullException.ThrowIfNull(loadedSpecs);
        _features = features;
        _portrayals = portrayals;
        _loadedSpecs = loadedSpecs;
        _bundledSpecs = bundledSpecs ?? Specifications.Specification.AvailableSpecs;
        _features.CataloguesChanged += OnCataloguesChanged;
        _portrayals.CataloguesChanged += OnCataloguesChanged;
    }

    /// <summary>
    /// Raised when a product's catalogue is replaced and its index dropped,
    /// on the thread that replaced it. <see cref="CataloguesChangedEventArgs.Spec"/>
    /// names the product; the next <see cref="GetIndexAsync"/> for it builds
    /// a new index.
    /// </summary>
    public event EventHandler<CataloguesChangedEventArgs>? Changed;

    /// <summary>
    /// Every product with a portrayal catalogue: the products with datasets
    /// open first, then every other catalogue, bundled ones (S-401 among
    /// them) and ones the user added (design note D4). Each group is in
    /// spec order. Read on the UI thread.
    /// </summary>
    public IReadOnlyList<ChartReferenceProduct> Products
    {
        get
        {
            var available = _bundledSpecs
                .Select(TryRef)
                .Concat(_portrayals.RegisteredCataloguesByRef.Keys.Select(s => (SpecRef?)s))
                .OfType<SpecRef>()
                .Where(_portrayals.HasCatalogue)
                .Distinct()
                .ToList();
            var loaded = _loadedSpecs()
                .Select(TryRef)
                .OfType<SpecRef>()
                .Where(available.Contains)
                .ToHashSet();
            return
            [
                .. available
                    .OrderBy(s => loaded.Contains(s) ? 0 : 1)
                    .ThenBy(s => s.Name, StringComparer.Ordinal)
                    .ThenBy(s => s.Edition)
                    .Select(s => new ChartReferenceProduct(s, loaded.Contains(s))),
            ];
        }
    }

    /// <summary>
    /// Returns <paramref name="spec"/>'s index, building it on a worker
    /// thread the first time and after its catalogues change. Callers asking
    /// at once share one build.
    /// </summary>
    /// <param name="spec">The product.</param>
    /// <param name="cancellationToken">Stops waiting; a build already started still finishes for the next caller.</param>
    /// <returns>The index, or <see langword="null"/> when the product has no portrayal catalogue.</returns>
    public async Task<ChartReferenceIndex?> GetIndexAsync(SpecRef spec, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (spec.Name is null || !_portrayals.HasCatalogue(spec))
            return null;

        // The hashes read every catalogue file the first time, so they're
        // taken on a worker thread too.
        var key = await Task.Run(() => KeyAsync(spec), cancellationToken).ConfigureAwait(false);
        if (key is null)
            return null;

        Task<ChartReferenceIndex?> build;
        lock (_gate)
        {
            if (_slots.TryGetValue(spec, out var slot)
                && slot.Key == key
                && !slot.Build.IsFaulted
                && !slot.Build.IsCanceled)
            {
                build = slot.Build;
            }
            else
            {
                // Not bound to the caller's token: the build is shared.
                build = Task.Run(() => BuildAsync(spec), CancellationToken.None);
                _slots[spec] = new Slot(key, build);
            }
        }

        return await build.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The artwork reader over <paramref name="spec"/>'s portrayal catalogue,
    /// which Chart reference draws its glyphs from (#1637). It is kept until
    /// the catalogue changes.
    /// </summary>
    /// <param name="spec">The product.</param>
    /// <returns>The reader, or <see langword="null"/> when the product has no portrayal catalogue.</returns>
    public ChartReferenceArtwork? GetArtwork(SpecRef spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_artwork.TryGetValue(spec, out var held))
                return held;
        }

        if (spec.Name is null || !_portrayals.HasCatalogue(spec))
            return null;
        PortrayalCatalogueProvider provider;
        try
        {
            provider = _portrayals.GetProvider(spec);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_artwork.TryGetValue(spec, out var artwork))
            {
                artwork = new ChartReferenceArtwork(provider);
                _artwork[spec] = artwork;
            }

            return artwork;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _features.CataloguesChanged -= OnCataloguesChanged;
        _portrayals.CataloguesChanged -= OnCataloguesChanged;
        lock (_gate)
        {
            _slots.Clear();
            _artwork.Clear();
        }
    }

    private async Task<string?> KeyAsync(SpecRef spec)
    {
        var portrayal = await _portrayals.GetCatalogueHashAsync(spec).ConfigureAwait(false);
        if (portrayal is null)
            return null;
        var features = await _features.GetCatalogueHashAsync(spec).ConfigureAwait(false);
        return $"{IndexFormatVersion}:{features ?? "-"}:{portrayal}";
    }

    private async Task<ChartReferenceIndex?> BuildAsync(SpecRef spec)
    {
        PortrayalCatalogueProvider provider;
        try
        {
            provider = _portrayals.GetProvider(spec);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        var features = _features.GetCatalogue(spec);
        return await ChartReferenceIndex.BuildAsync(spec, features, provider).ConfigureAwait(false);
    }

    private void OnCataloguesChanged(object? sender, CataloguesChangedEventArgs e)
    {
        lock (_gate)
        {
            // A build in flight finishes for no one: the next call sees new
            // hashes and starts another.
            _slots.Remove(e.Spec);
            _artwork.Remove(e.Spec);
        }

        Changed?.Invoke(this, e);
    }

    private static SpecRef? TryRef(string spec) =>
        SpecName.TryNormalize(spec, out var name) ? new SpecRef(name, default) : null;

    private sealed record Slot(string Key, Task<ChartReferenceIndex?> Build);
}

/// <summary>A product the Chart reference can describe.</summary>
/// <param name="Spec">The product specification.</param>
/// <param name="IsLoaded">Whether datasets of the product are open.</param>
internal sealed record ChartReferenceProduct(SpecRef Spec, bool IsLoaded);
