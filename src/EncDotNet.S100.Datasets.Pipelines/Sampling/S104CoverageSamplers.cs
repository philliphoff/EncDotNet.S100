using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S104;

namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>
/// Samples an S-104 regular-grid (dcf2) water level: height and trend at the
/// node nearest the point and the step nearest the time. Shared by
/// <see cref="S104DatasetProcessor"/> and the catalog's
/// <see cref="Catalog.S104CoverageData"/>.
/// </summary>
internal sealed class S104GridSampler : TimedGridSampler<WaterLevelCoverage>
{
    /// <summary>The product sampled.</summary>
    public const string Spec = "S-104";

    private readonly S104Dataset _model;

    public S104GridSampler(S104CoverageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _model = source.Dataset;
    }

    protected override IReadOnlyList<WaterLevelCoverage> Coverages => _model.Coverages;

    protected override int? HorizontalCrs => _model.HorizontalCRS;

    protected override AxisOrderAssessment? AxisOrder => _model.AxisOrder;

    protected override int DataCodingFormat => _model.DataCodingFormat;

    protected override DateTime? Run => S100IssueTime.Parse(_model.IssueDate, _model.IssueTime);

    protected override string WindowOutsideGridReason =>
        "windowed time queries are only supported for gridded S-104 (dcf=2); no in-bounds gridded dataset was found";

    protected override GridGeometry Geometry(WaterLevelCoverage c) => new(
        c.OriginLatitude, c.OriginLongitude,
        c.SpacingLatitudinal, c.SpacingLongitudinal,
        c.NumPointsLatitudinal, c.NumPointsLongitudinal);

    protected override DateTime TimePoint(WaterLevelCoverage c) => c.TimePoint;

    protected override SampledValue? ReadNode(WaterLevelCoverage c, GridNode node, DateTimeOffset? requestedTime)
    {
        var value = c.Values[node.Index];
        return value.Height == S104CoverageSource.FillValue
            ? null
            : new WaterLevelSample(
                value.Height,
                S104Trend.Decode(value.Trend),
                GridNodes.Utc(c.TimePoint),
                requestedTime,
                node.Row,
                node.Col,
                node.Latitude,
                node.Longitude);
    }

    protected override SampledValue Fallback(DateTime sampleTime, DateTimeOffset requestedTime, GridNode node) =>
        new WaterLevelSample(double.NaN, "unknown", sampleTime, requestedTime, 0, 0, node.Latitude, node.Longitude);
}

/// <summary>
/// Samples an S-104 station-series (dcf8) dataset (S-104 Edition 2.0.0
/// §10.2.3 / §10.2.7): every station is a site, ranked by great-circle
/// distance with no cap, read at the step nearest the time. When several
/// runs report the same station, the one whose series covers the requested
/// time wins. Range and series time queries are not supported.
/// </summary>
internal sealed class S104StationSampler : ICoverageSampler
{
    private readonly S104StationSeriesDataset _dataset;

    public S104StationSampler(S104StationSeriesDataset dataset)
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

    private sealed class Site(WaterLevelStation station, double distanceMetres, DateTime? run) : CoverageSampleSite(
        CoverageSiteKind.Station,
        distanceMetres,
        run,
        CoverageStepWindow.FromStation(station.StartTime, station.EndTime, station.TimeRecordInterval, station.SampleTimes))
    {
        public override CoverageReading Read(DateTimeOffset? requestedTime)
        {
            var idx = requestedTime is { } requested ? station.NearestTimeIndex(requested.UtcDateTime) : 0;
            var sampleTime = requestedTime is null ? station.StartTime : station.TimeAt(idx);
            return new CoverageReading.Value(new WaterLevelStationSample(
                station.Identifier,
                Rank,
                station.Heights[idx],
                S104Trend.Decode(station.Trends[idx]),
                GridNodes.Utc(sampleTime),
                requestedTime,
                station.Latitude,
                station.Longitude));
        }
    }
}

internal static class S104Trend
{
    /// <summary>
    /// Decodes the S-104 waterLevelTrend enumeration (S-104 Edition 2.0.0
    /// §10.2.2 Table 10-3). Raw values outside the spec-defined set are
    /// returned as their integer string so callers can still surface the
    /// raw payload.
    /// </summary>
    public static string Decode(byte trend) => trend switch
    {
        0 => "unknown",
        1 => "decreasing",
        2 => "increasing",
        3 => "steady",
        _ => trend.ToString(CultureInfo.InvariantCulture),
    };
}
