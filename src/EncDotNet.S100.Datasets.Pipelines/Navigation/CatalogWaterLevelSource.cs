using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Water level from loaded S-104 datasets: regular grids (dcf2) first, in
/// <see cref="WaterLevelPreference"/> order, then the nearest station of each
/// station series (dcf8) within
/// <see cref="RouteCheckSourceOptions.MaxStationDistanceMeters"/> by water
/// (<see cref="WaterReach"/>: the loaded charts' land blocks a station, tide
/// predictions design D19), nearest first. Grids win over stations as in
/// <c>sample_coverage</c>.
/// </summary>
/// <remarks>
/// A grid point is found as in <c>sample_coverage</c>: the point must lie
/// inside the grid's node extent, and the nearest node is read (S-104 Ed
/// 2.0.0 §10.2). Swapped axis order, other geographic CRSs, projected CRSs
/// and the 0…360° longitude frame are absorbed at lookup by
/// <see cref="PassageGrid"/>. Heights are taken as metres above the vertical datum
/// (vertical CS 6499, height up; S-104 Ed 2.0.0 Table 5-1). The fill value
/// −9999 is NoData. A reading's uncertainty is the S-104 <c>uncertainty</c>
/// at the nearest step, or the larger of the two bracketing steps' when the
/// height is interpolated (S-104 Ed 2.0.0 §10.2.2.4).
/// </remarks>
internal sealed class CatalogWaterLevelSource : IWaterLevelSource
{
    private readonly List<GridCandidate> _grids = new();
    private readonly List<(string DatasetId, S104StationSeriesDataset Dataset)> _stations = new();
    private readonly double _maxStationDistance;
    private readonly WaterReach _reach;

    public CatalogWaterLevelSource(IReadOnlyList<LoadedDataset> datasets, ICrsTransformFactory transforms, RouteCheckSourceOptions options, WaterReach reach)
    {
        _maxStationDistance = options.MaxStationDistanceMeters;
        _reach = reach;
        foreach (var dataset in datasets)
        {
            switch (dataset.Data)
            {
                case S104CoverageData grid when grid.Source.Dataset is { DataCodingFormat: 2, Coverages.Count: > 0 } model
                    && GridCandidate.TryCreate(dataset.Id.Value, model, transforms) is { } candidate:
                    _grids.Add(candidate);
                    break;
                case S104StationSeriesData stations:
                    _stations.Add((dataset.Id.Value, stations.Dataset));
                    break;
            }
        }

        _grids.Sort((a, b) => WaterLevelPreference.Compare(a.Spacing, a.Issued, a.DatasetId, b.Spacing, b.Issued, b.DatasetId));

        var listed = new List<WaterLevelSourceInfo>();
        var footprint = new List<CoverageArea>();
        foreach (var grid in _grids)
        {
            listed.Add(new WaterLevelSourceInfo(grid.DatasetId, WaterLevelSourceKind.Grid, grid.Model.VerticalDatum));
            footprint.Add(GridFootprint(grid.Grid));
        }

        foreach (var (datasetId, dataset) in _stations)
        {
            listed.Add(new WaterLevelSourceInfo(datasetId, WaterLevelSourceKind.Station, dataset.VerticalDatum));
            foreach (var station in dataset.Stations)
            {
                if (station.NumberOfTimes > 0)
                {
                    footprint.Add(StationFootprint(station, _maxStationDistance));
                }
            }
        }

        Datasets = listed;
        Footprint = footprint;
    }

    /// <inheritdoc />
    public IReadOnlyList<WaterLevelSourceInfo> Datasets { get; }

    /// <summary>
    /// Each grid's node extent and a disc of
    /// <see cref="RouteCheckSourceOptions.MaxStationDistanceMeters"/> around
    /// each station: where <see cref="Locate"/> can return a series. Masked
    /// nodes aren't cut out, and overlapping station discs aren't split into
    /// cells.
    /// </summary>
    public IReadOnlyList<CoverageArea> Footprint { get; }

