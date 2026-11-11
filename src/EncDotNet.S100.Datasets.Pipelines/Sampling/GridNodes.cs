using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>A grid node: its row along latitude, column along longitude, index in the value array, and WGS-84 position.</summary>
internal readonly record struct GridNode(int Row, int Col, int Index, double Latitude, double Longitude);

/// <summary>The georeferencing of one regular-grid (dcf2) coverage.</summary>
internal readonly record struct GridGeometry(
    double OriginLatitude,
    double OriginLongitude,
    double SpacingLatitudinal,
    double SpacingLongitudinal,
    int NumPointsLatitudinal,
    int NumPointsLongitudinal);

/// <summary>Finds the grid node nearest a point in an S-104 or S-111 regular grid.</summary>
internal static class GridNodes
{
    // The grid node nearest the point, inside the node extent (S-104/S-111
    // Ed 2.0.0 §10.2). Found through the passage checks' lookup, so swapped
    // axis order, other geographic CRSs, projected CRSs and 0…360°
    // longitudes are read as if written without the quirk (#943). A CRS the
    // factory can't reproject finds nothing.
    public static GridNode? Find(
        GridGeometry grid,
        int? crs,
        AxisOrderAssessment? axisOrder,
        ICrsTransformFactory transforms,
        double latitude,
        double longitude)
    {
        PassageGrid passage;
        try
        {
            passage = PassageGrid.Create(
                grid.OriginLatitude, grid.OriginLongitude,
                grid.SpacingLatitudinal, grid.SpacingLongitudinal,
                grid.NumPointsLatitudinal, grid.NumPointsLongitudinal,
                crs, axisOrder, transforms);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or OverflowException or ArgumentException)
        {
            return null;
        }

        if (passage.FindNearestNode(latitude, longitude) is not (var row, var col))
        {
            return null;
        }

        var position = passage.NodeToWgs84(row, col);
        return new GridNode(row, col, (row * passage.Columns) + col, position.Latitude, position.Longitude);
    }

    // Spherical-earth great-circle distance, matching the accuracy bar
    // of other catalog tools.
    private const double EarthRadiusMetres = 6_371_000.0;

    /// <summary>Great-circle distance in metres between two WGS-84 points, on a spherical earth.</summary>
    public static double GreatCircleMetres(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var dPhi = (lat2 - lat1) * Math.PI / 180.0;
        var dLambda = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2)
              + Math.Cos(phi1) * Math.Cos(phi2)
              * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMetres * c;
    }

    /// <summary><paramref name="time"/> marked as UTC.</summary>
    public static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);
}
