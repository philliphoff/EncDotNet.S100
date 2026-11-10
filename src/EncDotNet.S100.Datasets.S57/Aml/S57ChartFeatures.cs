using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S57;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Reads the features of an S-57 chart as they are, without translating them
/// to S-101: each feature keeps its own object class and attributes, named by
/// the cell's object catalogue, with its geometry in WGS-84 degrees.
/// </summary>
/// <remarks>
/// <para>
/// This is how AML cells are read: their AML-only classes have no S-101
/// counterpart, so the S-57 → S-101 translation would drop them. The features
/// are what an AML portrayal draws and what a pick finds.
/// </para>
/// <para>
/// A feature's <see cref="Feature.Id"/> is its record id (<c>FRID</c>
/// <c>RCID</c>), its <see cref="Feature.FeatureType"/> the object class acronym
/// (<c>M_COVR</c>, <c>atsctl</c>), and its <see cref="Feature.Attributes"/> are
/// keyed by attribute acronym with the values as encoded. A code the catalogue
/// doesn't have keeps its number. Points come from their node (several for a
/// sounding node), lines from their edges joined end to end, and areas from
/// their boundary edges chained into rings. Edges given as arcs arrive already
/// densified by the reader. Meta features without geometry are kept with
/// <see cref="GeometryType.None"/>.
/// </para>
/// </remarks>
public static class S57ChartFeatures
{
    /// <summary>The <c>M_COVR</c> object class acronym.</summary>
    public const string CoverageClass = "M_COVR";

    /// <summary>The <c>CATCOV</c> attribute acronym.</summary>
    public const string CategoryOfCoverage = "CATCOV";

    /// <summary>Reads every feature of <paramref name="chart"/>.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="catalogue">
    /// The catalogue that names its codes; <see langword="null"/> uses
    /// <see cref="S57Chart.GetObjectCatalogue"/>.
    /// </param>
    /// <returns>The features, in record order: points, lines, areas, then meta features.</returns>
    public static IReadOnlyList<Feature> Read(S57Chart chart, S57ObjectCatalogue? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        catalogue ??= chart.GetObjectCatalogue();
        var reader = new Reader(chart, catalogue);

        var features = new List<Feature>(chart.AllFeatures.Count);
        foreach (var point in chart.PointFeatures)
            features.Add(reader.Point(point));
        foreach (var line in chart.LineFeatures)
            features.Add(reader.Line(line));
        foreach (var area in chart.AreaFeatures)
            features.Add(reader.Area(area));
        foreach (var meta in chart.MetaFeatures)
            features.Add(reader.Meta(meta));

        return features;
    }

    /// <summary>
    /// The data coverage of a cell: its <c>M_COVR</c> areas with
    /// <c>CATCOV</c> = 1 (coverage available), less any with
    /// <c>CATCOV</c> = 2 inside them. When there are none, the bounding box of
    /// every position in <paramref name="features"/>.
    /// </summary>
    /// <param name="features">The cell's features, from <see cref="Read"/>.</param>
    /// <param name="isBoundingBox"><see langword="true"/> when the coverage is the bounding-box fallback.</param>
    /// <returns>The coverage areas; empty when the cell has no positions at all.</returns>
    public static IReadOnlyList<CoverageArea> ResolveCoverage(IReadOnlyList<Feature> features, out bool isBoundingBox)
    {
        ArgumentNullException.ThrowIfNull(features);

        var areas = new List<CoverageArea>();
        foreach (var feature in features)
        {
            if (!string.Equals(feature.FeatureType, CoverageClass, StringComparison.Ordinal)
                || feature.GeometryType != GeometryType.Surface
                || feature.Coordinates.Count < 3)
            {
                continue;
            }

            var category = feature.Attributes.GetValueOrDefault(CategoryOfCoverage)?.ToString()?.Trim();
            if (category is "2")
                continue;

            if (feature.SurfaceParts.Count > 0)
            {
                foreach (var part in feature.SurfaceParts)
                    areas.Add(new CoverageArea { ExteriorRing = part.ExteriorRing, InteriorRings = part.InteriorRings });
            }
            else
            {
                areas.Add(new CoverageArea { ExteriorRing = feature.Coordinates, InteriorRings = feature.InteriorRings });
            }
        }

        if (areas.Count > 0)
        {
            isBoundingBox = false;
            return areas;
        }

        isBoundingBox = true;
        return BoundsOf(features) is { } box
            ?
            [
                new CoverageArea
                {
                    ExteriorRing =
                    [
                        new GeoPosition(box.South, box.West),
                        new GeoPosition(box.South, box.East),
                        new GeoPosition(box.North, box.East),
                        new GeoPosition(box.North, box.West),
                        new GeoPosition(box.South, box.West),
                    ],
                },
            ]
            : [];
    }

