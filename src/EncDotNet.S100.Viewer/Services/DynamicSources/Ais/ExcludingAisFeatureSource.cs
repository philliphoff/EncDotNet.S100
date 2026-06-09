using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

/// <summary>
/// Viewer-layer decorator that hides a single feature (by id) from a
/// wrapped <see cref="IDynamicFeatureSource"/>. Used by "pirate mode":
/// when own-ship impersonates a live AIS target, the same vessel must
/// not be drawn twice — once as the own-ship overlay and again as an
/// AIS target. Setting <see cref="ExcludedId"/> to the impersonated
/// target's feature id (<c>"ais:{mmsi}"</c>) removes it from the overlay,
/// vessel list, and pick results while leaving the inner source — which
/// the pirate-mode controller still reads — untouched.
/// </summary>
/// <remarks>
/// <para>
/// The decorator preserves the inner source's <see cref="Id"/> and
/// <see cref="DynamicSourceMetadata"/> so it can be registered in place
/// of the inner source without disturbing renderer resolution
/// (<see cref="DynamicSourceMetadata.RendererKey"/>), layer identity, or
/// persisted visibility keyed by <see cref="Id"/>.
/// </para>
/// <para>
/// <b>Two AIS surfaces.</b> The pirate-mode controller subscribes to the
/// <i>raw</i> inner source so it always sees the followed target, even
/// while that target is excluded from the overlay. Only the overlay /
/// list / pick consumers see the decorated (filtered) view. The
/// decorator therefore must wrap whatever instance is registered as the
/// public AIS source — including a
/// <see cref="DeferredAisFeatureSource"/> whose real inner source is
/// constructed lazily.
/// </para>
/// <para>
/// <b>Change propagation.</b> Inner <see cref="IDynamicFeatureSource.Changed"/>
/// events are forwarded verbatim. Additionally, mutating
/// <see cref="ExcludedId"/> raises a synthetic
/// <see cref="DynamicSourceChangeKind.Reset"/> so the overlay, vessel
/// list, and pick re-read the snapshot immediately rather than lagging
/// to the next AIS event (AIS reports can be minutes apart).
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="ExcludedId"/> is guarded by a lock;
/// <see cref="Changed"/> may be raised on any thread (forwarded from the
/// inner source or from the calling thread of an <see cref="ExcludedId"/>
/// mutation), matching the <see cref="IDynamicFeatureSource"/> contract.
/// </para>
/// </remarks>
internal sealed class ExcludingAisFeatureSource : IDynamicFeatureSource, IAsyncDisposable
{
    private readonly IDynamicFeatureSource _inner;
    private readonly object _gate = new();
    private string? _excludedId;
    private bool _disposed;

    /// <summary>
    /// Wraps <paramref name="inner"/>, forwarding its identity, metadata,
    /// and change events while filtering <see cref="ExcludedId"/> out of
    /// the published snapshot.
    /// </summary>
    /// <param name="inner">The source to decorate. Owned by this
    /// decorator: disposing the decorator disposes the inner source.</param>
    public ExcludingAisFeatureSource(IDynamicFeatureSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _inner.Changed += OnInnerChanged;
    }

    /// <inheritdoc />
    public string Id => _inner.Id;

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata => _inner.Metadata;

    /// <summary>
    /// The feature id to hide from the published snapshot, or
    /// <see langword="null"/> to publish the inner source unchanged.
    /// Conventionally an AIS feature id (<c>"ais:{mmsi}"</c>). Setting it
    /// to a new value raises <see cref="Changed"/> with
    /// <see cref="DynamicSourceChangeKind.Reset"/>.
    /// </summary>
    public string? ExcludedId
    {
        get { lock (_gate) return _excludedId; }
        set
        {
            lock (_gate)
            {
                if (string.Equals(_excludedId, value, StringComparison.Ordinal))
                    return;
                _excludedId = value;
            }

            Changed?.Invoke(this, new DynamicFeaturesChanged
            {
                Kind = DynamicSourceChangeKind.Reset,
                ChangedIds = Array.Empty<string>(),
            });
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures
    {
        get
        {
            string? excluded;
            lock (_gate) excluded = _excludedId;

            var snapshot = _inner.CurrentFeatures;
            if (excluded is null)
                return snapshot;

            var filtered = new List<DynamicFeature>(snapshot.Count);
            foreach (var feature in snapshot)
            {
                if (!string.Equals(feature.Id, excluded, StringComparison.Ordinal))
                    filtered.Add(feature);
            }
            return filtered;
        }
    }

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    private void OnInnerChanged(object? sender, DynamicFeaturesChanged e)
    {
        if (_disposed) return;
        Changed?.Invoke(this, e);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _inner.Changed -= OnInnerChanged;
        if (_inner is IAsyncDisposable asyncInner)
        {
            await asyncInner.DisposeAsync().ConfigureAwait(false);
        }
        else if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
