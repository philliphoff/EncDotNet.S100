using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

// The structural inverses of the forward translation: bridges and their spans
// (§6.6–§6.8), aggregations as C_AGGR (§25), and structure/equipment as
// master/slave (§18.2).
public sealed partial class S101ToS57Translator
{
    // An S-57 object under construction. Later passes add feature pointers,
    // text and topology before it becomes an S57FeatureRecord.
    private sealed class PendingObject(
        S101ToS57FeatureRule rule,
        S57GeometricPrimitive primitive,
        List<S57SpatialPointer> spatialPointers,
        AttributeBag bag,
        (int Agency, int Fidn, int Fids) foid)
    {
        public S101ToS57FeatureRule Rule { get; set; } = rule;
        public S57GeometricPrimitive Primitive { get; set; } = primitive;
        public List<S57SpatialPointer> SpatialPointers { get; set; } = spatialPointers;
        public AttributeBag Bag { get; } = bag;
        public (int Agency, int Fidn, int Fids) Foid { get; set; } = foid;
        public List<S57FeaturePointer> FeaturePointers { get; } = [];
        public bool Removed { get; set; }

        public S57LongName LongName => new((ushort)Foid.Agency, unchecked((uint)Foid.Fidn), (ushort)Foid.Fids);
    }

    // One S-101 feature association, read in the direction it is encoded.
    private readonly record struct Link(uint From, string Association, string Role, uint To);

    private sealed partial class Context
    {
        private const string BridgeAggregation = "BridgeAggregation";
        private const string StructureEquipment = "StructureEquipment";

        private readonly Dictionary<uint, S101FeatureRecord> _featuresById = new();
        private readonly List<Link> _links = [];
        private readonly Dictionary<uint, S101FeatureRecord> _bridgeOfSpan = new();
        private readonly HashSet<uint> _bridgesWithSpans = [];
        private readonly List<(S101FeatureRecord Feature, PendingObject Collection)> _aggregations = [];

        // Reads the feature associations once, and pairs every span with its
        // Bridge, whichever side encodes the BridgeAggregation.
        private void PlanStructure()
        {
            foreach (var f in _s101.Features)
                _featuresById[f.RecordId] = f;
            foreach (var f in _s101.Features)
            {
                foreach (var a in f.FeatureAssociations)
                {
                    _links.Add(new Link(
                        f.RecordId,
                        _s101.FeatureAssociationCatalogue.GetValueOrDefault(a.NumericCode) ?? string.Empty,
                        _s101.RoleCatalogue.GetValueOrDefault(a.RoleCode) ?? string.Empty,
                        a.RecordId));
                }
            }

            foreach (var link in _links)
            {
                if (link.Association != BridgeAggregation) continue;
                if (!_featuresById.TryGetValue(link.From, out var from) || !_featuresById.TryGetValue(link.To, out var to)) continue;
                var (bridge, span) = ClassOf(from) == "Bridge" ? (from, to) : (to, from);
                if (ClassOf(bridge) != "Bridge" || ClassOf(span) is not ("SpanFixed" or "SpanOpening")) continue;
                _bridgeOfSpan.TryAdd(span.RecordId, bridge);
                _bridgesWithSpans.Add(bridge.RecordId);
            }
        }

        // The other end of every association of the feature named
        // association, whichever side encodes it.
        private IEnumerable<uint> Linked(uint feature, string association)
        {
            foreach (var link in _links)
            {
                if (link.Association != association) continue;
                if (link.From == feature) yield return link.To;
                else if (link.To == feature) yield return link.From;
            }
        }

        // Whether the bridge's only component is one span, which then stands
        // for the whole bridge (§6.6, §2.5.6).
        private bool IsOnlySpan(S101FeatureRecord bridge)
        {
            var components = Linked(bridge.RecordId, BridgeAggregation).Distinct().ToList();
            return components.Count == 1 && _bridgeOfSpan.TryGetValue(components[0], out var b) && b.RecordId == bridge.RecordId;
        }

        private S101FeatureRecord? ParentBridge(S101FeatureRecord feature)
            => _bridgeOfSpan.GetValueOrDefault(feature.RecordId);