    private static (double South, double West, double North, double East)? BoundsOf(IEnumerable<Feature> features)
    {
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        var any = false;
        foreach (var feature in features)
        {
            foreach (var p in AllPositions(feature))
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
                any = true;
            }
        }

        return any ? (south, west, north, east) : null;
    }

    private static IEnumerable<GeoPosition> AllPositions(Feature feature)
    {
        foreach (var p in feature.Coordinates)
            yield return p;
        foreach (var curve in feature.Curves)
        {
            foreach (var p in curve)
                yield return p;
        }

        foreach (var part in feature.SurfaceParts)
        {
            foreach (var p in part.ExteriorRing)
                yield return p;
        }
    }

    private sealed class Reader(S57Chart chart, S57ObjectCatalogue catalogue)
    {
        private readonly Dictionary<int, string> _classNames = [];
        private readonly Dictionary<int, string> _attributeNames = [];

        public Feature Point(S57PointFeature feature)
        {
            var positions = new List<GeoPosition>();
            foreach (var pointer in feature.SpatialReferences)
            {
                if (chart.GetIsolatedNode(pointer.Name) is { } isolated)
                {
                    if (isolated.Position is { } position)
                        positions.Add(ToGeo(position.X, position.Y));
                    foreach (var sounding in isolated.Soundings)
                        positions.Add(ToGeo(sounding.X, sounding.Y));
                }
                else if (chart.GetConnectedNode(pointer.Name) is { } connected)
                {
                    positions.Add(ToGeo(connected.Position.X, connected.Position.Y));
                }
            }

            return Create(feature, positions.Count > 0 ? GeometryType.Point : GeometryType.None, positions);
        }

        public Feature Line(S57LineFeature feature)
        {
            var curves = new List<List<GeoPosition>>();
            List<GeoPosition>? current = null;
            foreach (var pointer in feature.EdgeReferences)
            {
                var edge = EdgePositions(pointer.Name, pointer.Orientation == S57Orientation.Reverse);
                if (edge.Count < 2)
                    continue;

                if (current is not null && current[^1] == edge[0])
                {
                    current.AddRange(edge.Skip(1));
                }
                else
                {
                    current = edge;
                    curves.Add(current);
                }
            }

            if (curves.Count == 0)
                return Create(feature, GeometryType.None, []);

            return new Feature
            {
                FeatureType = ClassName(feature.ObjectCode),
                Id = feature.RecordName.RecordId,
                GeometryType = GeometryType.Curve,
                Coordinates = curves[0],
                Curves = curves.Count > 1 ? curves : [],
                Attributes = Attributes(feature),
            };
        }

        public Feature Area(S57AreaFeature feature)
        {
            var exterior = Rings(feature.ExteriorEdgeReferences);
            var interior = Rings(feature.InteriorEdgeReferences);
            if (exterior.Count == 0)
                return Create(feature, GeometryType.None, []);

            // Each hole belongs to the first exterior ring that contains it.
            var parts = exterior.Select(ring => (Ring: ring, Holes: new List<IReadOnlyList<GeoPosition>>())).ToList();
            foreach (var hole in interior)
            {
                var owner = parts.FirstOrDefault(p => new CoverageArea { ExteriorRing = p.Ring }.Contains(hole[0].Latitude, hole[0].Longitude));
                (owner.Ring is null ? parts[0] : owner).Holes.Add(hole);
            }

            return new Feature
            {
                FeatureType = ClassName(feature.ObjectCode),
                Id = feature.RecordName.RecordId,
                GeometryType = GeometryType.Surface,
                Coordinates = parts[0].Ring,
                InteriorRings = parts[0].Holes,
                SurfaceParts = parts.Count > 1 ? parts.Select(p => new SurfacePart(p.Ring, p.Holes)).ToArray() : [],
                Attributes = Attributes(feature),
            };
        }

        public Feature Meta(S57MetaFeature feature) => Create(feature, GeometryType.None, []);

        private Feature Create(S57TypedFeature feature, GeometryType type, IReadOnlyList<GeoPosition> coordinates) => new()
        {
            FeatureType = ClassName(feature.ObjectCode),
            Id = feature.RecordName.RecordId,
            GeometryType = type,
            Coordinates = coordinates,
            Attributes = Attributes(feature),
        };

        private List<IReadOnlyList<GeoPosition>> Rings(IReadOnlyList<S57EdgeReference> references)
        {
            var pieces = new List<List<GeoPosition>>();
            foreach (var reference in references)
            {
                var edge = EdgePositions(reference.EdgeName, reference.Orientation == S57Orientation.Reverse);
                if (edge.Count >= 2)
                    pieces.Add(edge);
            }

            // Join the pieces end to end, reversing one where needed, until each
            // ring closes. S-57 doesn't promise the edges of a ring are listed in
            // order, nor that the holes are listed apart.
            var rings = new List<IReadOnlyList<GeoPosition>>();
            var used = new bool[pieces.Count];
            for (var seed = 0; seed < pieces.Count; seed++)
            {
                if (used[seed])
                    continue;

                used[seed] = true;
                var ring = new List<GeoPosition>(pieces[seed]);
                var extended = true;
                while (ring[0] != ring[^1] && extended)
                {
                    extended = false;
                    for (var i = 0; i < pieces.Count; i++)
                    {
                        if (used[i])
                            continue;

                        var piece = pieces[i];
                        if (piece[0] == ring[^1])
                        {
                            ring.AddRange(piece.Skip(1));
                        }
                        else if (piece[^1] == ring[^1])
                        {
                            ring.AddRange(Enumerable.Reverse(piece).Skip(1));
                        }
                        else
                        {
                            continue;
                        }

                        used[i] = true;
                        extended = true;
                        break;
                    }
                }

                if (ring[0] != ring[^1])
                    ring.Add(ring[0]);
                if (ring.Count >= 4)
                    rings.Add(ring);
            }

            return rings;
        }

        private List<GeoPosition> EdgePositions(S57RecordName name, bool reverse)
        {
            var positions = new List<GeoPosition>();
            if (chart.GetEdge(name) is not { } edge)
                return positions;

            if (edge.BeginningNode is { } begin && chart.GetConnectedNode(begin) is { } beginNode)
                positions.Add(ToGeo(beginNode.Position.X, beginNode.Position.Y));
            foreach (var point in edge.IntermediatePoints)
                positions.Add(ToGeo(point.X, point.Y));
            if (edge.EndNode is { } end && chart.GetConnectedNode(end) is { } endNode)
                positions.Add(ToGeo(endNode.Position.X, endNode.Position.Y));

            if (reverse)
                positions.Reverse();
            return positions;
        }

        private GeoPosition ToGeo(int x, int y)
        {
            var longitude = chart.ToDecimalDegrees(x);
            var latitude = chart.ToDecimalDegrees(y);
            return new GeoPosition(latitude, longitude);
        }

        private Dictionary<string, object?> Attributes(S57TypedFeature feature)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var value in feature.Attributes)
                attributes.TryAdd(AttributeName(value.AttributeCode), value.Value);
            foreach (var value in feature.NationalAttributes)
                attributes.TryAdd(AttributeName(value.AttributeCode), value.Value);
            return attributes;
        }

        private string ClassName(S57ObjectCode code)
        {
            var key = (int)code;
            if (!_classNames.TryGetValue(key, out var name))
            {
                name = catalogue.TryGetObjectClass(key, out var definition) && definition is not null
                    ? definition.Acronym
                    : key.ToString(CultureInfo.InvariantCulture);
                _classNames[key] = name;
            }

            return name;
        }

        private string AttributeName(int code)
        {
            if (!_attributeNames.TryGetValue(code, out var name))
            {
                name = catalogue.TryGetAttribute(code, out var definition) && definition is not null
                    ? definition.Acronym
                    : code.ToString(CultureInfo.InvariantCulture);
                _attributeNames[code] = name;
            }

            return name;
        }
    }
}
