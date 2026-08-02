using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// In-memory, single-slot <see cref="IPatternClipCache"/>: it remembers only
/// the most recently computed key and its clipped pattern geometry.
/// </summary>
/// <remarks>
/// <para>
/// A single slot bounds memory (one cell's clip geometry can be large) while
/// still capturing the dominant reuse pattern — repeatedly re-rendering the
/// <em>same</em> dataset under different palettes. It mirrors the
/// <c>S101DatasetProcessor</c>'s existing single-entry
/// <c>_cachedPortrayalInstructions</c> cache, which is keyed the same way.
/// </para>
/// <para>
/// The disk-backed <see cref="DiskPatternClipCache"/> retains multiple cells
/// and survives process restarts behind the same <see cref="IPatternClipCache"/>
/// contract; this implementation deliberately keeps the in-process footprint to
/// a single cell.
/// </para>
/// <para>
/// Instances are thread-safe. Concurrent misses may duplicate the same
/// computation, but the last identical result becomes the retained entry.
/// </para>
/// </remarks>
public sealed class InMemoryPatternClipCache : IPatternClipCache
{
    private readonly object _gate = new();
    private string? _key;
    private IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>? _value;
    private long _hits;
    private long _misses;

    /// <inheritdoc />
    public long Hits
    {
        get { lock (_gate) { return _hits; } }
    }

    /// <inheritdoc />
    public long Misses
    {
        get { lock (_gate) { return _misses; } }
    }

    /// <inheritdoc />
    public IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> GetOrCompute(
        string key,
        Func<IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (_value is not null && string.Equals(_key, key, StringComparison.Ordinal))
            {
                _hits++;
                return _value;
            }

            _misses++;
        }

        var produced = factory();

        lock (_gate)
        {
            _key = key;
            _value = produced;
        }

        return produced;
    }
}
