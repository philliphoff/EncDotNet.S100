using EncDotNet.S57;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Union;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>A topology problem <see cref="S57TopologyCheck"/> found in an S-57 dataset.</summary>
/// <param name="Kind">
/// What is wrong: <c>edge-node</c> (an edge doesn't begin and end at
/// connected nodes), <c>dangling-pointer</c> (a pointer names no vector
/// record), <c>open-ring</c> (an area's edges don't close),
/// <c>skin-overlap</c> or <c>skin-gap</c> (Group 1 areas overlap, or leave
/// coverage uncovered), <c>coverage-overlap</c> or <c>coverage-gap</c>
/// (<c>M_COVR</c> objects overlap, or don't tile their bounding box).
/// </param>
/// <param name="Detail">The records involved.</param>
public sealed record S57TopologyIssue(string Kind, string Detail);

/// <summary>
/// Checks the chain-node topology of an S-57 dataset as
/// <see cref="S101ToS57Translator"/> builds it: every edge ends at connected
/// nodes, every area closes, the skin of the earth (Group 1) covers the
/// coverage without gaps or overlaps, and <c>M_COVR</c> tiles its bounding
/// box (S-57 Appendix B.1 §3.5, S-65 Annex C §2.5.1.1 and §3.5).
/// </summary>
/// <remarks>
/// A consistency check, not S-58 validation: it covers the rules a converter
/// is responsible for. Areas are compared to a tolerance of
/// <see cref="AreaTolerance"/> of the dataset's extent, so rounding to the
/// coordinate grid is not reported.
/// </remarks>
public static class S57TopologyCheck
{
    private const int Edge = 130;
    private const int ConnectedNode = 120;
    private const int MCovr = 302;
    private const int CatCov = 18;

    /// <summary>The fraction of the dataset's extent below which an area difference is ignored.</summary>
    public const double AreaTolerance = 1e-7;

    /// <summary>Checks <paramref name="document"/> and returns every issue found.</summary>
    public static IReadOnlyList<S57TopologyIssue> Check(S57Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<S57TopologyIssue>();
        var vectors = new Dictionary<(int, int), S57VectorRecord>();
        foreach (var v in document.VectorRecords)
            vectors[(v.RecordName.RecordNameCode, v.RecordName.RecordId)] = v;

        uint next = 1;
        var graph = new S57EdgeGraph(() => next++);
        foreach (var v in document.VectorRecords)
        {
            if (v.RecordName.RecordNameCode == ConnectedNode && v.Coordinates2D.Count > 0)
                graph.AddNode((uint)v.RecordName.RecordId, v.Coordinates2D[0].X, v.Coordinates2D[0].Y);
        }
        foreach (var v in document.VectorRecords)
        {
            if (v.RecordName.RecordNameCode != Edge) continue;
            if (!v.VectorPointers.Any(p => (int)p.Topology == 1) || !v.VectorPointers.Any(p => (int)p.Topology == 2))
            {
                issues.Add(new("edge-node", $"edge {v.RecordName.RecordId}"));
                continue;
            }
            var begin = v.VectorPointers.First(p => (int)p.Topology == 1);
            var end = v.VectorPointers.First(p => (int)p.Topology == 2);
            if (begin.Name.RecordNameCode != ConnectedNode || end.Name.RecordNameCode != ConnectedNode
                || !graph.Nodes.ContainsKey((uint)begin.Name.RecordId) || !graph.Nodes.ContainsKey((uint)end.Name.RecordId))
            {
                issues.Add(new("edge-node", $"edge {v.RecordName.RecordId}"));
                continue;
            }
            graph.Edges[(uint)v.RecordName.RecordId] = new S57EdgeGraph.Edge(
                (uint)begin.Name.RecordId, (uint)end.Name.RecordId, v.Coordinates2D.Select(c => (c.X, c.Y)).ToArray());
        }

        var areas = new List<(S57FeatureRecord Feature, Geometry Area)>();
        foreach (var feature in document.FeatureRecords)
        {
            foreach (var p in feature.SpatialPointers)
            {
                if (!vectors.ContainsKey((p.Name.RecordNameCode, p.Name.RecordId)))
                    issues.Add(new("dangling-pointer", $"{Describe(feature)} → {p.Name.RecordNameCode}/{p.Name.RecordId}"));
            }
            if ((int)feature.Primitive != 3) continue;
            if (!Closes(graph, feature.SpatialPointers))
            {
                issues.Add(new("open-ring", Describe(feature)));
                continue;
            }
            if (graph.Area(feature.SpatialPointers) is { } area)
                areas.Add((feature, area));
        }
        if (areas.Count == 0) return issues;

        var extent = new Envelope();
        foreach (var (_, area) in areas) extent.ExpandToInclude(area.EnvelopeInternal);
        var tolerance = Math.Max(1, extent.Area * AreaTolerance);

        var skin = areas.Where(a => (int)a.Feature.Group == 1).ToList();
        var coverage = areas.Where(a => (int)a.Feature.ObjectCode == MCovr).ToList();
        var covered = coverage.Where(a => Attribute(a.Feature, CatCov) is null or "1").Select(a => a.Area).ToList();

        var skinUnion = Overlaps(skin, "skin-overlap", tolerance, issues);
        if (skinUnion is not null && covered.Count > 0)
        {
            var gap = CascadedPolygonUnion.Union(covered).Difference(skinUnion);
            if (gap.Area > tolerance)
                issues.Add(new("skin-gap", $"{gap.Area:0} square units of coverage outside the skin of the earth"));
        }

        var coverageUnion = Overlaps(coverage, "coverage-overlap", tolerance, issues);
        if (coverageUnion is not null)
        {
            var box = coverageUnion.Envelope;
            if (box.Area - coverageUnion.Area > tolerance)
                issues.Add(new("coverage-gap", $"{box.Area - coverageUnion.Area:0} square units of the bounding box without M_COVR"));
        }
        return issues;
    }