        // §6.6: a Bridge with spans becomes one BRIDGE per span. §21.2: a
        // physical AIS aid to navigation has no S-57 object; its structure
        // says it carries one (see EmitStructure).
        private bool IsReplacedByComponents(S101FeatureRecord feature, string s101Class)
        {
            if (s101Class == "Bridge" && _bridgesWithSpans.Contains(feature.RecordId))
            {
                _diagnostics?.RecordRestructured("Bridge", "6.6");
                return true;
            }
            if (s101Class == "PhysicalAISAidToNavigation")
            {
                _diagnostics?.RecordRestructured(s101Class, "21.2");
                return true;
            }
            return false;
        }

        // A span with its Bridge's information associations added, so that its
        // BRIDGE carries the Bridge's NauticalInformation as well.
        private static S101FeatureRecord WithInformation(S101FeatureRecord span, S101FeatureRecord bridge)
            => bridge.InformationAssociations.Count == 0
                ? span
                : new S101FeatureRecord
                {
                    RecordId = span.RecordId,
                    FeatureTypeCode = span.FeatureTypeCode,
                    ProducingAgency = span.ProducingAgency,
                    FeatureIdentificationNumber = span.FeatureIdentificationNumber,
                    FeatureIdentificationSubdivision = span.FeatureIdentificationSubdivision,
                    Attributes = span.Attributes,
                    SpatialAssociations = span.SpatialAssociations,
                    FeatureAssociations = span.FeatureAssociations,
                    InformationAssociations = [.. span.InformationAssociations, .. bridge.InformationAssociations],
                };

        // The second pass over associations, once every object exists:
        // aggregation members (C_AGGR peers), a bridge's spans and parts
        // (C_AGGR, §6.6), and structures with their equipment (§18.2).
        private void EmitStructure()
        {
            foreach (var (feature, collection) in _aggregations)
            {
                var association = S101ToS57Rules.Aggregations[ClassOf(feature)!].Association;
                foreach (var member in Linked(feature.RecordId, association).Distinct())
                    AddPeers(collection, member);
                if (collection.FeaturePointers.Count == 0)
                {
                    // A C_AGGR with no members says nothing.
                    collection.Removed = true;
                    _diagnostics?.RecordDroppedFeature(ClassOf(feature)!, "25");
                }
                else
                {
                    _diagnostics?.RecordSynthesised("C_AGGR");
                }
            }

            foreach (var bridgeId in _bridgesWithSpans)
                EmitBridgeAggregation(_featuresById[bridgeId]);

            EmitAssociationCollections();
            NamePilotageDistricts();

            foreach (var link in _links)
            {
                if (link.Association != StructureEquipment) continue;
                var (structure, equipment) = link.Role == "theStructure" ? (link.To, link.From) : (link.From, link.To);
                if (!_featuresById.TryGetValue(equipment, out var equipmentFeature)) continue;

                // A bridge that is a C_AGGR collects its equipment there.
                if (_bridgeCollections.TryGetValue(structure, out var bridgeCollection))
                {
                    AddPeers(bridgeCollection, equipment);
                    continue;
                }
                if (!_objectsByFeature.TryGetValue(structure, out var masters)) continue;
                var master = masters[0];

                if (ClassOf(equipmentFeature) == "PhysicalAISAidToNavigation")
                {
                    // §21.2: the structure says it carries an AIS aid.
                    const string ais = "Automatic Identification System (AIS) aid to navigation";
                    if (!master.Bag.Inform.Contains(ais)) master.Bag.Inform.Add(ais);
                    continue;
                }
                if (!_objectsByFeature.TryGetValue(equipment, out var slaves)) continue;
                foreach (var slave in slaves)
                {
                    if (slave != master) AddSlave(master, slave);
                }
            }
        }

        private readonly Dictionary<uint, PendingObject> _bridgeCollections = new();

