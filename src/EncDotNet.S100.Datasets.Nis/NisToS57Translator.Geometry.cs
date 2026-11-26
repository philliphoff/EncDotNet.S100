using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.OverlayNG;

namespace EncDotNet.S100.Datasets.Nis;

// The geometry of the cell as S-57 chain-node topology: isolated nodes for
// points and sounding groups, and the edges and connected nodes every line
// and area ring shares once they are noded together.
public sealed partial class NisToS57Translator
{
    private sealed partial class Context
    {
        private static (int X, int Y) Grid(NisPosition p)
            => ((int)Math.Round(p.Longitude * CoordinateFactor, MidpointRounding.AwayFromZero),
                (int)Math.Round(p.Latitude * CoordinateFactor, MidpointRounding.AwayFromZero));

        // ── Points ──────────────────────────────────────────────────────

        // One isolated node per position and node attributes, shared by the
        // point features at it.
        private List<S57VectorRecord> BuildPoints(List<Pending> objects)
        {
            var nodes = new Dictionary<string, (uint Id, S57VectorRecord Record)>(StringComparer.Ordinal);
            foreach (var pending in objects)
            {
                if (pending.Feature.Geometry is not { Kind: NisGeometryKind.Point or NisGeometryKind.MultiPoint } geometry) continue;
                var position = geometry.Parts.SelectMany(p => p).FirstOrDefault();
                if (geometry.IsEmpty)
                {
                    _diagnostics.EmptyGeometries++;
                    pending.Removed = true;
                    continue;
                }
                var (x, y) = Grid(position);
                var key = $"{x},{y}|" + string.Join(';', pending.NodeAttributes.Select(a => $"{a.AttributeCode}={a.Value}"));
                if (!nodes.TryGetValue(key, out var node))
                {
                    var id = _nextVectorId++;
                    node = (id, new S57VectorRecord
                    {
                        RecordName = new S57RecordName { RecordNameCode = RcnmIsolatedNode, RecordId = (int)id },
                        RecordVersion = 1,
                        UpdateInstruction = S57UpdateInstruction.Insert,
                        VectorPointers = [],
                        Coordinates2D = [new S57Coordinate2D { X = x, Y = y }],
                        Soundings = [],
                        Attributes = pending.NodeAttributes.OrderBy(a => a.AttributeCode).ToList(),
                    });
                    nodes[key] = node;
                }
                pending.Primitive = S57GeometricPrimitive.Point;
                pending.SpatialPointers.Add(NodePointer(node.Id));
            }
            return nodes.Values.OrderBy(n => n.Id).Select(n => n.Record).ToList();
        }

        // ── Soundings ───────────────────────────────────────────────────

        // Soundings with the same attributes share SOUNDG objects of at most
        // MaxSoundingsPerObject soundings, each with one isolated node of
        // soundings (SG3D) and its first member's identity.
        private List<S57VectorRecord> BuildSoundings(List<Pending> soundings)
        {
            var vectors = new List<S57VectorRecord>();
            int limit = Math.Max(1, _options.MaxSoundingsPerObject);
            var groups = soundings
                .Where(s => !s.Removed)
                .GroupBy(s => string.Join(';', s.Attributes.Concat(s.National).OrderBy(a => a.AttributeCode).Select(a => $"{a.AttributeCode}={a.Value}")), StringComparer.Ordinal);
            foreach (var group in groups)
            {
                foreach (var chunk in group.Chunk(limit))
                {
                    var points = new List<S57Sounding>(chunk.Length);
                    var members = new List<Pending>(chunk.Length);
                    foreach (var member in chunk)
                    {
                        foreach (var p in member.Feature.Geometry?.Parts.SelectMany(part => part) ?? [])
                        {
                            double? depth = p.Z ?? Depth(member.Feature);
                            if (depth is not { } d) continue;
                            var (x, y) = Grid(p);
                            points.Add(new S57Sounding { X = x, Y = y, Depth = (int)Math.Round(d * SoundingFactor, MidpointRounding.AwayFromZero) });
                        }
                        members.Add(member);
                    }
                    if (points.Count == 0)
                    {
                        _diagnostics.EmptyGeometries += members.Count;
                        continue;
                    }

                    var id = _nextVectorId++;
                    vectors.Add(new S57VectorRecord
                    {
                        RecordName = new S57RecordName { RecordNameCode = RcnmIsolatedNode, RecordId = (int)id },
                        RecordVersion = 1,
                        UpdateInstruction = S57UpdateInstruction.Insert,
                        VectorPointers = [],
                        Coordinates2D = [],
                        Soundings = points,
                        Attributes = [],
                    });

                    var first = members[0];
                    first.LongName = Identity(first);
                    first.Primitive = S57GeometricPrimitive.Point;
                    first.SpatialPointers.Add(NodePointer(id));
                    _objects.Add(first);
                    foreach (var member in members.Skip(1))
                    {
                        if (member.Feature.LongName is { } name) _byLongName.TryAdd(name.Trim(), first);
                    }
                    _diagnostics.SoundingObjects++;
                    _diagnostics.SoundingsWritten += points.Count;
                }
            }
            return vectors;

            static double? Depth(NisFeature feature)
                => feature.Attributes.TryGetValue("DEPTH", out var value) && value is double d ? d : null;
        }

