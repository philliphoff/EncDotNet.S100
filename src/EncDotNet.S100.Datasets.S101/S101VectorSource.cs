using EncDotNet.S57;
using EncDotNet.S57.Charts;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Adapts an <see cref="S101Dataset"/> to the pipeline's <see cref="IVectorSource"/>
/// interface, projecting S-57 feature records into the generic feature model.
/// </summary>
public sealed class S101VectorSource : IVectorSource
{
    private readonly S101Dataset _dataset;

    public S101VectorSource(S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    public VectorMetadata Metadata => new()
    {
        ProductSpec = "S-101",
        Extent = ComputeExtent(),
        HorizontalCRS = "EPSG:4326",
        CompilationScaleDenominator = _dataset.CompilationScale,
    };

    public IReadOnlyList<Feature> GetFeatures(BoundingBox? extent = null)
    {
        var chart = _dataset.Chart;
        int comf = chart.CoordinateMultiplicationFactor;
        var features = new List<Feature>();

        foreach (var pf in chart.PointFeatures)
        {
            var coords = ResolvePointGeometry(pf, chart, comf);
            if (coords.Count == 0) continue;
            if (extent is not null && !IntersectsExtent(coords, extent)) continue;

            features.Add(new Feature
            {
                Id = pf.RecordName.RecordId,
                FeatureType = pf.ObjectCode.ToString(),
                GeometryType = GeometryType.Point,
                Coordinates = coords,
                Attributes = ExtractAttributes(pf),
            });
        }

        foreach (var lf in chart.LineFeatures)
        {
            var coords = ResolveLineGeometry(lf, chart, comf);
            if (coords.Count == 0) continue;
            if (extent is not null && !IntersectsExtent(coords, extent)) continue;

            features.Add(new Feature
            {
                Id = lf.RecordName.RecordId,
                FeatureType = lf.ObjectCode.ToString(),
                GeometryType = GeometryType.Curve,
                Coordinates = coords,
                Attributes = ExtractAttributes(lf),
            });
        }

        foreach (var af in chart.AreaFeatures)
        {
            var coords = ResolveAreaGeometry(af, chart, comf);
            if (coords.Count == 0) continue;
            if (extent is not null && !IntersectsExtent(coords, extent)) continue;

            features.Add(new Feature
            {
                Id = af.RecordName.RecordId,
                FeatureType = af.ObjectCode.ToString(),
                GeometryType = GeometryType.Surface,
                Coordinates = coords,
                Attributes = ExtractAttributes(af),
            });
        }

        return features;
    }

    // ── Geometry resolution ────────────────────────────────────────────

    private static IReadOnlyList<(double Latitude, double Longitude)> ResolvePointGeometry(
        S57PointFeature feature, S57Chart chart, int comf)
    {
        if (!feature.HasSpatialReferences) return [];

        var results = new List<(double, double)>();

        foreach (var spatialRef in feature.SpatialReferences)
        {
            if (chart.IsolatedNodes.TryGetValue(spatialRef.Name, out var isolated) && isolated.HasPosition)
            {
                var pos = isolated.Position!.Value;
                results.Add(ToLatLon(pos, comf));
            }
            else if (chart.ConnectedNodes.TryGetValue(spatialRef.Name, out var connected))
            {
                results.Add(ToLatLon(connected.Position, comf));
            }
        }

        return results;
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> ResolveLineGeometry(
        S57LineFeature feature, S57Chart chart, int comf)
    {
        if (!feature.HasEdgeReferences) return [];

        var coords = new List<(double, double)>();

        foreach (var edgeRef in feature.EdgeReferences)
        {
            if (!chart.Edges.TryGetValue(edgeRef.Name, out var edge)) continue;

            var edgeCoords = new List<(double, double)>();

            // Beginning node
            if (edge.HasBeginningNode && chart.ConnectedNodes.TryGetValue(edge.BeginningNode!.Value, out var beginNode))
            {
                edgeCoords.Add(ToLatLon(beginNode.Position, comf));
            }

            // Intermediate points
            if (edge.HasIntermediatePoints)
            {
                foreach (var pt in edge.IntermediatePoints)
                {
                    edgeCoords.Add(ToLatLon(pt, comf));
                }
                }

            // End node
            if (edge.HasEndNode && chart.ConnectedNodes.TryGetValue(edge.EndNode!.Value, out var endNode))
            {
                edgeCoords.Add(ToLatLon(endNode.Position, comf));
            }

            // Reverse if orientation is reversed
            if (edgeRef.Orientation == S57Orientation.Reverse)
            {
                edgeCoords.Reverse();
            }

            coords.AddRange(edgeCoords);
        }

        return coords;
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> ResolveAreaGeometry(
        S57AreaFeature feature, S57Chart chart, int comf)
    {
        // For IVectorSource, flatten exterior boundary edges into a coordinate list.
        // The IFeatureXmlSource provides richer ring-based geometry.
        if (!feature.HasExteriorEdgeReferences) return [];

        var coords = new List<(double, double)>();

        foreach (var edgeRef in feature.ExteriorEdgeReferences)
        {
            if (!chart.Edges.TryGetValue(edgeRef.EdgeName, out var edge)) continue;  

            var edgeCoords = new List<(double, double)>();

            if (edge.HasBeginningNode && chart.ConnectedNodes.TryGetValue(edge.BeginningNode!.Value, out var beginNode))
            {
                edgeCoords.Add(ToLatLon(beginNode.Position, comf));
            }

            if (edge.HasIntermediatePoints)
            {
                foreach (var pt in edge.IntermediatePoints)
                {
                    edgeCoords.Add(ToLatLon(pt, comf));
                }
            }

            if (edge.HasEndNode && chart.ConnectedNodes.TryGetValue(edge.EndNode!.Value, out var endNode))
            {
                edgeCoords.Add(ToLatLon(endNode.Position, comf));
            }

            if (edgeRef.Orientation == S57Orientation.Reverse)
            {
                edgeCoords.Reverse();
            }

            coords.AddRange(edgeCoords);
        }

        return coords;
    }

    // ── Attribute extraction ───────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> ExtractAttributes(S57TypedFeature feature)
    {
        var attributes = new Dictionary<string, object?>();

        if (feature.HasAttributes)
        {
            foreach (var attr in feature.Attributes)
            {
                attributes[attr.AttributeCode.ToString()] = attr.Value;
            }
        }

        return attributes;
    }

    // ── Coordinate conversion ──────────────────────────────────────────

    private static (double Latitude, double Longitude) ToLatLon(S57Coordinate2D coord, int comf)
    {
        double lat = coord.Y / (double)comf;
        double lon = coord.X / (double)comf;
        return (lat, lon);
    }

    // ── Extent computation ─────────────────────────────────────────────

    private BoundingBox ComputeExtent()
    {
        var chart = _dataset.Chart;
        int comf = chart.CoordinateMultiplicationFactor;

        double minLat = double.MaxValue, minLon = double.MaxValue;
        double maxLat = double.MinValue, maxLon = double.MinValue;
        bool hasCoords = false;

        void UpdateBounds(double lat, double lon)
        {
            hasCoords = true;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        foreach (var node in chart.ConnectedNodes.Values)
        {
            var (lat, lon) = ToLatLon(node.Position, comf);
            UpdateBounds(lat, lon);
        }

        foreach (var node in chart.IsolatedNodes.Values)
        {
            if (node.HasPosition)
            {
                var (lat, lon) = ToLatLon(node.Position!.Value, comf);
                UpdateBounds(lat, lon);
            }
        }

        if (!hasCoords)
        {
            return new BoundingBox(0, 0, 0, 0);
        }

        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }

    private static bool IntersectsExtent(
        IReadOnlyList<(double Latitude, double Longitude)> coords,
        BoundingBox extent)
    {
        foreach (var (lat, lon) in coords)
        {
            if (lat >= extent.SouthLatitude && lat <= extent.NorthLatitude
                && lon >= extent.WestLongitude && lon <= extent.EastLongitude)
            {
                return true;
            }
        }
        return false;
    }
}
