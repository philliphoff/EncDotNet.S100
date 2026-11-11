namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Optional helper that an adapter with aging semantics (AIS
/// sleeping/lost, stale-sensor styling, fleet-track expiry) can opt
/// into. Sources without aging do not use it.
/// </summary>
/// <remarks>
/// <para>
/// The tracker is a plain dictionary-plus-event store with two
/// operations: <see cref="Apply"/> ingests one inbound update and
/// returns a change descriptor; <see cref="Sweep"/> retires entries
/// that haven't been updated within a caller-supplied threshold.
/// The library does <b>not</b> impose timer defaults — adapters
/// supply their own (AIS, for example, uses spec-defined timers in
/// the adapter, not in this helper).
/// </para>
/// <para>
/// The tracker is thread-safe for concurrent <see cref="Apply"/> /
/// <see cref="Sweep"/> / <see cref="Current"/> access; internal
/// state is guarded by a single lock.
/// </para>
/// </remarks>
/// <typeparam name="TInbound">
/// The adapter's raw update type, projected to
/// <see cref="DynamicFeature"/> by the caller-supplied projection.
/// </typeparam>
public sealed class DynamicFeatureTracker<TInbound>
{
    private readonly Func<TInbound, DynamicFeature> _project;
    private readonly object _lock = new();
    private readonly Dictionary<string, DynamicFeature> _byId = new(StringComparer.Ordinal);
    private IReadOnlyList<DynamicFeature> _snapshot = Array.Empty<DynamicFeature>();

    /// <summary>
    /// Creates a tracker. <paramref name="project"/> converts the
    /// adapter's inbound update into a <see cref="DynamicFeature"/>
    /// — typically a small mapping that fills the stable
    /// <see cref="DynamicFeature.Id"/>, the geometry, and the
    /// adapter-flavoured <see cref="DynamicFeature.Attributes"/>.
    /// </summary>
    public DynamicFeatureTracker(Func<TInbound, DynamicFeature> project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _project = project;
    }

    /// <summary>
    /// The current immutable snapshot. Updated atomically on each
    /// <see cref="Apply"/> / <see cref="Sweep"/> call. Safe to read
    /// from any thread.
    /// </summary>
    public IReadOnlyList<DynamicFeature> Current
    {
        get
        {
            lock (_lock) return _snapshot;
        }
    }

    /// <summary>
    /// Applies one inbound update and returns a description of the
    /// change (Added or Updated). The returned descriptor names the
    /// single touched id.
    /// </summary>
    public DynamicFeaturesChanged Apply(TInbound update)
    {
        var feature = _project(update);
        ArgumentNullException.ThrowIfNull(feature);
        var id = feature.Id ?? throw new InvalidOperationException(
            "Projected DynamicFeature.Id must be non-null.");

        DynamicSourceChangeKind kind;
        lock (_lock)
        {
            kind = _byId.ContainsKey(id)
                ? DynamicSourceChangeKind.Updated
                : DynamicSourceChangeKind.Added;
            _byId[id] = feature;
            _snapshot = _byId.Values.ToArray();
        }

        return new DynamicFeaturesChanged
        {
            Kind = kind,
            ChangedIds = new[] { id },
        };
    }

    /// <summary>
    /// Removes entries whose <see cref="DynamicFeature.LastUpdated"/>
    /// is older than <paramref name="now"/> minus
    /// <paramref name="retain"/>. Returns a Removed descriptor when
    /// anything was swept, or a Reset descriptor with no ids when
    /// nothing changed.
    /// </summary>
    /// <param name="now">
    /// Caller-supplied "now" — exposed for testability with a
    /// synthetic clock.
    /// </param>
    /// <param name="retain">
    /// Maximum age before an entry is removed.
    /// </param>
    public DynamicFeaturesChanged Sweep(DateTimeOffset now, TimeSpan retain)
    {
        List<string>? removed = null;
        lock (_lock)
        {
            foreach (var (id, feature) in _byId)
            {
                if (now - feature.LastUpdated > retain)
                {
                    (removed ??= new List<string>()).Add(id);
                }
            }
            if (removed is null)
            {
                return new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset };
            }

            foreach (var id in removed) _byId.Remove(id);
            _snapshot = _byId.Values.ToArray();
        }

        return new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Removed,
            ChangedIds = removed,
        };
    }

    /// <summary>
    /// Removes the entry with the given id, if present. Returns a
    /// Removed descriptor when the id was found, or a Reset
    /// descriptor with no ids when not.
    /// </summary>
    public DynamicFeaturesChanged Remove(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_lock)
        {
            if (!_byId.Remove(id))
            {
                return new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset };
            }
            _snapshot = _byId.Values.ToArray();
        }
        return new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Removed,
            ChangedIds = new[] { id },
        };
    }

    /// <summary>
    /// Drops all known features and returns a Reset descriptor.
    /// </summary>
    public DynamicFeaturesChanged Clear()
    {
        lock (_lock)
        {
            _byId.Clear();
            _snapshot = Array.Empty<DynamicFeature>();
        }
        return new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset };
    }
}
