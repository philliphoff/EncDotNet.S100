using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Navigation;

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
/// 2.0.0 §10.2). Heights are taken as metres above the vertical datum
/// (vertical CS 6499, height up; S-104 Ed 2.0.0 Table 5-1). The fill value
/// −9999 is NoData.
/// </remarks>
internal sealed class CatalogWaterLevelSource : IWaterLevelSource
{
    private readonly List<GridCandidate> _grids = new();
    private readonly List<(string DatasetId, S104StationSeriesDataset Dataset)> _stations = new();
    private readonly double _maxStationDistance;

    public CatalogWaterLevelSource(IReadOnlyList<LoadedDataset> datasets, PassageDataSourceOptions options)
    {
        _maxStationDistance = options.MaxStationDistanceMeters;
        foreach (var dataset in datasets)
        {
            switch (dataset.Data)
            {
                case S104CoverageData grid when grid.Source.Dataset is { DataCodingFormat: 2, Coverages.Count: > 0 } model:
                    var first = model.Coverages[0];
                    _grids.Add(new GridCandidate(
                        dataset.Id.Value,
                        model,
                        Math.Min(Math.Abs(first.SpacingLatitudinal), Math.Abs(first.SpacingLongitudinal)),
                        S100IssueTime.Parse(model.IssueDate, model.IssueTime)));
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

    private sealed record GridCandidate(string DatasetId, S104Dataset Model, double Spacing, DateTime? Issued);

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

        public static GridSeries? TryCreate(GridCandidate grid, double lat, double lon)
        {
            var cov = grid.Model.Coverages[0];
            var maxLat = cov.OriginLatitude + (cov.NumPointsLatitudinal - 1) * cov.SpacingLatitudinal;
            var maxLon = cov.OriginLongitude + (cov.NumPointsLongitudinal - 1) * cov.SpacingLongitudinal;
            if (lat < Math.Min(cov.OriginLatitude, maxLat) || lat > Math.Max(cov.OriginLatitude, maxLat)
                || lon < Math.Min(cov.OriginLongitude, maxLon) || lon > Math.Max(cov.OriginLongitude, maxLon))
            {
                return null;
            }

            var row = Math.Clamp((int)Math.Round((lat - cov.OriginLatitude) / cov.SpacingLatitudinal), 0, cov.NumPointsLatitudinal - 1);
            var col = Math.Clamp((int)Math.Round((lon - cov.OriginLongitude) / cov.SpacingLongitudinal), 0, cov.NumPointsLongitudinal - 1);
            return new GridSeries(grid, row, col, row * cov.NumPointsLongitudinal + col);
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
