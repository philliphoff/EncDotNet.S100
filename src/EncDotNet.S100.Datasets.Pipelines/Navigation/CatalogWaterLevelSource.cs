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
/// <see cref="PassageDataSourceOptions.MaxStationDistanceMeters"/>, nearest
/// first. Grids win over stations as in <c>sample_coverage</c>.
/// </summary>
/// <remarks>
/// A grid point is found as in <c>sample_coverage</c>: the point must lie
/// inside the grid's node extent, and the nearest node is read (S-104 Ed
/// 2.0.0 §10.2). Swapped axis order, other geographic CRSs, projected CRSs
/// and the 0…360° longitude frame are absorbed at lookup by
/// <see cref="PassageGrid"/>. Heights are taken as metres above the vertical datum
/// (vertical CS 6499, height up; S-104 Ed 2.0.0 Table 5-1). The fill value
/// −9999 is NoData.
/// </remarks>
internal sealed class CatalogWaterLevelSource : IWaterLevelSource
{
    private readonly List<GridCandidate> _grids = new();
    private readonly List<(string DatasetId, S104StationSeriesDataset Dataset)> _stations = new();
    private readonly double _maxStationDistance;

    public CatalogWaterLevelSource(IReadOnlyList<LoadedDataset> datasets, ICrsTransformFactory transforms, PassageDataSourceOptions options)
    {
        _maxStationDistance = options.MaxStationDistanceMeters;
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
    }

    public IReadOnlyList<IWaterLevelSeries> Locate(double latitude, double longitude)
    {
        var result = new List<IWaterLevelSeries>();
        foreach (var grid in _grids)
        {
            if (GridSeries.TryCreate(grid, latitude, longitude) is { } series)
            {
                result.Add(series);
            }
        }

        var stations = new List<StationSeries>();
        foreach (var (datasetId, dataset) in _stations)
        {
            WaterLevelStation? nearest = null;
            var best = double.PositiveInfinity;
            foreach (var station in dataset.Stations)
            {
                var d = PassageGeodesy.GreatCircleDistanceMeters(
                    new GeoPosition(latitude, longitude), new GeoPosition(station.Latitude, station.Longitude));
                if (d < best && station.NumberOfTimes > 0)
                {
                    best = d;
                    nearest = station;
                }
            }

            if (nearest is not null && best <= _maxStationDistance)
            {
                stations.Add(new StationSeries(datasetId, dataset.VerticalDatum, nearest, best));
            }
        }

        stations.Sort((a, b) => a.Source.DistanceMeters!.Value.CompareTo(b.Source.DistanceMeters!.Value));
        result.AddRange(stations);
        return result;
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
            Interpolate(_steps.Count, i => DateTime.SpecifyKind(_steps[i].TimePoint, DateTimeKind.Utc), i => _steps[i].Values[_index].Height, utc);
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
            Interpolate(_station.NumberOfTimes, i => DateTime.SpecifyKind(_station.TimeAt(i), DateTimeKind.Utc), i => _station.Heights[i], utc);
    }

    /// <summary>
    /// Linear interpolation between the steps either side of <paramref name="utc"/>.
    /// The nearest step must hold a value; when the other is NoData, the
    /// nearest value is used as is.
    /// </summary>
    private static WaterLevelReading? Interpolate(int count, Func<int, DateTime> timeAt, Func<int, float> heightAt, DateTime utc)
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
            return new WaterLevelReading(hNearest, timeAt(nearest));
        }

        var f = Math.Clamp((utc - tBefore).TotalSeconds / (tAfter - tBefore).TotalSeconds, 0, 1);
        return new WaterLevelReading(hBefore + f * (hAfter - hBefore), timeAt(nearest));
    }

    private static bool IsValue(float h) => h != S104CoverageSource.FillValue && float.IsFinite(h);
}
