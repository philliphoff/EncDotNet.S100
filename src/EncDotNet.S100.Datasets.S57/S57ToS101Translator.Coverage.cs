using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

// Data Coverage display scales (S-65 Annex B Ed 2.0.0 §2.2.6). S-101 Data
// Coverage combines the S-57 meta objects M_COVR and M_CSCL: each coverage-
// available M_COVR becomes Data Coverage whose optimum display scale is the
// cell's compilation scale (DSPM CSCL), and an M_CSCL "cookie-cuts" it into
// disjoint pieces that take their optimum display scale from its CSCALE.
public sealed partial class S57ToS101Translator
{
    private const ushort MCsclObjl = 301;          // M_CSCL (S-57 object class)
    private const ushort S57AttrCscale = 80;       // CSCALE — compilation scale
    private const string S101AttrOptimumDisplayScale = "optimumDisplayScale";
    private const string S101AttrMaximumDisplayScale = "maximumDisplayScale";

    // Annex B Table 2-3: the optimum display scales S-101 allows.
    private static readonly int[] OptimumDisplayScales =
        [10_000_000, 3_500_000, 1_500_000, 700_000, 350_000, 180_000, 90_000, 45_000, 22_000, 12_000, 8_000, 4_000, 3_000, 2_000, 1_000];

    /// <summary>
    /// The S-101 optimum display scale for an S-57 compilation scale
    /// (S-65 Annex B §2.2.6): the value itself when Table 2-3 lists it,
    /// otherwise "the next smallest scale denominator value" in that table.
    /// A compilation scale finer than the table's finest, 1:1 000, gets
    /// 1 000; a non-positive one gets <see langword="null"/>.
    /// </summary>
    internal static int? OptimumDisplayScale(int compilationScale)
    {
        if (compilationScale <= 0) return null;
        foreach (var scale in OptimumDisplayScales)
        {
            if (scale <= compilationScale) return scale;
        }
        return OptimumDisplayScales[^1];
    }

    private sealed partial class TranslationContext
    {
        // One Data Coverage feature to emit for a coverage-available M_COVR:
        // the boundary to use instead of the M_COVR's (null keeps it) and
        // the compilation scale its display scales come from.
        private readonly record struct CoveragePiece(IReadOnlyList<S57SpatialPointer>? Pointers, int CompilationScale);

        // Splits each coverage-available M_COVR (by feature index) into the
        // pieces of §2.2.6. Without an M_CSCL, or where one scale covers the
        // whole M_COVR, the M_COVR stays whole. When the faces the M_COVR and
        // M_CSCL edges bound don't add up to the M_COVR (edges that cross
        // without a shared node), the M_COVR stays whole at the cell's
        // compilation scale.
        private Dictionary<int, List<CoveragePiece>> PlanCoverage(IReadOnlyList<S57FeatureRecord> featureRecords)
        {
            var cellScale = _s57.DataSetParameters?.CompilationScale ?? 0;
            var plan = new Dictionary<int, List<CoveragePiece>>();
            var coverages = new List<int>();
            var scales = new List<(S57FeatureRecord Record, int Scale)>();
            for (int fi = 0; fi < featureRecords.Count; fi++)
            {
                var feat = featureRecords[fi];
                var objl = (ushort)(int)feat.ObjectCode;
                if (objl == MCovrObjl && (int)feat.Primitive == 3)
                {
                    coverages.Add(fi);
                    plan[fi] = [new CoveragePiece(null, cellScale)];
                }
                else if (objl == MCsclObjl && (int)feat.Primitive == 3
                    && feat.Attributes.Where(a => a.AttributeCode == S57AttrCscale).Select(a => a.Value).FirstOrDefault() is { } text
                    && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cscale)
                    && cscale > 0)
                {
                    scales.Add((feat, cscale));
                }
            }
            if (scales.Count == 0 || coverages.Count == 0) return plan;

            var graph = BuildEdgeGraph(coverages.Select(fi => featureRecords[fi]).Concat(scales.Select(s => s.Record)));
            var scaleAreas = scales
                .Select(s => (Area: graph.Area(s.Record.SpatialPointers), s.Scale))
                .Where(s => s.Area is not null)
                .ToList();

            foreach (var fi in coverages)
            {
                var coverage = featureRecords[fi];
                var area = graph.Area(coverage.SpatialPointers);
                if (area is null) continue;
                var overlapping = scaleAreas.Where(s => s.Area!.Intersects(area) && s.Area.Intersection(area).Area > 0).ToList();
                if (overlapping.Count == 0) continue;

                var edges = coverage.SpatialPointers
                    .Concat(scales.Where(s => overlapping.Any(o => o.Scale == s.Scale)).SelectMany(s => s.Record.SpatialPointers))
                    .Where(p => p.Name.RecordNameCode == S57RecordNameCodes.Edge && graph.Edges.ContainsKey((uint)p.Name.RecordId))
                    .Select(p => (uint)p.Name.RecordId);
                var faces = graph.Faces(edges).Where(f => area.Contains(f.Polygon.InteriorPoint)).ToList();
                if (Math.Abs(faces.Sum(f => f.Polygon.Area) - area.Area) > area.Area * 1e-6)
                {
                    _diagnostics?.RecordDataCoverageNotCut();
                    continue;
                }

                var groups = faces
                    .GroupBy(f => overlapping.Where(s => s.Area!.Contains(f.Polygon.InteriorPoint)).Select(s => (int?)s.Scale).Min() ?? cellScale)
                    .ToList();
                if (groups.Count == 1)
                {
                    plan[fi] = [new CoveragePiece(null, groups[0].Key)];
                    continue;
                }

                var pieces = new List<CoveragePiece>();
                foreach (var group in groups.OrderBy(g => g.Key == cellScale ? 0 : 1).ThenBy(g => g.Key))
                {
                    foreach (var pointers in graph.Boundary(group))
                        pieces.Add(new CoveragePiece(pointers, group.Key));
                }
                plan[fi] = pieces;
                if (_diagnostics is not null) _diagnostics.DataCoverageCuts++;
            }
            return plan;
        }

