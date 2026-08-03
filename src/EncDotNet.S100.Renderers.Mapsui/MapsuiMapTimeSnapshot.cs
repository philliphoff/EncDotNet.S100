namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Immutable aggregate time state for the datasets registered with a
/// <see cref="MapsuiMapSession"/>.
/// </summary>
public sealed class MapsuiMapTimeSnapshot
{
    private IReadOnlyList<DateTime> _samples = [];
    private IReadOnlyList<MapsuiMapTimeSegment> _coverageSegments = [];

    /// <summary>Gets an empty time snapshot.</summary>
    public static MapsuiMapTimeSnapshot Empty { get; } = new();

    /// <summary>Gets the earliest registered sample, or <see langword="null"/>.</summary>
    public DateTime? Minimum { get; init; }

    /// <summary>Gets the latest registered sample, or <see langword="null"/>.</summary>
    public DateTime? Maximum { get; init; }

    /// <summary>Gets the current global clock value, or <see langword="null"/>.</summary>
    public DateTime? Current { get; init; }

    /// <summary>Gets all distinct registered samples in ascending order.</summary>
    public IReadOnlyList<DateTime> Samples
    {
        get => _samples;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _samples = Array.AsReadOnly(value.ToArray());
        }
    }

    /// <summary>
    /// Gets the merged intervals over which at least one registered dataset can
    /// portray data.
    /// </summary>
    public IReadOnlyList<MapsuiMapTimeSegment> CoverageSegments
    {
        get => _coverageSegments;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _coverageSegments = Array.AsReadOnly(value.ToArray());
        }
    }

    /// <summary>Gets whether at least one time sample is registered.</summary>
    public bool IsActive => Samples.Count > 0;
}
