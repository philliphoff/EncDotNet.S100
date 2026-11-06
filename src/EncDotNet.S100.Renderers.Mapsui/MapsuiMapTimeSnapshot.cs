using EncDotNet.S100.Datasets.Pipelines;

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

    /// <summary>
    /// When a newer dataset is next expected (UTC), such as NHC's next
    /// advisory (<c>ITimeAwareDatasetProcessor.NextIssueTime</c>), or
    /// <see langword="null"/> when the producer doesn't say.
    /// </summary>
    public DateTime? NextIssueTime { get; init; }

    /// <summary>
    /// The model run the dataset belongs to, as its processor reports it
    /// (<c>ITimeAwareDatasetProcessor.ForecastRun</c>), or <see langword="null"/>
    /// when it is not a forecast run.
    /// </summary>
    public DatasetForecastRun? ForecastRun { get; init; }

    /// <summary>How the dataset picks the sample it draws.</summary>
    public MapsuiTimeSelectionKind Selection { get; init; } = MapsuiTimeSelectionKind.Nearest;

    /// <summary>How far from the clock a sample may be and still be drawn.</summary>
    public TimeSpan Tolerance { get; init; } = TimeSpan.MaxValue;

    /// <summary>What the dataset's steps are (<see cref="TimeSemantics.Kind"/>).</summary>
    public TimeStepKind StepKind { get; init; } = TimeStepKind.Forecast;

    /// <summary>How long after its step the data drawn is stale (<see cref="TimeSemantics.StaleAfter"/>), or null for never.</summary>
    public TimeSpan? StaleAfter { get; init; }

    /// <summary>The key of the datasets of which only one draws at a time (<see cref="TimeSemantics.Supersession"/>), or null.</summary>
    public string? Supersession { get; init; }

    /// <summary>
    /// The sample the dataset draws at <paramref name="time"/> under its time
    /// rule, or <see langword="null"/> when it has no data near that time and hides.
    /// </summary>
    /// <param name="time">The clock value.</param>
    public DateTime? SampleAt(DateTime time) => MapsuiTimeSelection.Select(Samples, Selection, Tolerance, time);

    /// <summary>
    /// The sample the dataset draws at <paramref name="time"/> beside
    /// <paramref name="all"/>: <see cref="SampleAt"/>, or <see langword="null"/>
    /// when another dataset with the same <see cref="Supersession"/> takes over
    /// (<see cref="MapsuiTimeSupersession"/>).
    /// </summary>
    /// <param name="time">The clock value.</param>
    /// <param name="all">Every time-aware dataset, as the snapshot lists them.</param>
    public DateTime? DrawnSampleAt(DateTime time, IEnumerable<MapsuiMapTimedDataset> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        var sample = SampleAt(time);
        if (Supersession is null || sample is null)
            return sample;
        return MapsuiTimeSupersession.IsSuperseded(Candidate(sample), all.Select(d => d.Candidate(d.SampleAt(time))))
            ? null
            : sample;
    }

    /// <summary>
    /// True when another dataset with the same <see cref="Supersession"/>
    /// draws in this one's place at <paramref name="time"/>, though it has data then.
    /// </summary>
    /// <param name="time">The clock value.</param>
    /// <param name="all">Every time-aware dataset, as the snapshot lists them.</param>
    public bool IsSupersededAt(DateTime time, IEnumerable<MapsuiMapTimedDataset> all) =>
        SampleAt(time) is not null && DrawnSampleAt(time, all) is null;

    /// <summary>
    /// How old the sample drawn at <paramref name="time"/> is, to the nearest hour,
    /// once past <see cref="StaleAfter"/>; null while current.
    /// </summary>
    /// <param name="time">The clock value.</param>
    public TimeSpan? StaleAgeAt(DateTime time) => MapsuiTimeSupersession.StaleAge(SampleAt(time), time, StaleAfter);

    private MapsuiTimeCandidate Candidate(DateTime? sample) => new(DatasetId ?? Name, Supersession, StepKind, sample);

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
        && NextIssueTime == other.NextIssueTime
        && ForecastRun == other.ForecastRun
        && Selection == other.Selection
        && Tolerance == other.Tolerance
        && StepKind == other.StepKind
        && StaleAfter == other.StaleAfter
        && Supersession == other.Supersession
        && Coverage.SequenceEqual(other.Coverage)
        && Samples.SequenceEqual(other.Samples);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, First, Last, Coverage.Count);
}
