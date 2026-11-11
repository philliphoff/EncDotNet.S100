using System.Collections.Frozen;
using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace EncDotNet.S100.Datasets.S57;

// The geometry Annex C asks of the converter beyond translating each feature:
// the primitive changes of Table 2-1, the skin-of-the-earth cut around
// floating docks, hulks and pontoons (§2.5.1.1), and the M_COVR tiling and
// M_CSCL objects of §3.5. All of it keeps S-57 chain-node topology: edges stay
// shared, and a split edge is split for every object that uses it.
public sealed partial class S101ToS57Translator
{
    // S-57 Group 1 objects S-101 doesn't count as skin of the earth, around
    // which the S-101 skin of the earth is cut (§2.5.1.1).
    private static readonly FrozenSet<string> SkinCutters = new[] { "FLODOC", "HULKES", "PONTON" }.ToFrozenSet(StringComparer.Ordinal);

    // The S-101 skin-of-the-earth classes' S-57 objects (S-101 DCEG 2.4).
    private static readonly FrozenSet<string> SkinAreas = new[] { "DEPARE", "DRGARE", "LNDARE", "UNSARE" }.ToFrozenSet(StringComparer.Ordinal);

    // The scale a curve is widened for when the dataset states none.
    private const int DefaultCompilationScale = 50_000;

    // §16.27 and Table 2-1: a curve becomes an area at least 0.3 mm wide at
    // the compilation scale.
    private const double MinimumWidthMetres = 0.0003;

    private const double MetresPerDegree = 111_320;

    private sealed partial class Context
    {
        private readonly int _compilationScale;

        private int? OptimumDisplayScale(S101FeatureRecord feature)
        {
            var value = BuildTree(feature.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings)
                .Find(n => n.Code == "optimumDisplayScale")?.Value;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale) && scale > 0 ? scale : null;
        }

        private uint NextVectorId() => _nextVectorId++;

        // The connected nodes and edges translated so far, as a graph.
        private S57EdgeGraph Graph()
        {
            var graph = new S57EdgeGraph(NextVectorId);
            foreach (var (id, node) in _connectedNodes)
                graph.AddNode(id, node.Coordinates2D[0].X, node.Coordinates2D[0].Y);
            foreach (var (id, edge) in _edges)
            {
                var begin = edge.VectorPointers.First(p => (int)p.Topology == 1).Name.RecordId;
                var end = edge.VectorPointers.First(p => (int)p.Topology == 2).Name.RecordId;
                graph.Edges[id] = new S57EdgeGraph.Edge((uint)begin, (uint)end, edge.Coordinates2D.Select(c => (c.X, c.Y)).ToArray());
            }
            return graph;
        }