    // Each node an area's edges reach must be reached an even number of
    // times, or a ring is open.
    private static bool Closes(S57EdgeGraph graph, IEnumerable<S57SpatialPointer> pointers)
    {
        var degree = new Dictionary<uint, int>();
        foreach (var p in pointers)
        {
            if (p.Name.RecordNameCode != Edge || !graph.Edges.TryGetValue((uint)p.Name.RecordId, out var e)) continue;
            degree[e.Begin] = degree.GetValueOrDefault(e.Begin) + 1;
            degree[e.End] = degree.GetValueOrDefault(e.End) + 1;
        }
        return degree.Count > 0 && degree.Values.All(d => d % 2 == 0);
    }

    // Reports the pairs of areas that overlap and returns their union. The
    // pairs are only looked for when the areas add up to more than the union.
    private static Geometry? Overlaps(List<(S57FeatureRecord Feature, Geometry Area)> areas, string kind, double tolerance, List<S57TopologyIssue> issues)
    {
        if (areas.Count == 0) return null;
        var union = CascadedPolygonUnion.Union(areas.Select(a => a.Area).ToList());
        if (areas.Sum(a => a.Area.Area) - union.Area <= tolerance) return union;

        var index = new STRtree<int>();
        for (int i = 0; i < areas.Count; i++) index.Insert(areas[i].Area.EnvelopeInternal, i);
        for (int i = 0; i < areas.Count; i++)
        {
            foreach (var j in index.Query(areas[i].Area.EnvelopeInternal))
            {
                if (j <= i) continue;
                var overlap = areas[i].Area.Intersection(areas[j].Area).Area;
                if (overlap > tolerance)
                    issues.Add(new(kind, $"{Describe(areas[i].Feature)} and {Describe(areas[j].Feature)}"));
            }
        }
        return union;
    }

    private static string? Attribute(S57FeatureRecord feature, int attl)
        => feature.Attributes.Where(a => a.AttributeCode == attl).Select(a => a.Value?.Trim()).FirstOrDefault();

    private static string Describe(S57FeatureRecord feature)
    {
        var objl = (int)feature.ObjectCode;
        var name = S57UnknownObject.Acronym(objl) ?? objl.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{name} {feature.RecordName.AgencyCode}/{feature.RecordName.FeatureId}/{feature.RecordName.FeatureSubdivision}";
    }
}