        // The connected nodes and edges the features' boundaries use.
        private S57EdgeGraph BuildEdgeGraph(IEnumerable<S57FeatureRecord> features)
        {
            uint next = uint.MaxValue;
            var graph = new S57EdgeGraph(() => next--);
            foreach (var feature in features)
            {
                foreach (var pointer in feature.SpatialPointers)
                {
                    if (pointer.Name.RecordNameCode != S57RecordNameCodes.Edge
                        || graph.Edges.ContainsKey((uint)pointer.Name.RecordId)
                        || !_vectorIndex.TryGetValue((S57RecordNameCodes.Edge, pointer.Name.RecordId), out var edge))
                        continue;

                    uint? begin = null, end = null;
                    foreach (var p in edge.VectorPointers)
                    {
                        if (!_vectorIndex.TryGetValue((p.Name.RecordNameCode, p.Name.RecordId), out var node) || node.Coordinates2D.Count == 0)
                            continue;
                        var id = (uint)p.Name.RecordId;
                        if (!graph.Nodes.ContainsKey(id))
                            graph.AddNode(id, node.Coordinates2D[0].X, node.Coordinates2D[0].Y);
                        if ((int)p.Topology == TopologyBegin) begin = id;
                        else if ((int)p.Topology == TopologyEnd) end = id;
                    }
                    if (begin is { } b && end is { } e)
                        graph.Edges[(uint)pointer.Name.RecordId] = new S57EdgeGraph.Edge(b, e, edge.Coordinates2D.Select(c => (c.X, c.Y)).ToArray());
                }
            }
            return graph;
        }

        // Emits the Data Coverage features of one coverage-available M_COVR,
        // each with the display scales of §2.2.6: optimum from the
        // compilation scale by Table 2-3, maximum half of it, and minimum
        // left empty for the producer to set. The first piece keeps the
        // M_COVR's FOID; the others are derived from it.
        private void EmitDataCoverage(
            S57FeatureRecord feat,
            ResolvedFeature resolved,
            ushort objl,
            IReadOnlyList<CoveragePiece> pieces,
            List<(S57FeatureRecord Record, IReadOnlyList<S101SpatialAssociation> Spatials)> coverageFeatures)
        {
            IReadOnlyList<S101Attribute>? attributes = null;
            IReadOnlyList<S101InformationAssociation> infoAssociations = [];
            var first = true;
            foreach (var piece in pieces)
            {
                var spatials = TranslateSpatialPointers(piece.Pointers is null ? feat : feat with { SpatialPointers = [.. piece.Pointers] });
                if (spatials.Count == 0)
                {
                    _diagnostics?.RecordFeatureWithoutGeometry(resolved.S101Code);
                    continue;
                }

                var typeCode = GetOrAssignFeatureTypeCode(resolved.S101Code);
                attributes ??= TranslateAttributes(feat.Attributes, resolved, objl, out infoAssociations, null, null);

                if (_diagnostics is not null) _diagnostics.FeaturesEmitted++;
                var recordId = _nextFeatureId++;
                if (first)
                    _recordIdByLnam[Lnam(feat.RecordName)] = recordId;
                else
                    _derivedFeatureIds.Add(recordId);
                first = false;

                Features.Add(new S101FeatureRecord
                {
                    RecordId = recordId,
                    FeatureTypeCode = typeCode,
                    ProducingAgency = (ushort)feat.RecordName.AgencyCode,
                    FeatureIdentificationNumber = (uint)feat.RecordName.FeatureId,
                    FeatureIdentificationSubdivision = (ushort)feat.RecordName.FeatureSubdivision,
                    Attributes = WithDisplayScales(resolved.S101Code, attributes, piece.CompilationScale),
                    SpatialAssociations = spatials,
                    FeatureAssociations = [],
                    InformationAssociations = infoAssociations,
                });
                coverageFeatures.Add((feat, spatials));
            }
        }

        private IReadOnlyList<S101Attribute> WithDisplayScales(string s101Class, IReadOnlyList<S101Attribute> attributes, int compilationScale)
        {
            if (OptimumDisplayScale(compilationScale) is not { } optimum) return attributes;
            var result = new List<S101Attribute>(attributes.Count + 2);
            if (_featureBindings.Binds(s101Class, S101AttrMaximumDisplayScale))
                result.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrMaximumDisplayScale), 1, (optimum / 2).ToString(CultureInfo.InvariantCulture)));
            if (_featureBindings.Binds(s101Class, S101AttrOptimumDisplayScale))
                result.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrOptimumDisplayScale), 1, optimum.ToString(CultureInfo.InvariantCulture)));
            result.AddRange(attributes);
            return result;
        }
    }
}