    private static CoverageArea GridFootprint(PassageGrid grid)
    {
        // Walk the perimeter nodes so a projected grid's curved edges are kept.
        var ring = new List<GeoPosition>();
        int rows = grid.Rows - 1, cols = grid.Columns - 1;
        var rowStep = Math.Max(1, rows / 16);
        var colStep = Math.Max(1, cols / 16);
        for (var c = 0; c < cols; c += colStep) ring.Add(grid.NodeToWgs84(0, c));
        for (var r = 0; r < rows; r += rowStep) ring.Add(grid.NodeToWgs84(r, cols));
        for (var c = cols; c > 0; c -= colStep) ring.Add(grid.NodeToWgs84(rows, c));
        for (var r = rows; r > 0; r -= rowStep) ring.Add(grid.NodeToWgs84(r, 0));
        if (ring.Count == 0)
        {
            ring.Add(grid.NodeToWgs84(0, 0));
        }

        ring.Add(ring[0]);
        return new CoverageArea { ExteriorRing = ring };
    }

    private static CoverageArea StationFootprint(WaterLevelStation station, double radiusMeters)
    {
        const int vertices = 36;
        const double earthRadius = 6_371_008.8;
        var lat = station.Latitude * Math.PI / 180;
        var ring = new List<GeoPosition>(vertices + 1);
        for (var i = 0; i < vertices; i++)
        {
            var bearing = 2 * Math.PI * i / vertices;
            var dLat = radiusMeters * Math.Cos(bearing) / earthRadius;
            var dLon = radiusMeters * Math.Sin(bearing) / (earthRadius * Math.Max(Math.Cos(lat), 1e-6));
            ring.Add(new GeoPosition(station.Latitude + (dLat * 180 / Math.PI), station.Longitude + (dLon * 180 / Math.PI)));
        }

        ring.Add(ring[0]);
        return new CoverageArea { ExteriorRing = ring };
    }

    public IReadOnlyList<IWaterLevelSeries> Locate(GeoPosition position)
    {
        var (latitude, longitude) = (position.Latitude, position.Longitude);
        var result = new List<IWaterLevelSeries>();
        foreach (var grid in _grids)
        {
            if (GridSeries.TryCreate(grid, latitude, longitude) is { } series)
            {
                result.Add(series);
            }
        }

        var stations = new List<StationSeries>();
        var here = new GeoPosition(latitude, longitude);
        foreach (var (datasetId, dataset) in _stations)
        {
            if (NearestStation(dataset.Stations, here) is ({ } nearest, var distance))
            {
                stations.Add(new StationSeries(datasetId, dataset.VerticalDatum, nearest, distance));
            }
        }

        stations.Sort((a, b) => a.Source.DistanceMeters!.Value.CompareTo(b.Source.DistanceMeters!.Value));
        result.AddRange(stations);
        return result;
    }

    // The flat-earth distance below is within 0.6 % of the WGS-84 geodesic at
    // the station reach (10 km), so a station is a candidate when its flat
    // distance is within this factor of the nearest one's or of the reach.
    private const double CandidateBand = 1.02;
    private const double EarthRadiusMeters = 6_371_008.8;

