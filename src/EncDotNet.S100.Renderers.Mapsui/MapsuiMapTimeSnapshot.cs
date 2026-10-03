namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Immutable aggregate time state for the datasets registered with a
/// <see cref="MapsuiDatasetLayerSession"/>.
/// </summary>
public sealed class MapsuiMapTimeSnapshot
{
    private IReadOnlyList<DateTime> _samples = [];
    private IReadOnlyList<MapsuiMapTimeSegment> _coverageSegments = [];
    private IReadOnlyList<MapsuiMapTimedDataset> _datasets = [];

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

    /// <summary>
    /// Gets the registered datasets that have time samples, with the first and
    /// last of them, in registration order — for example to name the forecast
    /// runs the timeline spans.
    /// </summary>
    public IReadOnlyList<MapsuiMapTimedDataset> Datasets
    {
        get => _datasets;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _datasets = Array.AsReadOnly(value.ToArray());
        }
    }

    /// <summary>Gets whether at least one time sample is registered.</summary>
    public bool IsActive => Samples.Count > 0;
}

/// <summary>A registered dataset that has time samples, and the span they cover.</summary>
/// <param name="Name">The dataset's name (usually its file name without extension).</param>
/// <param name="First">Its earliest time sample.</param>
/// <param name="Last">Its latest time sample.</param>
public sealed record MapsuiMapTimedDataset(string Name, DateTime First, DateTime Last)
{
    private IReadOnlyList<MapsuiMapTimeSegment> _coverage = [];

    private IReadOnlyList<DateTime> _samples = [];

    /// <summary>The dataset's product specification (e.g. <c>S-111</c>), when known.</summary>
    public string? ProductSpec { get; init; }

    /// <summary>The session's id for the dataset (the value of its <c>MapDatasetId</c>), when known.</summary>
    public string? DatasetId { get; init; }

    /// <summary>
    /// When the producer issued the dataset (UTC), from its data, when known;
    /// for a forecast, usually some time after its model run (#720).
    /// </summary>
    public DateTime? IssueTime { get; init; }

    /// <summary>How the dataset picks the sample it draws.</summary>
    public MapsuiTimeSelectionKind Selection { get; init; } = MapsuiTimeSelectionKind.Nearest;

    /// <summary>How far from the clock a sample may be and still be drawn.</summary>
    public TimeSpan Tolerance { get; init; } = TimeSpan.MaxValue;

    /// <summary>
    /// The sample the dataset draws at <paramref name="time"/> under its time
    /// rule, or <see langword="null"/> when it has no data near that time and hides.
    /// </summary>
    /// <param name="time">The clock value.</param>
    public DateTime? SampleAt(DateTime time) => MapsuiTimeSelection.Select(Samples, Selection, Tolerance, time);

    /// <summary>The dataset's own time samples, ascending; empty when not computed.</summary>
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
    /// The windows in which this dataset draws under its product's time
    /// policy; outside them it has no data near the clock and hides. Empty
    /// when not computed.
    /// </summary>
    public IReadOnlyList<MapsuiMapTimeSegment> Coverage
    {
        get => _coverage;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _coverage = Array.AsReadOnly(value.ToArray());
        }
    }

    /// <summary>True when the dataset has data within its tolerance of <paramref name="time"/>.</summary>
    /// <param name="time">The clock value.</param>
    public bool Covers(DateTime time) => Coverage.Any(window => time >= window.Start && time <= window.End);

    /// <inheritdoc />
    public bool Equals(MapsuiMapTimedDataset? other) =>
        other is not null
        && Name == other.Name
        && First == other.First
        && Last == other.Last
        && ProductSpec == other.ProductSpec
        && DatasetId == other.DatasetId
        && IssueTime == other.IssueTime
        && Selection == other.Selection
        && Tolerance == other.Tolerance
        && Coverage.SequenceEqual(other.Coverage)
        && Samples.SequenceEqual(other.Samples);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, First, Last, Coverage.Count);
}
