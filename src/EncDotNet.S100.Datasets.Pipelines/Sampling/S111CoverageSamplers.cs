using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S111;

namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>
/// Samples an S-111 regular-grid (dcf2) surface current: speed and direction
/// at the node nearest the point and the step nearest the time. Shared by
/// <see cref="S111DatasetProcessor"/> and the catalog's
/// <see cref="Catalog.S111CoverageData"/>.
/// </summary>
internal sealed class S111GridSampler : TimedGridSampler<SurfaceCurrentCoverage>
{
    /// <summary>The product sampled.</summary>
    public const string Spec = "S-111";

    // S-111 encodes surfaceCurrentSpeed in knots; the MCP contract also
    // reports m/s. 1 knot = 1852 m / 3600 s ≈ 0.514444 m/s.
    internal const double KnotsToMetresPerSecond = 1852.0 / 3600.0;

    private readonly S111Dataset _model;

    public S111GridSampler(S111CoverageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _model = source.Dataset;
    }

    protected override IReadOnlyList<SurfaceCurrentCoverage> Coverages => _model.Coverages;

    protected override int? HorizontalCrs => _model.HorizontalCRS;

    protected override AxisOrderAssessment? AxisOrder => _model.AxisOrder;

    protected override int DataCodingFormat => _model.DataCodingFormat;

    protected override DateTime? Run => S100IssueTime.Parse(_model.IssueDate, _model.IssueTime);

    protected override string WindowOutsideGridReason =>
        "windowed time queries are only supported for gridded S-111 (dcf=2); no in-bounds gridded dataset was found";

    protected override GridGeometry Geometry(SurfaceCurrentCoverage c) => new(
        c.OriginLatitude, c.OriginLongitude,
        c.SpacingLatitudinal, c.SpacingLongitudinal,
        c.NumPointsLatitudinal, c.NumPointsLongitudinal);

    protected override DateTime TimePoint(SurfaceCurrentCoverage c) => c.TimePoint;

    protected override SampledValue? ReadNode(SurfaceCurrentCoverage c, GridNode node, DateTimeOffset? requestedTime)
    {
        var value = c.Values[node.Index];
        // S-111 §10.2.5 fill value for both speed and direction is -9999f.
        return value.Speed == S111CoverageSource.FillValue
            ? null
            : new SurfaceCurrentSample(
                value.Speed * KnotsToMetresPerSecond,
                value.Speed,
                value.Direction,
                GridNodes.Utc(c.TimePoint),
                requestedTime,
                node.Row,
                node.Col,
                node.Latitude,
                node.Longitude);
    }

    protected override SampledValue Fallback(DateTime sampleTime, DateTimeOffset requestedTime, GridNode node) =>
        new SurfaceCurrentSample(
            double.NaN, double.NaN, double.NaN,
            sampleTime, requestedTime, 0, 0, node.Latitude, node.Longitude);
}

/// <summary>
/// Samples an S-111 station-series (dcf8) dataset (S-111 Edition 2.0.0
/// §10.2.3 / §10.2.7): every station is a site, ranked by great-circle
/// distance with no cap, read at the step nearest the time. When several
/// runs report the same station, the one whose series covers the requested
/// time wins. Range and series time queries are not supported.
/// </summary>
internal sealed class S111StationSampler : ICoverageSampler
{
    private readonly S111StationSeriesDataset _dataset;

    public S111StationSampler(S111StationSeriesDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = S100IssueTime.Parse(_dataset.IssueDate, _dataset.IssueTime);
        return new CoverageSiteSearch(_dataset.Stations
            .Select(s => (CoverageSampleSite)new Site(
                s,
                GridNodes.GreatCircleMetres(query.Latitude, query.Longitude, s.Latitude, s.Longitude),
                run))
            .ToList());
    }

    private sealed class Site(SurfaceCurrentStation station, double distanceMetres, DateTime? run) : CoverageSampleSite(
        CoverageSiteKind.Station,
        distanceMetres,
        run,
        CoverageStepWindow.FromStation(station.StartTime, station.EndTime, station.TimeRecordInterval, station.SampleTimes))
    {
        public override CoverageReading Read(DateTimeOffset? requestedTime)
        {
            var idx = requestedTime is { } requested ? station.NearestTimeIndex(requested.UtcDateTime) : 0;
            var sampleTime = requestedTime is null ? station.StartTime : station.TimeAt(idx);
            var speed = station.SpeedsKnots[idx];
            return new CoverageReading.Value(new SurfaceCurrentStationSample(
                station.Identifier,
                Rank,
                speed * S111GridSampler.KnotsToMetresPerSecond,
                speed,
                station.DirectionsDegreesTrue[idx],
                GridNodes.Utc(sampleTime),
                requestedTime,
                station.Latitude,
                station.Longitude));
        }
    }
}