        // §6.6: the BRIDGEs of one bridge are tied by a C_AGGR, as are its
        // pylons and pontoons. A Bridge with one span that shares its identity
        // (the forward translation of a single BRIDGE) needs none.
        private void EmitBridgeAggregation(S101FeatureRecord bridge)
        {
            var bridgeFoid = (Agency: (int)bridge.ProducingAgency, Fidn: unchecked((int)bridge.FeatureIdentificationNumber), Fids: (int)bridge.FeatureIdentificationSubdivision);
            var components = Linked(bridge.RecordId, BridgeAggregation).Distinct().ToList();
            if (IsOnlySpan(bridge) && !Linked(bridge.RecordId, StructureEquipment).Any()) return;

            var bag = new AttributeBag();
            var tree = BuildTree(bridge.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings);
            foreach (var node in tree.Where(n => n.Code == "featureName"))
                TranslateComplex("Bridge", node, bag);
            FoldNames(bag);
            var rule = new S101ToS57FeatureRule("BridgeAggregation", S101ToS57Rules.S57Objl("C_AGGR"), "C_AGGR", "6.6", S101ToS57Rules.NoFixedAttributesValue);
            // The bridge's identity goes to its C_AGGR unless a span already
            // carries it.
            var foid = _objects.Exists(o => o.Foid == bridgeFoid) ? NextFoid(bridgeFoid) : bridgeFoid;
            var collection = Emit(bridge, rule, S57GeometricPrimitive.None, [], bag, foid);
            foreach (var component in components)
                AddPeers(collection, component);
            _bridgeCollections[bridge.RecordId] = collection;
            _diagnostics?.RecordSynthesised("C_AGGR");
        }

        // §25.2, §25.8, §25.11, §25.14: associations between features that
        // S-57 encodes as a collection object of the features at both ends,
        // one per feature that heads the association.
        private void EmitAssociationCollections()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in _links.Where(l => CollectionAssociations.ContainsKey(l.Association)).GroupBy(l => (l.Association, l.From)))
            {
                var (acronym, clause) = CollectionAssociations[group.Key.Association];
                var members = group.Select(l => l.To).Prepend(group.Key.From).Distinct().Order().ToList();
                if (!seen.Add($"{group.Key.Association}:{string.Join(',', members)}")) continue;
                if (!_objectsByFeature.TryGetValue(group.Key.From, out var head)) continue;

                var rule = new S101ToS57FeatureRule(group.Key.Association, S101ToS57Rules.S57Objl(acronym), acronym, clause, S101ToS57Rules.NoFixedAttributesValue);
                var collection = Emit(null, rule, S57GeometricPrimitive.None, [], new AttributeBag(), NextFoid(head[0].Foid));
                foreach (var member in members)
                    AddPeers(collection, member);
                if (collection.FeaturePointers.Count < 2) collection.Removed = true;
                else _diagnostics?.RecordSynthesised(acronym);
            }
        }

        private static readonly Dictionary<string, (string Acronym, string Clause)> CollectionAssociations = new(StringComparer.Ordinal)
        {
            ["AidsToNavigationAssociation"] = ("C_ASSO", "25.2"),
            ["FairwayAuxiliary"] = ("C_ASSO", "25.8"),
            ["PilotageDistrictAssociation"] = ("C_AGGR", "25.11"),
            ["RoofedStructureAggregation"] = ("C_AGGR", "25.14"),
        };

        // §22.1 / §16.26: a pilot boarding place takes the name of its
        // pilotage district as PILDST / NPLDST.
        private void NamePilotageDistricts()
        {
            foreach (var link in _links.Where(l => l.Association == "PilotageDistrictAssociation"))
            {
                foreach (var (district, place) in new[] { (link.From, link.To), (link.To, link.From) })
                {
                    if (!_featuresById.TryGetValue(district, out var d) || ClassOf(d) != "PilotageDistrict") continue;
                    if (!_objectsByFeature.TryGetValue(district, out var districtObjects)
                        || !_objectsByFeature.TryGetValue(place, out var placeObjects)) continue;
                    var names = districtObjects[0].Bag.Values;
                    foreach (var (acronym, target) in new[] { ("OBJNAM", "PILDST"), ("NOBJNM", "NPLDST") })
                    {
                        var name = names.Find(v => v.Acronym == acronym).Values?.FirstOrDefault();
                        if (name is not null && !placeObjects[0].Bag.Has(target))
                            placeObjects[0].Bag.Add(target, S101ToS57Rules.S57Attl(target), name);
                    }
                }
            }
        }

        // Adds every S-57 object of an S-101 feature to a collection, as peers.
        private void AddPeers(PendingObject collection, uint member)
        {
            if (!_objectsByFeature.TryGetValue(member, out var objects)) return;
            foreach (var obj in objects)
            {
                if (obj == collection || obj.Removed) continue;
                if (collection.FeaturePointers.Exists(p => p.Name.Equals(obj.LongName))) continue;
                collection.FeaturePointers.Add(new S57FeaturePointer { Name = obj.LongName, Relationship = S57RelationshipIndicator.Peer });
            }
        }
    }
}
