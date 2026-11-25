using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

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
/// <para>
/// <b>Probe results</b> (R13a). An index comes with the probe results
/// committed next to a bundled catalogue when their hashes match, or with
/// results cached on disk for these catalogues. A user-added or changed
/// catalogue has neither: given a trace engine, the service probes its rules
/// in the background, caches the results under the catalogues' hashes, swaps
/// in an index that has them and raises <see cref="ProbesReady"/>. Until
/// then, and without a trace engine, the index has no
/// <see cref="ChartReferenceIndex.Probes"/>, as before.
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
    private readonly Dictionary<SpecRef, CancellationTokenSource> _probes = [];
    private readonly string? _probeCacheDirectory;
    private readonly Func<ILuaEngine>? _traceEngine;
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
    /// <param name="probeCacheDirectory">
    /// Where probe results for catalogues without committed ones are cached,
    /// or <see langword="null"/> for no disk cache.
    /// </param>
    /// <param name="traceEngine">
    /// Makes the LuaCSharp engine that probes rules in the background, or
    /// <see langword="null"/> to use only committed and cached results.
    /// </param>
    public ChartReferenceService(
        FeatureCatalogueManager features,
        PortrayalCatalogueManager portrayals,
        Func<IEnumerable<string>> loadedSpecs,
        IReadOnlyList<string>? bundledSpecs = null,
        string? probeCacheDirectory = null,
        Func<ILuaEngine>? traceEngine = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(portrayals);
        ArgumentNullException.ThrowIfNull(loadedSpecs);
        _features = features;
        _portrayals = portrayals;
        _loadedSpecs = loadedSpecs;
        _bundledSpecs = bundledSpecs ?? Specifications.Specification.AvailableSpecs;
        _probeCacheDirectory = probeCacheDirectory;
        _traceEngine = traceEngine;
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
    /// Raised on a worker thread when a background probe of a product's
    /// rules finishes: the next <see cref="GetIndexAsync"/> for
    /// <see cref="CataloguesChangedEventArgs.Spec"/> returns an index with
    /// <see cref="ChartReferenceIndex.Probes"/>.
    /// </summary>
    public event EventHandler<CataloguesChangedEventArgs>? ProbesReady;

    /// <summary>
    /// Whether a background probe of <paramref name="spec"/>'s rules is
    /// running, so its feature type entries say "Working out variants…"
    /// until <see cref="ProbesReady"/> (R13b, #1721).
    /// </summary>
    /// <param name="spec">The product.</param>
    /// <returns>Whether a probe is running.</returns>
    public bool IsProbing(SpecRef spec)
    {
        lock (_gate)
            return _probes.ContainsKey(spec);
    }

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
                && slot.Key == key.Value.Key
                && !slot.Build.IsFaulted
                && !slot.Build.IsCanceled)
            {
                build = slot.Build;
            }
            else
            {
                // Not bound to the caller's token: the build is shared.
                var hashes = key.Value;
                build = Task.Run(() => BuildAsync(spec, hashes), CancellationToken.None);
                _slots[spec] = new Slot(hashes.Key, build);
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
            foreach (var probe in _probes.Values)
                probe.Cancel();
            _probes.Clear();
        }
    }

    private async Task<Hashes?> KeyAsync(SpecRef spec)
    {
        var portrayal = await _portrayals.GetCatalogueHashAsync(spec).ConfigureAwait(false);
        if (portrayal is null)
            return null;
        var features = await _features.GetCatalogueHashAsync(spec).ConfigureAwait(false);
        return new Hashes($"{IndexFormatVersion}:{features ?? "-"}:{portrayal}", features, portrayal);
    }

    private async Task<ChartReferenceIndex?> BuildAsync(SpecRef spec, Hashes hashes)
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
        var index = await ChartReferenceIndex.BuildAsync(spec, features, provider).ConfigureAwait(false);
        if (hashes.FeatureCatalogue is not { } fc || features is null || spec.Name is null)
            return index;

        // The committed results, or ones cached for these catalogues.
        var probes = await RuleProbeStore.LoadOrProbeAsync(spec.Name, fc, hashes.PortrayalCatalogue, _probeCacheDirectory, probe: null)
            .ConfigureAwait(false);
        if (probes is not null)
            return index.WithProbes(probes);

        if (_traceEngine is not null && RuleProber.Supports(spec.Name))
            StartProbe(spec, hashes, index, features, provider);
        return index;
    }

    /// <summary>
    /// Probes the rules of catalogues without committed or cached results,
    /// on a worker thread, and swaps in an index with the results when the
    /// catalogues are still the same.
    /// </summary>
    private void StartProbe(
        SpecRef spec, Hashes hashes, ChartReferenceIndex index, FeatureCatalogue features, PortrayalCatalogueProvider provider)
    {
        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_probes.Remove(spec, out var previous))
                previous.Cancel();
            _probes[spec] = cancellation;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var probes = await RuleProbeStore.LoadOrProbeAsync(
                    spec.Name!,
                    hashes.FeatureCatalogue!,
                    hashes.PortrayalCatalogue,
                    _probeCacheDirectory,
                    ct => RuleProber.ProbeAsync(
                        index,
                        features,
                        provider,
                        _traceEngine!(),
                        hashes.FeatureCatalogue!,
                        hashes.PortrayalCatalogue,
                        // Half the cores: the map keeps drawing meanwhile.
                        new RuleProberOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                        cancellationToken: ct),
                    cancellation.Token).ConfigureAwait(false);
                if (probes is null)
                    return;

                lock (_gate)
                {
                    if (cancellation.IsCancellationRequested || !_slots.TryGetValue(spec, out var slot) || slot.Key != hashes.Key)
                        return;
                    _slots[spec] = new Slot(hashes.Key, Task.FromResult<ChartReferenceIndex?>(index.WithProbes(probes)));
                }

                ProbesReady?.Invoke(this, new CataloguesChangedEventArgs(spec));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The reference works without probe results; a later build tries again.
            }
            finally
            {
                // Cancel is only called under the gate, so it can't meet a disposed source.
                lock (_gate)
                {
                    if (_probes.TryGetValue(spec, out var current) && current == cancellation)
                        _probes.Remove(spec);
                    cancellation.Dispose();
                }
            }
        }, CancellationToken.None);
    }

    private void OnCataloguesChanged(object? sender, CataloguesChangedEventArgs e)
    {
        lock (_gate)
        {
            // A build in flight finishes for no one: the next call sees new
            // hashes and starts another.
            _slots.Remove(e.Spec);
            _artwork.Remove(e.Spec);
            if (_probes.Remove(e.Spec, out var probe))
                probe.Cancel();
        }

        Changed?.Invoke(this, e);
    }

    private static SpecRef? TryRef(string spec) =>
        SpecName.TryNormalize(spec, out var name) ? new SpecRef(name, default) : null;

    private sealed record Slot(string Key, Task<ChartReferenceIndex?> Build);

    /// <summary>An index's cache key and the catalogue hashes it's made from.</summary>
    private readonly record struct Hashes(string Key, string? FeatureCatalogue, string PortrayalCatalogue);
}

/// <summary>A product the Chart reference can describe.</summary>
/// <param name="Spec">The product specification.</param>
/// <param name="IsLoaded">Whether datasets of the product are open.</param>
internal sealed record ChartReferenceProduct(SpecRef Spec, bool IsLoaded)
{
    /// <summary>The product id, e.g. <c>S-101</c>: the name a screen reader gives the combo's value (#1657).</summary>
    /// <returns>The product id.</returns>
    public override string ToString() => Spec.Name;
}