    // The nearest station with values within the reach, by water (the WGS-84
    // geodesic where no charted land is in the way), or none. A cheap
    // flat-earth distance ranks the stations first, so only the few near the
    // nearest one (and none beyond the reach) need the dearer distance: a
    // chart adjustment asks this at every sampled node. The distance by water
    // is never shorter than the straight one, so once a station's flat
    // distance is past the best found, none further can win.
    private (WaterLevelStation? Station, double Distance) NearestStation(IReadOnlyList<WaterLevelStation> stations, GeoPosition here)
    {
        var cosLat = Math.Cos(here.Latitude * Math.PI / 180);
        var bestFlat = double.PositiveInfinity;
        foreach (var station in stations)
        {
            if (station.NumberOfTimes > 0)
            {
                bestFlat = Math.Min(bestFlat, FlatDistance(here, station, cosLat));
            }
        }

        if (bestFlat > _maxStationDistance * CandidateBand)
        {
            return (null, double.PositiveInfinity);
        }

        var candidates = new List<(WaterLevelStation Station, double Flat)>();
        var limit = _reach.ByWater ? _maxStationDistance * CandidateBand : bestFlat * CandidateBand;
        foreach (var station in stations)
        {
            if (station.NumberOfTimes > 0 && FlatDistance(here, station, cosLat) is var flat && flat <= limit)
            {
                candidates.Add((station, flat));
            }
        }

        candidates.Sort((a, b) => a.Flat.CompareTo(b.Flat));
        WaterLevelStation? nearest = null;
        var best = double.PositiveInfinity;
        foreach (var (station, flat) in candidates)
        {
            if (flat > best * CandidateBand)
            {
                break;
            }

            if (_reach.DistanceMeters(new GeoPosition(station.Latitude, station.Longitude), here) is { } d && d < best)
            {
                best = d;
                nearest = station;
            }
        }

        return best <= _maxStationDistance ? (nearest, best) : (null, double.PositiveInfinity);
    }

    private static double FlatDistance(GeoPosition here, WaterLevelStation station, double cosLat)
    {
        var dLat = (station.Latitude - here.Latitude) * Math.PI / 180;
        var dLon = station.Longitude - here.Longitude;
        dLon = (dLon + 540) % 360 - 180;
        dLon *= Math.PI / 180 * cosLat;
        return EarthRadiusMeters * Math.Sqrt((dLat * dLat) + (dLon * dLon));
    }

    private sealed record GridCandidate(string DatasetId, S104Dataset Model, PassageGrid Grid, double Spacing, DateTime? Issued)
    {
        public static GridCandidate? TryCreate(string datasetId, S104Dataset model, ICrsTransformFactory transforms)
        {
            var first = model.Coverages[0];
            try
            {
                var grid = PassageGrid.Create(
                    first.OriginLatitude, first.OriginLongitude,
                    first.SpacingLatitudinal, first.SpacingLongitudinal,
                    first.NumPointsLatitudinal, first.NumPointsLongitudinal,
                    model.HorizontalCRS,
                    model.AxisOrder,
                    transforms);
                return new GridCandidate(
                    datasetId,
                    model,
                    grid,
                    Math.Min(Math.Abs(first.SpacingLatitudinal), Math.Abs(first.SpacingLongitudinal)),
                    S100IssueTime.Parse(model.IssueDate, model.IssueTime));
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or OverflowException or ArgumentException)
            {
                // A CRS the factory can't reproject can't be sampled.
                return null;
            }
        }
    }

    private sealed class GridSeries : IWaterLevelSeries
    {
        private readonly IReadOnlyList<WaterLevelCoverage> _steps;
        private readonly int _index;

        private GridSeries(GridCandidate grid, int row, int col, int index)
        {
            _steps = grid.Model.Coverages;
            _index = index;
            Source = new WaterLevelSourceInfo(
                grid.DatasetId,
                WaterLevelSourceKind.Grid,
                grid.Model.VerticalDatum,
                GridCell: string.Create(CultureInfo.InvariantCulture, $"{row},{col}"));
            Start = DateTime.SpecifyKind(_steps[0].TimePoint, DateTimeKind.Utc);
            End = DateTime.SpecifyKind(_steps[^1].TimePoint, DateTimeKind.Utc);
        }

        public WaterLevelSourceInfo Source { get; }

        public DateTime Start { get; }

        public DateTime End { get; }

        public static GridSeries? TryCreate(GridCandidate candidate, double lat, double lon)
        {
            return candidate.Grid.FindNearestNode(lat, lon) is (var row, var col)
                ? new GridSeries(candidate, row, col, row * candidate.Grid.Columns + col)
                : null;
        }

