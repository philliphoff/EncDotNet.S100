namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Represents a single surface current coverage grid at a specific time within an S-111 dataset.
/// </summary>
public sealed class SurfaceCurrentCoverage
{
    /// <summary>Latitude of the grid origin in decimal degrees.</summary>
    public required double OriginLatitude { get; init; }

    /// <summary>Longitude of the grid origin in decimal degrees.</summary>
    public required double OriginLongitude { get; init; }

    /// <summary>Grid spacing in the latitudinal direction in decimal degrees.</summary>
    public required double SpacingLatitudinal { get; init; }

    /// <summary>Grid spacing in the longitudinal direction in decimal degrees.</summary>
    public required double SpacingLongitudinal { get; init; }

    /// <summary>Number of grid points in the latitudinal direction (rows).</summary>
    public required int NumPointsLatitudinal { get; init; }

    /// <summary>Number of grid points in the longitudinal direction (columns).</summary>
    public required int NumPointsLongitudinal { get; init; }

    /// <summary>Start sequence describing the origin corner of the grid (e.g. "0,0").</summary>
    public string? StartSequence { get; init; }

    /// <summary>
    /// The HDF5 instance-group path this coverage was read from, e.g.
    /// <c>"/SurfaceCurrent/SurfaceCurrent.01"</c>. Populated by
    /// <see cref="S111DatasetReader"/>; nullable so synthetic test
    /// fixtures may omit it. Validation rules surface this on
    /// per-coverage findings via <c>RelatedFeatureId</c>, and append
    /// <c>#timePoint</c> on per-time-step findings (per
    /// <c>docs/design/non-gml-validation.md</c> §4.3). Multiple
    /// time-step coverages share an instance path; the
    /// <see cref="TimePoint"/> disambiguates them in finding messages.
    /// </summary>
    public string? GroupPath { get; init; }

    /// <summary>
    /// The time point for this coverage, parsed from the HDF5 group's <c>timePoint</c> attribute.
    /// </summary>
    public required DateTime TimePoint { get; init; }

    private readonly object _valuesGate = new();
    private SurfaceCurrentValue[]? _values;
    private Func<SurfaceCurrentValue[]>? _valuesFactory;

    /// <summary>
    /// Flat array of surface current values, ordered row-major.
    /// Index a cell at (row, col) as <c>Values[row * NumPointsLongitudinal + col]</c>.
    /// </summary>
    /// <remarks>
    /// May be supplied eagerly (the <c>init</c> setter, used by synthetic
    /// fixtures and the file-independent <see cref="S111DatasetReader.ReadAny(EncDotNet.S100.Hdf5.IHdf5File)"/>
    /// path) or lazily via <see cref="ValuesFactory"/>. When backed by a
    /// factory the per-time-step <c>values</c> compound is read from the
    /// underlying HDF5 file on first access and then cached, so opening a
    /// dataset with hundreds of time steps does not decode every step up
    /// front (S-111 Edition 2.0.0 §10.2.6; one <c>Group_NNN/values</c>
    /// dataset per time step). Access is thread-safe.
    /// </remarks>
    public SurfaceCurrentValue[] Values
    {
        get
        {
            if (_values is not null)
                return _values;

            lock (_valuesGate)
            {
                return _values ??= (_valuesFactory
                    ?? throw new InvalidOperationException(
                        "SurfaceCurrentCoverage.Values was accessed but neither an eager " +
                        "value array nor a lazy ValuesFactory was provided."))();
            }
        }
        init => _values = value;
    }

    /// <summary>
    /// Optional lazy provider for <see cref="Values"/>. Set by
    /// <see cref="S111DatasetReader"/> when deferred value reads are
    /// requested; the factory is invoked at most once (its result is
    /// cached on <see cref="Values"/>). Mutually exclusive with the
    /// eager <see cref="Values"/> initializer.
    /// </summary>
    internal Func<SurfaceCurrentValue[]>? ValuesFactory
    {
        init => _valuesFactory = value;
    }
}