        private static S57SpatialPointer NodePointer(uint id)
            => new()
            {
                Name = new S57RecordName { RecordNameCode = RcnmIsolatedNode, RecordId = (int)id },
                Orientation = (S57Orientation)Null,
                Usage = (S57UsageIndicator)Null,
                Mask = (S57MaskingIndicator)Null,
            };

        // ── Lines and areas ─────────────────────────────────────────────

        // One input line to node: a path of a line feature or a ring of an area.
        private sealed record Part(Pending Owner, int Index, List<(int X, int Y)> Positions);

        private List<S57VectorRecord> BuildLinesAndAreas(List<Pending> objects, bool addCoverage)
        {
            var parts = new List<Part>();
            foreach (var pending in objects)
            {
                if (pending.Feature.Geometry is not { Kind: NisGeometryKind.Line or NisGeometryKind.Area } geometry) continue;
                bool area = geometry.Kind == NisGeometryKind.Area;
                int index = 0;
                foreach (var path in geometry.Parts)
                {
                    var positions = WithoutRepeats(path.Select(Grid));
                    if (area && positions.Count > 1 && positions[0] != positions[^1]) positions.Add(positions[0]);
                    bool usable = area ? positions.Count >= 4 && Math.Abs(SignedArea(positions)) > 0 : positions.Count >= 2;
                    if (usable) parts.Add(new Part(pending, index++, positions));
                }
                if (index == 0)
                {
                    _diagnostics.EmptyGeometries++;
                    pending.Removed = true;
                    continue;
                }
                pending.Primitive = area ? S57GeometricPrimitive.Area : S57GeometricPrimitive.Line;
            }

            if (addCoverage) AddCoverage(parts);

            var noded = new S57EdgeGraph(() => _nextVectorId++);
            var pieces = noded.AddNodedLines(parts.Select(p => (IReadOnlyList<(int X, int Y)>)p.Positions).ToList());
            var (graph, uses) = EdgeChains.Merge(noded, pieces, () => _nextVectorId++);

            var byOwner = parts
                .Select((p, i) => (Part: p, Uses: uses[i]))
                .GroupBy(x => x.Part.Owner, ReferenceEqualityComparer.Instance);
            foreach (var group in byOwner)
            {
                var owner = (Pending)group.Key!;
                var ownParts = group.ToList();
                if (owner.Primitive == S57GeometricPrimitive.Line)
                {
                    foreach (var (_, lineUses) in ownParts)
                    {
                        foreach (var use in lineUses)
                            owner.SpatialPointers.Add(S57EdgeGraph.Pointer(use.Edge, use.Reverse, Null));
                    }
                }
                else
                {
                    owner.SpatialPointers.AddRange(AreaPointers(ownParts));
                }
                if (owner.SpatialPointers.Count == 0)
                {
                    _diagnostics.EmptyGeometries++;
                    owner.Removed = true;
                }
            }

            var vectors = new List<S57VectorRecord>();
            foreach (var (id, (x, y)) in graph.Nodes.OrderBy(n => n.Key))
            {
                vectors.Add(new S57VectorRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = RcnmConnectedNode, RecordId = (int)id },
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    VectorPointers = [],
                    Coordinates2D = [new S57Coordinate2D { X = x, Y = y }],
                    Soundings = [],
                    Attributes = [],
                });
            }
            foreach (var (id, edge) in graph.Edges.OrderBy(e => e.Key))
            {
                vectors.Add(new S57VectorRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = RcnmEdge, RecordId = (int)id },
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    VectorPointers = [EdgeNode(edge.Begin, S57TopologyIndicator.Beginning), EdgeNode(edge.End, S57TopologyIndicator.End)],
                    Coordinates2D = edge.Inner.Select(p => new S57Coordinate2D { X = p.X, Y = p.Y }).ToArray(),
                    Soundings = [],
                    Attributes = [],
                });
            }
            return vectors;
        }

        // An area's rings: each exterior ring clockwise with usage 1, then
        // the rings inside it anticlockwise with usage 2. A ring is interior
        // when an odd number of the feature's other rings hold it.
        private static List<S57SpatialPointer> AreaPointers(List<(Part Part, List<S57EdgeGraph.Use> Uses)> rings)
        {
            var depth = rings.Select(r => rings.Count(o => !ReferenceEquals(o.Part, r.Part) && Holds(o.Part.Positions, Probe(r.Part.Positions)))).ToList();
            var pointers = new List<S57SpatialPointer>();
            var placed = new HashSet<int>();
            for (int i = 0; i < rings.Count; i++)
            {
                if (depth[i] % 2 != 0) continue;
                Add(i, exterior: true);
                for (int j = 0; j < rings.Count; j++)
                {
                    if (depth[j] % 2 == 0 || placed.Contains(j) || depth[j] != depth[i] + 1) continue;
                    if (Holds(rings[i].Part.Positions, Probe(rings[j].Part.Positions))) Add(j, exterior: false);
                }
            }
            // A hole no exterior claimed still bounds the area.
            for (int j = 0; j < rings.Count; j++)
            {
                if (!placed.Contains(j)) Add(j, exterior: false);
            }
            return pointers;

            void Add(int index, bool exterior)
            {
                placed.Add(index);
                var (part, uses) = rings[index];
                // Clockwise has a negative signed area with Y north.
                bool clockwise = SignedArea(part.Positions) < 0;
                bool reverse = clockwise != exterior;
                var ordered = reverse ? Enumerable.Reverse(uses) : uses;
                foreach (var use in ordered)
                    pointers.Add(S57EdgeGraph.Pointer(use.Edge, use.Reverse ^ reverse, exterior ? 1 : 2));
            }
        }

        // M_COVR objects that tile the extent of the data, as an ENC's must
        // (S-57 Appendix B.1 §3.2): CATCOV 1 (coverage available) over the
        // union of the skin-of-the-earth areas, and CATCOV 2 (no coverage)
        // over the rest. With no skin of the earth, CATCOV 1 covers it all.
        private void AddCoverage(List<Part> parts)
        {
            if (!_catalogue.TryGetObjectClass("M_COVR", out var mcovr) || mcovr is null) return;
            if (!_catalogue.TryGetAttribute("CATCOV", out var catcov) || catcov is null) return;
            if (Extent() is not { } extent) return;

            var covered = SkinUnion(parts) ?? extent;
            var uncovered = extent.Difference(covered);
            Add("1", covered);
            Add("2", uncovered);
            _diagnostics.CoverageSynthesised = true;

            void Add(string category, Geometry area)
            {
                var rings = Rings(area);
                if (rings.Count == 0) return;
                var feature = new NisFeature { FeatureClass = "(synthesised)", ObjectClass = "M_COVR" };
                var coverage = new Pending(feature, mcovr) { Primitive = S57GeometricPrimitive.Area };
                coverage.Attributes.Add(new S57AttributeValue { AttributeCode = catcov.Code, Value = category });
                coverage.LongName = Identity(coverage, synthesised: true);
                _objects.Add(coverage);
                for (int i = 0; i < rings.Count; i++)
                    parts.Add(new Part(coverage, i, rings[i]));
            }
        }

        private static readonly GeometryFactory GridFactory = new(new PrecisionModel(1.0));

        // The union of the skin-of-the-earth areas, in grid units.
        private static Geometry? SkinUnion(List<Part> parts)
        {
            var areas = new List<Geometry>();
            foreach (var group in parts.Where(p => p.Owner.Primitive == S57GeometricPrimitive.Area && SkinOfTheEarth.Contains(p.Owner.Acronym)).GroupBy(p => p.Owner, ReferenceEqualityComparer.Instance))
            {
                // As in AreaPointers, a ring an odd number of others hold is a hole.
                var own = group.ToList();
                Geometry? shells = null, holes = null;
                foreach (var part in own)
                {
                    var probe = Probe(part.Positions);
                    bool hole = own.Count(o => !ReferenceEquals(o, part) && Holds(o.Positions, probe)) % 2 != 0;
                    Geometry polygon = GridFactory.CreatePolygon(part.Positions.Select(q => new Coordinate(q.X, q.Y)).ToArray());
                    if (!polygon.IsValid) polygon = polygon.Buffer(0);
                    if (hole) holes = holes is null ? polygon : holes.Union(polygon);
                    else shells = shells is null ? polygon : shells.Union(polygon);
                }
                if (shells is null) continue;
                areas.Add(holes is null ? shells : shells.Difference(holes));
            }
            return areas.Count == 0 ? null : OverlayNGRobust.Union(areas);
        }

        // The rings of the polygons of an area, in grid units.
        private static List<List<(int X, int Y)>> Rings(Geometry area)
        {
            var rings = new List<List<(int X, int Y)>>();
            for (int i = 0; i < area.NumGeometries; i++)
            {
                if (area.GetGeometryN(i) is not Polygon polygon || polygon.IsEmpty) continue;
                rings.Add(ToGrid(polygon.Shell));
                foreach (var hole in polygon.Holes) rings.Add(ToGrid(hole));
            }
            return rings.Where(r => r.Count >= 4).ToList();

            static List<(int X, int Y)> ToGrid(LineString ring)
                => WithoutRepeats(ring.Coordinates.Select(c => ((int)Math.Round(c.X), (int)Math.Round(c.Y))));
        }

        // The rectangle over every position of the cell, in grid units.
        private Polygon? Extent()
        {
            var positions = _objects
                .Where(o => !o.Removed)
                .SelectMany(o => o.Feature.Geometry?.Parts.SelectMany(p => p) ?? [])
                .Select(Grid)
                .ToList();
            if (positions.Count == 0) return null;
            int west = positions.Min(p => p.X), east = positions.Max(p => p.X);
            int south = positions.Min(p => p.Y), north = positions.Max(p => p.Y);
            if (west == east || south == north) return null;
            return GridFactory.CreatePolygon(
            [
                new Coordinate(west, south), new Coordinate(west, north), new Coordinate(east, north),
                new Coordinate(east, south), new Coordinate(west, south),
            ]);
        }

        private static S57VectorPointer EdgeNode(uint node, S57TopologyIndicator topology)
            => new()
            {
                Name = new S57RecordName { RecordNameCode = RcnmConnectedNode, RecordId = (int)node },
                Orientation = (S57Orientation)Null,
                Usage = (S57UsageIndicator)Null,
                Topology = topology,
                Mask = (S57MaskingIndicator)Null,
            };

        private static List<(int X, int Y)> WithoutRepeats(IEnumerable<(int X, int Y)> positions)
        {
            var result = new List<(int X, int Y)>();
            foreach (var p in positions)
            {
                if (result.Count == 0 || result[^1] != p) result.Add(p);
            }
            return result;
        }

        // Twice the signed area (shoelace); positive anticlockwise with Y north.
        private static double SignedArea(List<(int X, int Y)> ring)
        {
            double sum = 0;
            for (int i = 0; i + 1 < ring.Count; i++)
                sum += ((double)ring[i].X * ring[i + 1].Y) - ((double)ring[i + 1].X * ring[i].Y);
            return sum;
        }

        // A point of a ring to test containment with: the middle of its first
        // segment, which another ring that only touches it at a vertex won't hold.
        private static (double X, double Y) Probe(List<(int X, int Y)> ring)
            => ((ring[0].X + (double)ring[1].X) / 2, (ring[0].Y + (double)ring[1].Y) / 2);

        private static bool Holds(List<(int X, int Y)> ring, (double X, double Y) p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 2; i < ring.Count - 1; j = i++)
            {
                var (xi, yi) = ring[i];
                var (xj, yj) = ring[j];
                if ((yi > p.Y) != (yj > p.Y) && p.X < ((double)(xj - xi) * (p.Y - yi) / (yj - yi)) + xi)
                    inside = !inside;
            }
            return inside;
        }
    }
}