        public WaterLevelReading? ValueAt(DateTime utc) =>
            Interpolate(_steps.Count, TimeAt, i => _steps[i].Values[_index].Height, i => _steps[i].UncertaintyAt(_index), utc);

        public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end) =>
            Steps(_steps.Count, TimeAt, start, end);

        private DateTime TimeAt(int i) => DateTime.SpecifyKind(_steps[i].TimePoint, DateTimeKind.Utc);
    }

    private sealed class StationSeries : IWaterLevelSeries
    {
        private readonly WaterLevelStation _station;

        public StationSeries(string datasetId, int? datum, WaterLevelStation station, double distance)
        {
            _station = station;
            Source = new WaterLevelSourceInfo(datasetId, WaterLevelSourceKind.Station, datum, station.Identifier, distance);
            Start = DateTime.SpecifyKind(station.TimeAt(0), DateTimeKind.Utc);
            End = DateTime.SpecifyKind(station.TimeAt(station.NumberOfTimes - 1), DateTimeKind.Utc);
        }

        public WaterLevelSourceInfo Source { get; }

        public DateTime Start { get; }

        public DateTime End { get; }

        public WaterLevelReading? ValueAt(DateTime utc) =>
            Interpolate(_station.NumberOfTimes, TimeAt, i => _station.Heights[i], _station.UncertaintyAt, utc);

        public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end) =>
            Steps(_station.NumberOfTimes, TimeAt, start, end);

        private DateTime TimeAt(int i) => DateTime.SpecifyKind(_station.TimeAt(i), DateTimeKind.Utc);
    }

    private static List<DateTime> Steps(int count, Func<int, DateTime> timeAt, DateTime start, DateTime end)
    {
        var result = new List<DateTime>();
        for (var i = 0; i < count; i++)
        {
            var t = timeAt(i);
            if (t >= start && t <= end)
            {
                result.Add(t);
            }
        }

        return result;
    }

    /// <summary>
    /// Linear interpolation between the steps either side of <paramref name="utc"/>.
    /// The nearest step must hold a value; when the other is NoData, the
    /// nearest value is used as is.
    /// </summary>
    private static WaterLevelReading? Interpolate(int count, Func<int, DateTime> timeAt, Func<int, float> heightAt, Func<int, double?> uncertaintyAt, DateTime utc)
    {
        if (count == 0)
        {
            return null;
        }

        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (timeAt(mid) < utc) lo = mid + 1;
            else hi = mid;
        }

        var after = Math.Min(lo, count - 1);
        var before = Math.Max(lo - 1, 0);
        var tBefore = timeAt(before);
        var tAfter = timeAt(after);
        var nearest = (utc - tBefore).Duration() <= (tAfter - utc).Duration() ? before : after;
        var hNearest = heightAt(nearest);
        if (!IsValue(hNearest))
        {
            return null;
        }

        var hBefore = heightAt(before);
        var hAfter = heightAt(after);
        if (before == after || !IsValue(hBefore) || !IsValue(hAfter) || tAfter <= tBefore)
        {
            return new WaterLevelReading(hNearest, timeAt(nearest)) { UncertaintyMeters = uncertaintyAt(nearest) };
        }

        var f = Math.Clamp((utc - tBefore).TotalSeconds / (tAfter - tBefore).TotalSeconds, 0, 1);
        var (uBefore, uAfter) = (uncertaintyAt(before), uncertaintyAt(after));
        return new WaterLevelReading(hBefore + f * (hAfter - hBefore), timeAt(nearest))
        {
            UncertaintyMeters = f is 0 or 1
                ? uncertaintyAt(nearest)
                : uBefore is { } a && uAfter is { } b ? Math.Max(a, b) : uBefore ?? uAfter,
        };
    }

    private static bool IsValue(float h) => h != S104CoverageSource.FillValue && float.IsFinite(h);
}