        // Replaces the translated connected nodes and edges with the graph's.
        private void Store(S57EdgeGraph graph)
        {
            _connectedNodes.Clear();
            _edges.Clear();
            foreach (var (id, (x, y)) in graph.Nodes)
            {
                _connectedNodes[id] = new S57VectorRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RcnmConnectedNode, RecordId = (int)id },
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    VectorPointers = [],
                    Coordinates2D = [new S57Coordinate2D { X = x, Y = y }],
                    Soundings = [],
                    Attributes = [],
                };
            }
            foreach (var (id, edge) in graph.Edges)
            {
                _edges[id] = new S57VectorRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RcnmEdge, RecordId = (int)id },
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    VectorPointers =
                    [
                        NodePointer(edge.Begin, S57TopologyIndicator.Beginning),
                        NodePointer(edge.End, S57TopologyIndicator.End),
                    ],
                    Coordinates2D = edge.Inner.Select(p => new S57Coordinate2D { X = p.X, Y = p.Y }).ToArray(),
                    Soundings = [],
                    Attributes = [],
                };
            }
        }

        private static S57VectorPointer NodePointer(uint node, S57TopologyIndicator topology)
            => new()
            {
                Name = new S57RecordName { RecordNameCode = S57RcnmConnectedNode, RecordId = (int)node },
                Orientation = (S57Orientation)Null,
                Usage = (S57UsageIndicator)Null,
                Topology = topology,
                Mask = (S57MaskingIndicator)Null,
            };

        // ── Table 2-1 ───────────────────────────────────────────────────

        // A surface that S-57 allows only as a point becomes a point inside it,
        // at its centre where the centre lies inside.
        private (S57GeometricPrimitive, List<S57SpatialPointer>) CentrePoint(List<S57SpatialPointer> pointers)
        {
            if (Graph().Area(pointers) is not { } area)
                return (S57GeometricPrimitive.Area, pointers);
            var centre = area.Centroid;
            if (!area.Contains(centre)) centre = area.InteriorPoint;
            var id = NextVectorId();
            _isolatedNodes[id] = new S57VectorRecord
            {
                RecordName = new S57RecordName { RecordNameCode = S57RcnmIsolatedNode, RecordId = (int)id },
                RecordVersion = 1,
                UpdateInstruction = S57UpdateInstruction.Insert,
                VectorPointers = [],
                Coordinates2D = [new S57Coordinate2D { X = (int)Math.Round(centre.X), Y = (int)Math.Round(centre.Y) }],
                Soundings = [],
                Attributes = [],
            };
            return (S57GeometricPrimitive.Point, [Pointer(S57RcnmIsolatedNode, id, Null, Null)]);
        }

        // A curve that S-57 allows only as an area becomes an area centred on
        // it, 0.3 mm wide at the compilation scale (§16.9, §16.27). The
        // buffer is taken in a frame scaled by the cosine of the latitude, so
        // that its width is the same in every direction on the ground.
        private (S57GeometricPrimitive, List<S57SpatialPointer>) BufferToArea(List<S57SpatialPointer> pointers)
        {
            var graph = Graph();
            var factory = new GeometryFactory();
            var lines = pointers
                .Where(p => p.Name.RecordNameCode == S57RcnmEdge && graph.Edges.ContainsKey((uint)p.Name.RecordId))
                .Select(p => graph.Positions((uint)p.Name.RecordId))
                .ToList();
            if (lines.Count == 0) return (S57GeometricPrimitive.Line, pointers);

            var comf = (double)_comf;
            var latitude = lines.SelectMany(l => l).Average(p => p.Y) / comf;
            var cos = Math.Cos(latitude * Math.PI / 180);
            var scale = _compilationScale > 0 ? _compilationScale : DefaultCompilationScale;
            var halfWidth = MinimumWidthMetres * scale / MetresPerDegree * comf / 2;

            Geometry union = factory.CreateMultiLineString(lines
                .Select(l => factory.CreateLineString(l.Select(p => new Coordinate(p.X * cos, p.Y)).ToArray()))
                .ToArray());
            var buffered = union.Buffer(halfWidth, new BufferParameters { QuadrantSegments = 4 });

            var result = new List<S57SpatialPointer>();
            foreach (var polygon in Polygons(buffered))
            {
                AddRing(polygon.Shell, exterior: true);
                foreach (var hole in polygon.Holes) AddRing(hole, exterior: false);
            }
            Store(graph);
            return (S57GeometricPrimitive.Area, result);

            // One closed edge per ring, clockwise for the exterior.
            void AddRing(LineString ring, bool exterior)
            {
                var positions = ring.Coordinates.Select(c => ((int)Math.Round(c.X / cos), (int)Math.Round(c.Y))).ToList();
                bool ccw = NetTopologySuite.Algorithm.Orientation.IsCCW(ring.CoordinateSequence);
                if (ccw == exterior) positions.Reverse();
                var deduped = new List<(int X, int Y)>();
                foreach (var p in positions)
                {
                    if (deduped.Count == 0 || deduped[^1] != p) deduped.Add(p);
                }
                if (deduped.Count < 4) return;
                result.Add(S57EdgeGraph.Pointer(graph.AddEdge(deduped), reverse: false, usage: exterior ? 1 : 2));
            }
        }

        private static IEnumerable<Polygon> Polygons(Geometry geometry)
        {
            for (int i = 0; i < geometry.NumGeometries; i++)
            {
                if (geometry.GetGeometryN(i) is Polygon { IsEmpty: false } polygon) yield return polygon;
            }
        }

        // ── Post-pass ───────────────────────────────────────────────────

        private void ApplyTopology()
        {
            var graph = Graph();
            CutSkinOfTheEarth(graph);
            TileCoverage(graph);
            Store(graph);
            RemoveUnusedVectors();
        }

        private IEnumerable<PendingObject> Areas(Func<PendingObject, bool> predicate)
            => _objects.Where(o => !o.Removed && o.Primitive == S57GeometricPrimitive.Area && predicate(o));

        private void ReplaceEverywhere(Dictionary<uint, List<S57EdgeGraph.Use>> split)
        {
            if (split.Count == 0) return;
            foreach (var obj in _objects)
                obj.SpatialPointers = S57EdgeGraph.Replace(obj.SpatialPointers, split);
        }

        // §2.5.1.1: S-57 counts FLODOC, HULKES and PONTON areas as skin of the
        // earth and S-101 doesn't, so the S-101 skin of the earth under them
        // is cut away. Each skin area becomes its faces outside the cutters;
        // one that falls apart into several polygons becomes several objects,
        // and one wholly covered is removed.
        private void CutSkinOfTheEarth(S57EdgeGraph graph)
        {
            var cutters = Areas(o => SkinCutters.Contains(o.Rule.S57Acronym))
                .Select(o => (Object: o, Area: graph.Area(o.SpatialPointers)))
                .Where(c => c.Area is not null)
                .ToList();
            if (cutters.Count == 0) return;

            var skins = new List<(PendingObject Object, Geometry Area)>();
            foreach (var skin in Areas(o => SkinAreas.Contains(o.Rule.S57Acronym)))
            {
                if (graph.Area(skin.SpatialPointers) is not { } area) continue;
                if (cutters.Any(c => c.Area!.EnvelopeInternal.Intersects(area.EnvelopeInternal) && c.Area.Intersection(area).Area > 0))
                    skins.Add((skin, area));
            }
            if (skins.Count == 0) return;

            var edges = skins.Select(s => s.Object).Concat(cutters.Select(c => c.Object))
                .SelectMany(o => o.SpatialPointers)
                .Where(p => p.Name.RecordNameCode == S57RcnmEdge)
                .Select(p => (uint)p.Name.RecordId)
                .Where(graph.Edges.ContainsKey)
                .ToHashSet();
            var (split, _) = graph.Node(edges, []);
            ReplaceEverywhere(split);

            var noded = skins.Select(s => s.Object).Concat(cutters.Select(c => c.Object))
                .SelectMany(o => o.SpatialPointers)
                .Where(p => p.Name.RecordNameCode == S57RcnmEdge)
                .Select(p => (uint)p.Name.RecordId);
            var faces = graph.Faces(noded);
            foreach (var (skin, area) in skins)
            {
                var kept = faces.Where(f =>
                {
                    var probe = f.Probe;
                    return area.Contains(probe) && !cutters.Any(c => c.Area!.Contains(probe));
                });
                Reshape(skin, graph.Boundary(kept));
                _diagnostics?.RecordSkinOfTheEarthCut(skin.Rule.S57Acronym);
            }
        }

        // Gives an area object new boundaries: the first polygon keeps the
        // object, any further one becomes a copy with an identity of its own.
        private void Reshape(PendingObject obj, List<List<S57SpatialPointer>> polygons)
        {
            if (polygons.Count == 0)
            {
                obj.Removed = true;
                return;
            }
            obj.SpatialPointers = polygons[0];
            for (int i = 1; i < polygons.Count; i++)
            {
                var copy = Emit(null, obj.Rule, S57GeometricPrimitive.Area, polygons[i], obj.Bag.Clone(), NextFoid(obj.Foid));
                foreach (var (feature, list) in _objectsByFeature)
                {
                    if (list.Contains(obj)) { list.Add(copy); break; }
                }
            }
        }

        // §3.5: M_COVR objects tile the dataset's bounding box without
        // overlap: adjoining Data Coverage merge into one CATCOV 1 object, and
        // what no coverage covers is CATCOV 2. A coverage whose optimum display
        // scale isn't the dataset's compilation scale also gets an M_CSCL.
        private void TileCoverage(S57EdgeGraph graph)
        {
            var coverages = Areas(o => o.Rule.S57Acronym == "M_COVR")
                .Select(o => (Object: o, Area: graph.Area(o.SpatialPointers)))
                .Where(c => c.Area is not null)
                .ToList();
            if (coverages.Count == 0) return;

            EmitScales(coverages.Select(c => c.Object));

            var (minX, minY, maxX, maxY) = Extent(graph);
            List<(int X, int Y)> box = [(minX, minY), (minX, maxY), (maxX, maxY), (maxX, minY), (minX, minY)];

            var edges = coverages
                .SelectMany(c => c.Object.SpatialPointers)
                .Where(p => p.Name.RecordNameCode == S57RcnmEdge)
                .Select(p => (uint)p.Name.RecordId)
                .Where(graph.Edges.ContainsKey)
                .ToHashSet();
            var (split, added) = graph.Node(edges, [box]);
            ReplaceEverywhere(split);

            var noded = coverages.SelectMany(c => c.Object.SpatialPointers)
                .Where(p => p.Name.RecordNameCode == S57RcnmEdge)
                .Select(p => (uint)p.Name.RecordId)
                .Concat(added);
            var faces = graph.Faces(noded);
            var covered = faces.Where(f => coverages.Any(c => c.Area!.Contains(f.Probe))).ToList();
            var uncovered = faces.Except(covered).ToList();

            // Coverage available: one object per connected group.
            var available = graph.Boundary(covered);
            for (int i = 0; i < Math.Max(available.Count, coverages.Count); i++)
            {
                if (i < coverages.Count && i < available.Count)
                    coverages[i].Object.SpatialPointers = available[i];
                else if (i < coverages.Count)
                    coverages[i].Object.Removed = true;
                else
                    Emit(null, coverages[0].Object.Rule, S57GeometricPrimitive.Area, available[i], coverages[0].Object.Bag.Clone(), NextFoid(coverages[0].Object.Foid));
            }

            // No coverage available.
            foreach (var pointers in graph.Boundary(uncovered))
            {
                var bag = new AttributeBag();
                bag.Add("CATCOV", S101ToS57Rules.S57Attl("CATCOV"), "2");
                Emit(null, coverages[0].Object.Rule, S57GeometricPrimitive.Area, pointers, bag, NextFoid(coverages[0].Object.Foid));
                _diagnostics?.RecordSynthesised("M_COVR");
            }
        }

        // §3.5: an M_CSCL over each Data Coverage whose optimum display scale
        // differs from the compilation scale, with that scale as CSCALE.
        private void EmitScales(IEnumerable<PendingObject> coverages)
        {
            foreach (var coverage in coverages.ToList())
            {
                var feature = _objectsByFeature.FirstOrDefault(kv => kv.Value.Contains(coverage)).Key;
                if (!_featuresById.TryGetValue(feature, out var source)) continue;
                if (OptimumDisplayScale(source) is not { } scale || scale == _compilationScale) continue;

                var bag = new AttributeBag();
                bag.Add("CSCALE", S101ToS57Rules.S57Attl("CSCALE"), scale.ToString(CultureInfo.InvariantCulture));
                var rule = new S101ToS57FeatureRule("DataCoverage", S101ToS57Rules.S57Objl("M_CSCL"), "M_CSCL", "3.5", FrozenDictionary<string, string>.Empty);
                Emit(source, rule, S57GeometricPrimitive.Area, [.. coverage.SpatialPointers], bag, NextFoid(coverage.Foid));
                _diagnostics?.RecordSynthesised("M_CSCL");
            }
        }

        // The bounding box of every position in the dataset.
        private (int MinX, int MinY, int MaxX, int MaxY) Extent(S57EdgeGraph graph)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            void Include(int x, int y)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            foreach (var (x, y) in graph.Nodes.Values) Include(x, y);
            foreach (var edge in graph.Edges.Values)
                foreach (var (x, y) in edge.Inner) Include(x, y);
            foreach (var node in _isolatedNodes.Values.Concat(_soundingNodes.Values))
            {
                foreach (var c in node.Coordinates2D) Include(c.X, c.Y);
                foreach (var s in node.Soundings) Include(s.X, s.Y);
            }
            return (minX, minY, maxX, maxY);
        }

        // Vector records no remaining object uses (those of removed or reshaped
        // objects) are dropped, with the connected nodes only they end at.
        private void RemoveUnusedVectors()
        {
            var used = new HashSet<(int Rcnm, int Rcid)>();
            foreach (var obj in _objects)
            {
                if (obj.Removed) continue;
                foreach (var p in obj.SpatialPointers) used.Add((p.Name.RecordNameCode, p.Name.RecordId));
            }
            foreach (var id in _edges.Keys.Where(id => !used.Contains((S57RcnmEdge, (int)id))).ToList())
                _edges.Remove(id);
            foreach (var edge in _edges.Values)
                foreach (var p in edge.VectorPointers) used.Add((p.Name.RecordNameCode, p.Name.RecordId));
            foreach (var id in _connectedNodes.Keys.Where(id => !used.Contains((S57RcnmConnectedNode, (int)id))).ToList())
                _connectedNodes.Remove(id);
            foreach (var id in _isolatedNodes.Keys.Where(id => !used.Contains((S57RcnmIsolatedNode, (int)id))).ToList())
                _isolatedNodes.Remove(id);
        }
    }
}
