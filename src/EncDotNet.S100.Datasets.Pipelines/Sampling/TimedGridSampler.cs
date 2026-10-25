using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>
/// Samples a time-varying regular grid (S-104 and S-111 dcf2): the node
/// nearest the point, at the time step nearest the requested time. The
/// product supplies the grid's georeferencing and how to read a node.
/// </summary>
/// <typeparam name="TCoverage">The product's per-time-step coverage.</typeparam>
/// <remarks>
/// Only regular grids (data coding format 2) are read; another format is
/// found (so it can still win the dataset choice) but reads as not supported.
/// A dataset whose first step does not contain the point reports that the
/// point is outside its grid.
/// </remarks>
internal abstract class TimedGridSampler<TCoverage> : ICoverageSampler
    where TCoverage : class
{
    /// <summary>The time steps, in storage order.</summary>
    protected abstract IReadOnlyList<TCoverage> Coverages { get; }

    /// <summary>The dataset's horizontal CRS (EPSG code).</summary>
    protected abstract int? HorizontalCrs { get; }

    /// <summary>The dataset's axis-order assessment.</summary>
    protected abstract AxisOrderAssessment? AxisOrder { get; }

    /// <summary>The dataset's data coding format.</summary>
    protected abstract int DataCodingFormat { get; }

    /// <summary>The dataset's issue time (model run).</summary>
    protected abstract DateTime? Run { get; }

    /// <summary>Why a windowed query cannot be answered when the point is outside the grid.</summary>
    protected abstract string WindowOutsideGridReason { get; }

    /// <summary>The georeferencing of one step.</summary>
    protected abstract GridGeometry Geometry(TCoverage coverage);

    /// <summary>The time of one step.</summary>
    protected abstract DateTime TimePoint(TCoverage coverage);

    /// <summary>The value at a node of one step, or <c>null</c> when the node carries the fill value.</summary>
    protected abstract SampledValue? ReadNode(TCoverage coverage, GridNode node, DateTimeOffset? requestedTime);

    /// <summary>The value reported for a window in which every step has no data.</summary>
    protected abstract SampledValue Fallback(DateTime sampleTime, DateTimeOffset requestedTime, GridNode node);

    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var coverages = Coverages;
        if (coverages.Count == 0 || Find(coverages[0], query.Transforms, query.Latitude, query.Longitude) is null)
        {
            return CoverageSiteSearch.None with { OutsideGrid = true, WindowUnsupportedReason = WindowOutsideGridReason };
        }

        var probe = Geometry(coverages[0]);
        var steps = coverages.Select(TimePoint).ToArray();
        return new CoverageSiteSearch([new Site(this, query, steps, probe.SpacingLatitudinal * probe.SpacingLongitudinal)]);
    }

    private GridNode? Find(TCoverage coverage, ICrsTransformFactory transforms, double latitude, double longitude) =>
        GridNodes.Find(Geometry(coverage), HorizontalCrs, AxisOrder, transforms, latitude, longitude);

    private CoverageReading.NotSupported? UnsupportedFormat() =>
        DataCodingFormat == 2
            ? null
            : new CoverageReading.NotSupported(
                $"data coding format {DataCodingFormat} is not yet supported (only dcf=2 / regular grid)");

    private sealed class Site : CoverageSampleSite
    {
        private readonly TimedGridSampler<TCoverage> _owner;
        private readonly CoverageSampleQuery _query;
        private readonly DateTime[] _steps;

        public Site(TimedGridSampler<TCoverage> owner, CoverageSampleQuery query, DateTime[] steps, double cellArea)
            : base(CoverageSiteKind.Grid, cellArea, owner.Run, CoverageStepWindow.FromSteps(steps))
        {
            _owner = owner;
            _query = query;
            _steps = steps;
        }

        public override IReadOnlyList<DateTime> Steps => _steps;

        public override CoverageReading Read(DateTimeOffset? requestedTime)
        {
            if (_owner.UnsupportedFormat() is { } unsupported) return unsupported;

            var step = _owner.Coverages[CoverageSampling.SelectTimeStep(_steps, requestedTime)];
            if (_owner.Find(step, _query.Transforms, _query.Latitude, _query.Longitude) is not { } node)
            {
                return new CoverageReading.OutsideGrid();
            }

            return _owner.ReadNode(step, node, requestedTime) is { } value
                ? new CoverageReading.Value(value)
                : new CoverageReading.NoData(node.Row, node.Col, _owner.TimePoint(step));
        }

        public override CoverageReading ReadSteps(IReadOnlyList<CoverageStepRequest> steps)
        {
            if (_owner.UnsupportedFormat() is { } unsupported) return unsupported;

            var coverages = _owner.Coverages;
            if (_owner.Find(coverages[0], _query.Transforms, _query.Latitude, _query.Longitude) is not { } node)
            {
                return new CoverageReading.OutsideGrid();
            }

            var entries = new List<CoverageTimedSample>(steps.Count);
            foreach (var (requested, index) in steps)
            {
                var step = coverages[index];
                entries.Add(new CoverageTimedSample(
                    GridNodes.Utc(_owner.TimePoint(step)),
                    requested,
                    _owner.ReadNode(step, node, requested)));
            }

            // SampleCoverageResult.Value is non-nullable, so a window with no
            // data at any step reports a placeholder at the first step.
            return new CoverageReading.Series(
                entries,
                _owner.Fallback(GridNodes.Utc(_owner.TimePoint(coverages[steps[0].StepIndex])), steps[0].RequestedTime, node));
        }
    }
}
