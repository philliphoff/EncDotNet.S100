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
/// Instances are not thread-safe; callers that re-render re-entrantly must
/// serialize access (the S-101 processor already does so via its render gate).
/// </para>
/// </remarks>
public sealed class InMemoryPatternClipCache : IPatternClipCache
{
    private string? _key;
    private IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>? _value;

    /// <inheritdoc />
    public long Hits { get; private set; }

    /// <inheritdoc />
    public long Misses { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> GetOrCompute(
        string key,
        Func<IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_value is not null && string.Equals(_key, key, StringComparison.Ordinal))
        {
            Hits++;
            return _value;
        }

        Misses++;
        var produced = factory();
        _key = key;
        _value = produced;
        return produced;
    }
}
