using System.Collections.Frozen;
using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Translates an <see cref="S101Document"/> into an in-memory S-57
/// <see cref="S57Document"/>, following S-65 Annex C (<em>S-101 to S-57 ENC
/// Conversion Guidance</em>, Ed 1.0.0, May 2025). This is a preview: it covers
/// Annex C §2–§30 (see <see cref="S101ToS57Coverage"/>), but not yet the
/// attributes of spatial records.
/// </summary>
/// <remarks>
/// <para>
/// The output uses the <c>EncDotNet.S57</c> record types and is a base cell
/// <see cref="S57DocumentWriter"/> can encode: every record is an insert at
/// version 1, the DSID/DSPM describe a new ENC cell, and ATTF/NATF text sits
/// at lexical levels that encode it. Points become isolated nodes and S-101
/// curves shared edges between connected nodes, which keeps S-57 chain-node
/// topology. The skin of the earth is cut around floating docks, hulks and
/// pontoons (§2.5.1.1), and the <c>M_COVR</c> objects tile the dataset's
/// bounding box, with an <c>M_CSCL</c> where a coverage's scale differs
/// (§3.5); both split shared edges for every object that uses them.
/// <see cref="S57TopologyCheck"/> checks the result.
/// </para>
/// <para>
/// The structural inverses of the forward translation are built in a second
/// pass over the S-101 feature associations: a bridge's spans become
/// <c>BRIDGE</c> objects tied by a <c>C_AGGR</c> (§6.6), aggregations become
/// <c>C_AGGR</c> or <c>C_ASSO</c> (§25), and Structure/Equipment becomes master
/// and slave feature pointers (§18.2).
/// </para>
/// </remarks>
public sealed partial class S101ToS57Translator
{
    private const ushort FeatureRecordName = 100;
    private const byte S101RcnmPoint = 110;
    private const byte S101RcnmMultiPoint = 115;
    private const byte S101RcnmCurve = 120;
    private const byte S101RcnmCompositeCurve = 125;
    private const byte S101RcnmSurface = 130;
    private const int S57RcnmIsolatedNode = 110;
    private const int S57RcnmConnectedNode = 120;
    private const int S57RcnmEdge = 130;
    private const int Null = 255;

    // Feature classes Annex C drops outright.
    private static readonly FrozenDictionary<string, string> DroppedClasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TextPlacement"] = "2.5.8.1",
            ["UpdateInformation"] = "3.12",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // §8.3, §8.18, §8.19: classes whose clause drops attributes only on a
    // surface, where the S-57 object is Group 1 (skin of the earth).
    private static readonly FrozenSet<string> GroupOneOnSurface = new[]
    {
        "FloatingDock", "Hulk", "Pontoon",
    }.ToFrozenSet(StringComparer.Ordinal);

    // S-57 Group 1 (skin of the earth) object classes (S-57 Appendix B.1 §3.5).
    private static readonly FrozenSet<string> SkinOfTheEarth = new[]
    {
        "DEPARE", "DRGARE", "FLODOC", "HULKES", "LNDARE", "PONTON", "UNSARE",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly Lazy<FrozenDictionary<string, string>> DefaultValueTypes = new(LoadValueTypes);

    private readonly S101ToS57Rules _rules;
    private readonly S101ToS57TranslationOptions _options;
    private readonly S101FeatureAttributeBindings _bindings;
    private readonly IReadOnlyDictionary<string, string> _valueTypes;

    /// <summary>Creates a translator with the default rules and options.</summary>
    public S101ToS57Translator()
        : this(S101ToS57Rules.Default, S101ToS57TranslationOptions.Default)
    {
    }

    /// <summary>Creates a translator with the default rules and the given options.</summary>
    public S101ToS57Translator(S101ToS57TranslationOptions options)
        : this(S101ToS57Rules.Default, options)
    {
    }

    /// <summary>Creates a translator with the given rules and options.</summary>
    /// <param name="rules">The conversion rules.</param>
    /// <param name="options">The converter choices Annex C leaves open.</param>
    public S101ToS57Translator(S101ToS57Rules rules, S101ToS57TranslationOptions options)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(options);
        _rules = rules;
        _options = options;
        _bindings = S101FeatureAttributeBindings.Default;
        _valueTypes = DefaultValueTypes.Value;
    }

    /// <summary>Translates an S-101 document into an S-57 document.</summary>
    public S57Document Translate(S101Document s101) => Translate(s101, null);

    /// <summary>
    /// Translates an S-101 document into an S-57 document, collecting
    /// diagnostics into <paramref name="diagnostics"/> when it isn't <c>null</c>.
    /// </summary>
    public S57Document Translate(S101Document s101, S101ToS57TranslationDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(s101);
        return new Context(this, s101, diagnostics).Run();
    }

    private static FrozenDictionary<string, string> LoadValueTypes()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("Bundled S-101 Feature Catalogue not found in EncDotNet.S100.Specifications.");
        var fc = FeatureCatalogueReader.Read(stream);
        return fc.SimpleAttributes
            .Where(a => !string.IsNullOrEmpty(a.Code))
            .GroupBy(a => a.Code, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.First().ValueType, StringComparer.Ordinal);
    }

    // ── Attribute tree ──────────────────────────────────────────────────

    // One S-101 attribute row with the rows nested under it.
    internal sealed class AttributeNode(string? code, string value)
    {
        public string? Code { get; } = code;
        public string Value { get; } = value;
        public List<AttributeNode> Children { get; } = [];

        public string? Sub(string code) => Children.Find(c => c.Code == code)?.Value;

        public IEnumerable<AttributeNode> All(string code) => Children.Where(c => c.Code == code);
    }

    // Rebuilds the nesting of a record's flat attribute rows: from PAIX where
    // the rows carry it, otherwise from the Feature Catalogue, the way
    // S57ToS101Translator assigns PAIX (a row nests in the innermost open
    // complex that binds it).
    internal static List<AttributeNode> BuildTree(
        IReadOnlyList<S101Attribute> rows,
        IReadOnlyDictionary<ushort, string> catalogue,
        S101FeatureAttributeBindings bindings)
    {
        var nodes = new List<AttributeNode>(rows.Count);
        foreach (var row in rows)
            nodes.Add(new AttributeNode(catalogue.GetValueOrDefault(row.NumericCode), row.Value ?? string.Empty));

        var roots = new List<AttributeNode>();
        if (rows.Any(r => r.ParentIndex > 0))
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var parent = rows[i].ParentIndex - 1;
                if (parent >= 0 && parent < nodes.Count && parent != i)
                    nodes[parent].Children.Add(nodes[i]);
                else
                    roots.Add(nodes[i]);
            }
            return roots;
        }

        var open = new List<AttributeNode>();
        foreach (var node in nodes)
        {
            while (open.Count > 0 && !bindings.BindsSubAttribute(open[^1].Code, node.Code))
                open.RemoveAt(open.Count - 1);
            if (open.Count > 0)
                open[^1].Children.Add(node);
            else
                roots.Add(node);
            if (bindings.IsComplexAttribute(node.Code))
                open.Add(node);
        }
        return roots;
    }

    // ── S-57 attribute accumulation ─────────────────────────────────────

    // The S-57 attributes of one object as they are assembled: values per
    // acronym in arrival order, and the text strings to fold into INFORM and
    // NINFOM.
    private sealed class AttributeBag
    {
        public List<(string Acronym, ushort Attl, List<string> Values)> Values { get; } = [];
        public List<string> Inform { get; } = [];
        public List<string> Ninfom { get; } = [];

        public void Add(string acronym, ushort attl, string value)
        {
            var index = Values.FindIndex(v => v.Acronym == acronym);
            if (index < 0)
                Values.Add((acronym, attl, [value]));
            else
                Values[index].Values.Add(value);
        }

        public bool Has(string acronym) => Values.Exists(v => v.Acronym == acronym);

        public AttributeBag Clone()
        {
            var copy = new AttributeBag();
            foreach (var (acronym, attl, values) in Values)
                copy.Values.Add((acronym, attl, [.. values]));
            copy.Inform.AddRange(Inform);
            copy.Ninfom.AddRange(Ninfom);
            return copy;
        }
    }

    // One light sector of a LightSectored, as a further LIGHTS object needs it.
    private sealed record LightSector(AttributeBag Attributes);

    // ── Translation context ─────────────────────────────────────────────

    private sealed partial class Context
    {
        private readonly S101ToS57Translator _owner;
        private readonly S101Document _s101;
        private readonly S101ToS57TranslationDiagnostics? _diagnostics;
        private readonly S101ToS57Rules _rules;
        private readonly S101ToS57TranslationOptions _options;

        private readonly Dictionary<uint, S57VectorRecord> _isolatedNodes = new();
        private readonly Dictionary<uint, S57VectorRecord> _connectedNodes = new();
        private readonly Dictionary<uint, S57VectorRecord> _edges = new();
        private readonly Dictionary<uint, S57VectorRecord> _soundingNodes = new();
        private readonly List<PendingObject> _objects = [];
        private readonly Dictionary<uint, List<PendingObject>> _objectsByFeature = new();
        private readonly HashSet<(int Agency, int Fidn, int Fids)> _usedFoids = [];
        private readonly uint _soundingNodeBase;
        private uint _nextVectorId;
        private readonly uint _comf;
        private readonly uint _somf;
        private readonly uint _cmfy;

        public Context(S101ToS57Translator owner, S101Document s101, S101ToS57TranslationDiagnostics? diagnostics)
        {
            _owner = owner;
            _s101 = s101;
            _diagnostics = diagnostics;
            _rules = owner._rules;
            _options = owner._options;
            _soundingNodeBase = s101.Points.Count == 0 ? 0 : s101.Points.Keys.Max();
            // S-57 has one COMF for both axes (DSPM, Part 3 §7.3.2.1), so it
            // takes the S-101 CMFX and Y is rescaled when CMFY differs.
            var structure = s101.StructureInfo;
            _comf = structure.CoordinateMultiplicationFactorX == 0 ? 10_000_000u : structure.CoordinateMultiplicationFactorX;
            _cmfy = structure.CoordinateMultiplicationFactorY == 0 ? _comf : structure.CoordinateMultiplicationFactorY;
            _somf = structure.CoordinateMultiplicationFactorZ == 0 ? 10u : structure.CoordinateMultiplicationFactorZ;
            foreach (var f in s101.Features)
                _usedFoids.Add((f.ProducingAgency, unchecked((int)f.FeatureIdentificationNumber), f.FeatureIdentificationSubdivision));

            // Records the translator creates (cut or buffered areas, surface
            // centres, coverage tiles) take ids past every S-101 one, so they
            // collide with none in any S-57 record name space.
            uint max = _soundingNodeBase + (s101.MultiPoints.Count == 0 ? 0 : s101.MultiPoints.Keys.Max());
            if (s101.CurveSegments.Count > 0) max = Math.Max(max, s101.CurveSegments.Keys.Max());
            _nextVectorId = max + 1;
            _compilationScale = CompilationScale() ?? 0;
        }

        public S57Document Run()
        {
            var (soundingDatum, droppedSoundingDatums) = DatasetDatum("SoundingDatum", "3.9");
            var (verticalDatum, droppedVerticalDatums) = DatasetDatum("VerticalDatumOfData", "3.10");

            PlanStructure();
            foreach (var feature in _s101.Features)
            {
                if (_diagnostics is not null) _diagnostics.FeaturesRead++;
                if (droppedSoundingDatums.Contains(feature.RecordId) || droppedVerticalDatums.Contains(feature.RecordId))
                    continue;
                TranslateFeature(feature);
            }
            EmitStructure();
            ApplyTopology();

            var id = _s101.Identification;

            var vectors = new List<S57VectorRecord>();
            vectors.AddRange(_isolatedNodes.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            vectors.AddRange(_soundingNodes.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            vectors.AddRange(_connectedNodes.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            vectors.AddRange(_edges.OrderBy(kv => kv.Key).Select(kv => kv.Value));

            // S-57 Part 3 §2.4: ATTF and NATF are written at the lowest lexical
            // level that encodes their text.
            var (features, attfLevel, natfLevel) = S101ToS57LexicalLevels.Assign(FinishObjects(), _options.InformSeparator, _diagnostics);
            var issueDate = IssueDate(id.DatasetReferenceDate);
            int? compilationScale = _compilationScale == 0 ? null : _compilationScale;

            // A new ENC base cell (S-57 Part 3 §7.3.1, Appendix B.1 §5): the
            // S-101 identification where it has an S-57 counterpart, the ENC
            // product specification's fixed values otherwise.
            return new S57Document
            {
                DataSetIdentification = new S57DataSetIdentification
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RecordNameCodes.DataSetGeneralInfo, RecordId = 1 },
                    ExchangePurpose = 1, // N: data set is new
                    IntendedUsage = IntendedUsage(id.DatasetName, compilationScale),
                    DataSetName = id.DatasetName,
                    EditionNumber = id.EditionNumber?.ToString(CultureInfo.InvariantCulture) ?? id.DatasetEdition,
                    UpdateNumber = id.UpdateNumber.ToString(CultureInfo.InvariantCulture),
                    UpdateApplicationDate = issueDate,
                    IssueDate = issueDate,
                    S57EditionNumber = "03.1",
                    ProductSpecification = 1, // ENC
                    ProductSpecificationEdition = "2.0",
                    ApplicationProfile = 1, // EN: ENC new
                    ProducingAgency = ProducingAgency(),
                    DataStructure = 2, // chain-node
                    AttfLexicalLevel = attfLevel,
                    NatfLexicalLevel = natfLevel,
                },
                DataSetParameters = new S57DataSetParameters
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RecordNameCodes.DataSetGeoReference, RecordId = 1 },
                    HorizontalDatum = 2, // WGS 84, the only S-101 horizontal CRS
                    CoordinateMultiplicationFactor = (int)_comf,
                    SoundingMultiplicationFactor = (int)_somf,
                    SoundingDatum = soundingDatum ?? 0,
                    VerticalDatum = verticalDatum ?? 0,
                    CompilationScale = compilationScale ?? 0,
                    DepthUnits = 1, // metres
                    HeightUnits = 1, // metres
                    PositionalUnits = 1, // metres
                    CoordinateUnits = 1, // latitude/longitude
                },
                VectorRecords = vectors,
                FeatureRecords = features,
            };
        }

        // DSID ISDT and UADT are YYYYMMDD; S-101 may write the reference date
        // with dashes.
        private static string IssueDate(string date) => date.Replace("-", string.Empty, StringComparison.Ordinal);

        // The cell's producing agency: the commonest feature agency, as S-101
        // has no dataset-level AGEN.
        private int ProducingAgency()
            => _s101.Features
                .Where(f => f.ProducingAgency != 0)
                .GroupBy(f => (int)f.ProducingAgency)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key)
                .FirstOrDefault();

        // §3.5: CSCL is the largest scale (smallest denominator) among the
        // S-101 DataCoverage optimum display scales, or, where none has one,
        // among their maximum display scales.
        private int? CompilationScale()
        {
            int? optimum = null, maximum = null;
            foreach (var feature in _s101.Features)
            {
                if (ClassOf(feature) != "DataCoverage") continue;
                var tree = BuildTree(feature.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings);
                optimum = Smallest(optimum, tree.Find(n => n.Code == "optimumDisplayScale")?.Value);
                maximum = Smallest(maximum, tree.Find(n => n.Code == "maximumDisplayScale")?.Value);
            }
            return optimum ?? maximum;

            static int? Smallest(int? current, string? text)
                => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale) && scale > 0
                    ? current is { } c ? Math.Min(c, scale) : scale
                    : current;
        }

        // INTU (navigational purpose 1–6): the third character of an S-57
        // style cell name (S-57 Appendix B.1 §5.6), else the band the
        // compilation scale falls in.
        private static int IntendedUsage(string name, int? compilationScale)
        {
            if (name.Length >= 8 && name[2] is >= '1' and <= '6' && char.IsAsciiLetterUpper(name[0]))
                return name[2] - '0';
            return compilationScale switch
            {
                null => 0,
                > 1_500_000 => 1,
                > 350_000 => 2,
                > 90_000 => 3,
                > 22_000 => 4,
                > 4_000 => 5,
                _ => 6,
            };
        }

        // §3.9 / §3.10: S-57 states a dataset's sounding and vertical datum once,
        // in DSPM, where S-101 covers the data with SoundingDatum and
        // VerticalDatumOfData features. The commonest verticalDatum among them
        // becomes the dataset's (the "default" Annex C lets a converter assign),
        // and the features that carry it are not converted; the rest become
        // M_SDAT / M_VDAT.
        private (int? Datum, HashSet<uint> Dropped) DatasetDatum(string s101Class, string clause)
        {
            var values = new List<(uint RecordId, string? Value)>();
            foreach (var feature in _s101.Features)
            {
                if (ClassOf(feature) != s101Class) continue;
                var tree = BuildTree(feature.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings);
                values.Add((feature.RecordId, tree.Find(n => n.Code == "verticalDatum")?.Value));
            }

            var dropped = new HashSet<uint>();
            if (values.Count == 0) return (null, dropped);

            var commonest = values
                .Where(v => !string.IsNullOrEmpty(v.Value))
                .GroupBy(v => v.Value!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;
            foreach (var (recordId, value) in values)
            {
                if (values.Count == 1 || value == commonest)
                {
                    dropped.Add(recordId);
                    _diagnostics?.RecordDroppedFeature(s101Class, clause);
                    if (_diagnostics is not null) _diagnostics.FeaturesRead++;
                }
            }

            return (int.TryParse(commonest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var datum) ? datum : null, dropped);
        }

        private string? ClassOf(S101FeatureRecord feature)
            => _s101.FeatureTypeCatalogue.GetValueOrDefault(feature.FeatureTypeCode);

        // ── Features ────────────────────────────────────────────────────

        private void TranslateFeature(S101FeatureRecord feature)
        {
            var s101Class = ClassOf(feature) ?? $"#{feature.FeatureTypeCode}";
            if (DroppedClasses.TryGetValue(s101Class, out var dropClause))
            {
                _diagnostics?.RecordDroppedFeature(s101Class, dropClause);
                return;
            }
            if (_rules.ResolveFeature(s101Class) is not { } baseRule)
            {
                _diagnostics?.RecordUnmappedFeature(s101Class);
                return;
            }

            // A Bridge with spans is converted by its spans (§6.6); a physical
            // AIS aid folds into the structure it equips (§21.2).
            if (IsReplacedByComponents(feature, s101Class))
                return;

            var (primitive, pointers) = TranslateGeometry(feature);
            var tree = BuildTree(feature.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings);
            var (rule, consumed) = Resolve(baseRule, tree, primitive);
            if (consumed is not null)
                tree = tree.Where(n => n.Code != consumed).ToList();
            _primitive = primitive;

            // Table 2-1: primitives S-57 doesn't allow on the class.
            switch (baseRule.PrimitiveChange)
            {
                case S101ToS57PrimitiveChange.DropCurve when primitive == S57GeometricPrimitive.Line:
                    _diagnostics?.RecordDroppedFeature(s101Class, rule.Clause);
                    return;
                case S101ToS57PrimitiveChange.SurfaceToPoint when primitive == S57GeometricPrimitive.Area:
                    (primitive, pointers) = CentrePoint(pointers);
                    _diagnostics?.RecordPrimitiveChanged(s101Class);
                    break;
                case S101ToS57PrimitiveChange.CurveToArea when primitive == S57GeometricPrimitive.Line:
                    (primitive, pointers) = BufferToArea(pointers);
                    _diagnostics?.RecordPrimitiveChanged(s101Class);
                    break;
            }

            // §6.7 / §6.8: a span is a BRIDGE that also carries its Bridge's
            // attributes; the span's own come first, so they win.
            var nodes = tree.Select(n => (Class: s101Class, Node: n)).ToList();
            var bridge = ParentBridge(feature);
            if (bridge is not null)
            {
                rule = _rules.ResolveFeature("Bridge") ?? rule;
                var bridgeTree = BuildTree(bridge.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings);
                nodes.AddRange(bridgeTree.Select(n => (Class: "Bridge", Node: n)));
                tree = [.. tree, .. bridgeTree];
                feature = WithInformation(feature, bridge);
            }

            var bag = new AttributeBag();
            AttributeNode? topmark = null;
            var sectors = new List<AttributeNode>();
            foreach (var (nodeClass, node) in nodes)
            {
                switch (node.Code)
                {
                    case "topmark":
                        topmark ??= node;
                        break;
                    case "sectorCharacteristics":
                        sectors.Add(node);
                        break;
                    default:
                        TranslateTopLevel(nodeClass, node, bag);
                        break;
                }
            }

            FoldInformationTypes(feature, bag);
            AddFixedAttributes(rule, bag);

            if (s101Class == "DataCoverage" && !bag.Has("CATCOV"))
                bag.Add("CATCOV", S101ToS57Rules.S57Attl("CATCOV"), "1"); // §3.5: coverage available

            // A LightSectored: one LIGHTS object per light sector, the first
            // keeping the feature's identity.
            var lightSectors = SplitSectors(sectors);

            // FIDN is a b14 (unsigned) that the S-57 model holds as an int.
            var foid = (Agency: (int)feature.ProducingAgency, Fidn: unchecked((int)feature.FeatureIdentificationNumber), Fids: (int)feature.FeatureIdentificationSubdivision);
            // §2.5.6: the one span of a bridge is the bridge, so its BRIDGE
            // keeps the Bridge's identity.
            if (bridge is not null && IsOnlySpan(bridge))
                foid = (bridge.ProducingAgency, unchecked((int)bridge.FeatureIdentificationNumber), bridge.FeatureIdentificationSubdivision);

            // An aggregation that is also a surface yields the area object as
            // well as its C_AGGR (§5.5, §15.x); the C_AGGR keeps the identity.
            if (S101ToS57Rules.Aggregations.TryGetValue(s101Class, out var aggregation))
            {
                var collection = Emit(feature, rule, S57GeometricPrimitive.None, [], bag, foid);
                if (aggregation.SurfaceAcronym is { } surfaceAcronym && primitive == S57GeometricPrimitive.Area)
                {
                    var surfaceBag = new AttributeBag();
                    foreach (var (acronym, attl, values) in bag.Values)
                    {
                        if (acronym is "OBJNAM" or "NOBJNM")
                            foreach (var v in values) surfaceBag.Add(acronym, attl, v);
                    }
                    var surfaceRule = new S101ToS57FeatureRule(s101Class, S101ToS57Rules.S57Objl(surfaceAcronym), surfaceAcronym, rule.Clause, FrozenDictionary<string, string>.Empty);
                    Emit(feature, surfaceRule, primitive, pointers, surfaceBag, NextFoid(foid));
                    _diagnostics?.RecordSynthesised(surfaceAcronym);
                }
                _aggregations.Add((feature, collection));
                return;
            }

            var primaryBag = lightSectors.Count > 0 ? Merge(bag, lightSectors[0].Attributes) : bag;
            var primary = Emit(feature, rule, primitive, pointers, primaryBag, foid);

            for (int i = 1; i < lightSectors.Count; i++)
            {
                Emit(feature, rule, primitive, pointers, Merge(bag, lightSectors[i].Attributes), NextFoid(foid));
                _diagnostics?.RecordSynthesised(rule.S57Acronym);
            }

            if (topmark is not null)
                EmitTopmark(feature, topmark, tree, pointers, primitive, primary, foid);

            foreach (var companion in baseRule.Companions)
            {
                if (!Matches(companion, tree, primitive)) continue;
                var companionBag = new AttributeBag();
                AddFixedAttributes(companion.Target, companionBag);
                foreach (var (acronym, attl, values) in bag.Values)
                {
                    if (acronym == "SCAMIN") companionBag.Add(acronym, attl, values[0]);
                }
                Emit(feature, companion.Target, primitive, pointers, companionBag, NextFoid(foid));
                _diagnostics?.RecordSynthesised(companion.Target.S57Acronym);
            }
        }

        // The rule for a feature: its class's, or the first redirect whose
        // condition its attributes and primitive meet.
        // Also returns the attribute the redirect used up, if any.
        private static (S101ToS57FeatureRule Rule, string? Consumed) Resolve(S101ToS57FeatureRule rule, List<AttributeNode> tree, S57GeometricPrimitive primitive)
        {
            foreach (var redirect in rule.Redirects)
            {
                if (Matches(redirect, tree, primitive))
                {
                    var target = redirect.Target with { Companions = rule.Companions, PrimitiveChange = rule.PrimitiveChange };
                    return (target, redirect.ConsumesAttribute ? redirect.Attribute : null);
                }
            }
            return (rule, null);
        }

        // The primitive of the feature being translated.
        private S57GeometricPrimitive _primitive;

        private static bool Matches(S101ToS57FeatureRedirect redirect, List<AttributeNode> tree, S57GeometricPrimitive primitive)
        {
            if (redirect.Primitives.Count > 0 && !redirect.Primitives.Contains(primitive))
                return false;
            if (redirect.Attribute is not { } code)
                return true;
            foreach (var node in tree)
            {
                if (node.Code != code) continue;
                var value = node.Value.Trim();
                if (redirect.Values.Count == 0
                    ? value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    : redirect.Values.Contains(value))
                    return true;
            }
            return false;
        }

        // The attributes the class implies: a fixed INFORM text applies only
        // when the feature has no text of its own (e.g. §16.27); the others
        // when the feature didn't supply the attribute, an empty value marking
        // a mandatory attribute with no source (§2.4.3).
        private void AddFixedAttributes(S101ToS57FeatureRule rule, AttributeBag bag)
        {
            foreach (var (acronym, value) in rule.FixedAttributes)
            {
                if (acronym == "INFORM")
                {
                    if (bag.Inform.Count == 0) bag.Inform.Add(value);
                    continue;
                }
                if (bag.Has(acronym)) continue;
                if (S101ToS57Rules.TryGetS57Attl(acronym, out var attl))
                    bag.Add(acronym, attl, value);
                else if (FlatAttl(acronym) is { } flat)
                    bag.Add(acronym, flat, value);
            }
        }

        private AttributeBag Merge(AttributeBag feature, AttributeBag sector)
        {
            var merged = feature.Clone();
            foreach (var (acronym, attl, values) in sector.Values)
            {
                foreach (var value in values)
                    merged.Add(acronym, attl, value);
            }
            merged.Inform.AddRange(sector.Inform);
            merged.Ninfom.AddRange(sector.Ninfom);
            return merged;
        }

        private (int Agency, int Fidn, int Fids) NextFoid((int Agency, int Fidn, int Fids) foid)
        {
            var fids = foid.Fids;
            do
            {
                fids = (fids + 1) & 0xFFFF;
            }
            while (_usedFoids.Contains((foid.Agency, foid.Fidn, fids)) && fids != foid.Fids);
            _usedFoids.Add((foid.Agency, foid.Fidn, fids));
            return (foid.Agency, foid.Fidn, fids);
        }

        private PendingObject Emit(
            S101FeatureRecord? source,
            S101ToS57FeatureRule rule,
            S57GeometricPrimitive primitive,
            IReadOnlyList<S57SpatialPointer> pointers,
            AttributeBag bag,
            (int Agency, int Fidn, int Fids) foid)
        {
            var pending = new PendingObject(rule, primitive, [.. pointers], bag, foid);
            _objects.Add(pending);
            if (source is not null)
            {
                if (!_objectsByFeature.TryGetValue(source.RecordId, out var list))
                    _objectsByFeature[source.RecordId] = list = [];
                list.Add(pending);
            }
            return pending;
        }

        // The S-57 feature records, once every pass has finished with them.
        private List<S57FeatureRecord> FinishObjects()
        {
            var records = new List<S57FeatureRecord>(_objects.Count);
            foreach (var pending in _objects)
            {
                if (pending.Removed) continue;
                var rule = pending.Rule;
                var bag = pending.Bag;
                if (S101ToS57Rules.S57MandatoryAttributes.TryGetValue(rule.S57Acronym, out var mandatory))
                {
                    foreach (var acronym in mandatory)
                    {
                        if (bag.Has(acronym) || (acronym == "INFORM" && bag.Inform.Count > 0)) continue;
                        bag.Add(acronym, S101ToS57Rules.S57Attl(acronym), string.Empty);
                        _diagnostics?.RecordMandatoryNulled(rule.S57Acronym, acronym);
                    }
                }

                var (attributes, national) = Finish(bag);
                records.Add(new S57FeatureRecord
                {
                    RecordName = new S57RecordName
                    {
                        RecordNameCode = FeatureRecordName,
                        RecordId = records.Count + 1,
                        AgencyCode = pending.Foid.Agency,
                        FeatureId = pending.Foid.Fidn,
                        FeatureSubdivision = pending.Foid.Fids,
                    },
                    Primitive = pending.Primitive,
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    Group = rule.Objl >= 300 ? Null : SkinOfTheEarth.Contains(rule.S57Acronym) ? 1 : 2,
                    ObjectCode = (S57ObjectCode)rule.Objl,
                    Attributes = attributes,
                    NationalAttributes = national,
                    SpatialPointers = pending.SpatialPointers,
                    FeaturePointers = pending.FeaturePointers,
                });
                if (_diagnostics is not null) _diagnostics.FeaturesWritten++;
            }
            return records;
        }

        // Turns the assembled values into ATTF and NATF attributes: a List
        // attribute joins its values with commas, any other keeps the first
        // (§2.4.1); the folded text strings join with the separator (§2.4.6).
        private (List<S57AttributeValue> Attributes, List<S57AttributeValue> National) Finish(AttributeBag bag)
        {
            var values = new List<(ushort Attl, string Acronym, string Value)>();
            foreach (var (acronym, attl, list) in bag.Values)
            {
                if (list.Count > 1 && !S101ToS57Rules.IsS57ListAttribute(acronym))
                {
                    _diagnostics?.RecordTruncatedToFirst(acronym);
                    values.Add((attl, acronym, list[0]));
                }
                else
                {
                    values.Add((attl, acronym, string.Join(',', list)));
                }
            }

            AddText("INFORM", bag.Inform);
            AddText("NINFOM", bag.Ninfom);

            var attributes = new List<S57AttributeValue>();
            var national = new List<S57AttributeValue>();
            foreach (var (attl, _, value) in values.OrderBy(v => v.Attl))
            {
                var attribute = new S57AttributeValue { AttributeCode = attl, Value = value };
                (attl >= 300 ? national : attributes).Add(attribute);
            }
            return (attributes, national);

            void AddText(string acronym, List<string> texts)
            {
                if (texts.Count == 0) return;
                var existing = values.FindIndex(v => v.Acronym == acronym);
                var joined = string.Join(_options.InformSeparator, existing < 0 ? texts : [values[existing].Value, .. texts]);
                if (joined.Length > _options.MaxInformLength)
                {
                    if (_diagnostics is not null) _diagnostics.InformOverLength++;
                    if (_options.InformLengthPolicy == S101InformLengthPolicy.Truncate)
                        joined = joined[.._options.MaxInformLength];
                }
                if (existing >= 0) values.RemoveAt(existing);
                values.Add((S101ToS57Rules.S57Attl(acronym), acronym, joined));
            }
        }

        // ── Attributes ──────────────────────────────────────────────────

        private void TranslateTopLevel(string s101Class, AttributeNode node, AttributeBag bag)
        {
            if (node.Code is null)
            {
                _diagnostics?.RecordUnmappedAttribute(s101Class, "#unknown");
                return;
            }

            if (S101ToS57AnnexCClauses.NotConvertedOnClass.TryGetValue((s101Class, node.Code), out var classClause)
                && (_primitive == S57GeometricPrimitive.Area || !GroupOneOnSurface.Contains(s101Class)))
            {
                _diagnostics?.RecordDroppedAttribute(node.Code, classClause);
                return;
            }

            if (_owner._bindings.IsComplexAttribute(node.Code))
            {
                TranslateComplex(s101Class, node, bag);
                return;
            }

            TranslateSimple(s101Class, node.Code, node.Value, bag);
        }

        private void TranslateSimple(string s101Class, string code, string value, AttributeBag bag)
        {
            if (S101ToS57Rules.NotConvertedOnClass.TryGetValue((s101Class, code), out var classClause))
            {
                _diagnostics?.RecordDroppedAttribute(code, classClause);
                return;
            }

            var valueType = _owner._valueTypes.GetValueOrDefault(code);
            if (valueType is "time" or "URI" or "URL" or "URN")
            {
                _diagnostics?.RecordDroppedAttribute(code, valueType == "time" ? "2.4.9" : "2.4.2");
                return;
            }

            if (S101ToS57AnnexCClauses.NotConverted.TryGetValue(code, out var clause)
                || S101ToS57Rules.NotConverted.TryGetValue(code, out clause))
            {
                _diagnostics?.RecordDroppedAttribute(code, clause);
                return;
            }

            switch (code)
            {
                case "mMSICode":
                    _pendingMmsi = value;
                    return;
                case "stationNumber" or "stationName" when s101Class == "TidalStreamPanelData":
                    _pendingTspHeader[code] = value;
                    return;
            }

            if (_rules.ResolveAttribute(s101Class, code) is not { } rule)
            {
                _diagnostics?.RecordUnmappedAttribute(s101Class, code);
                return;
            }

            // An empty Boolean is unknown, and stays an empty S-57 value
            // rather than becoming false.
            if (valueType == "boolean" && value.Length > 0)
                value = NormaliseBoolean(value);
            else if (valueType == "S100_TruncatedDate")
                value = RemoveDashes(value);

            if (rule.InformFormat is { } format)
            {
                // §27: the value folds into INFORM as text.
                if (valueType == "boolean" && value != S101ToS57Rules.BooleanTrue) return;
                bag.Inform.Add(string.Format(CultureInfo.InvariantCulture, format, FormatNumber(value)));
                _diagnostics?.RecordFoldedIntoInform(code);
                return;
            }

            if (rule.InformText.TryGetValue(value, out var text))
            {
                // Table A-2: a value S-57 lacks folds into INFORM as its name.
                if (!bag.Inform.Contains(text)) bag.Inform.Add(text);
                _diagnostics?.RecordFoldedIntoInform(code);
            }

            if (rule.ValueRemap.TryGetValue(value, out var remapped))
            {
                if (remapped is null)
                {
                    if (!rule.InformText.ContainsKey(value))
                        _diagnostics?.RecordDroppedAttribute(code, rule.Clause);
                    return;
                }
                value = remapped;
            }

            if (rule.Attl == 0)
            {
                // No S-57 attribute: only the values that fold into INFORM convert.
                if (!rule.InformText.ContainsKey(value)) _diagnostics?.RecordDroppedAttribute(code, rule.Clause);
                return;
            }

            bag.Add(rule.S57Acronym, rule.Attl, value);
        }

        // §2.4.2: S-57 has no Boolean type. Values are read the way the S-100
        // Lua framework reads them: "1" and "true" are true, anything else is
        // false (with a non-Boolean value reported).
        private string NormaliseBoolean(string value)
        {
            var trimmed = value.Trim();
            if (trimmed is "1" || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
                return S101ToS57Rules.BooleanTrue;
            if (trimmed is not ("0" or "") && !trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
                && _diagnostics is not null)
                _diagnostics.NonBooleanValues++;
            return S101ToS57Rules.BooleanFalse;
        }

        private void Add(AttributeBag bag, string acronym, string? value)
        {
            if (value is null) return;
            var attl = S101ToS57Rules.TryGetS57Attl(acronym, out var known)
                ? known
                : FlatAttl(acronym) ?? throw new InvalidOperationException($"No ATTL for {acronym}.");
            bag.Add(acronym, attl, value);
        }

        private ushort? FlatAttl(string acronym)
        {
            foreach (var rule in _rules.AttributeRules.Values)
            {
                if (rule.S57Acronym == acronym) return rule.Attl;
            }
            return null;
        }

        private void TranslateComplex(string s101Class, AttributeNode node, AttributeBag bag)
        {
            switch (node.Code)
            {
                case "featureName":
                    // Collected by FoldNames once all instances are known.
                    _pendingNames.Add(node);
                    break;
                case "information":
                    FoldInformation(node, bag, "information");
                    break;
                case "multiplicityOfFeatures":
                    FoldMultiplicity(node, bag);
                    break;
                case "rhythmOfLight":
                    Add(bag, "LITCHR", node.Sub("lightCharacteristic"));
                    Add(bag, "SIGGRP", node.Sub("signalGroup"));
                    Add(bag, "SIGPER", node.Sub("signalPeriod"));
                    Add(bag, "SIGSEQ", SignalSequence(node));
                    break;
                case "signalSequence":
                    // Top-level on FogSignal and RadarTransponderBeacon; the
                    // instances are joined once all are seen.
                    _pendingSequences.Add(node);
                    break;
                case "fixedDateRange":
                    AddDateRange(bag, node, "DATSTA", "DATEND");
                    break;
                case "periodicDateRange":
                    AddDateRange(bag, node, "PERSTA", "PEREND");
                    break;
                case "surveyDateRange":
                    AddDateRange(bag, node, "SURSTA", "SUREND");
                    break;
                case "zoneOfConfidence":
                    // §3.8 / §29.42: the category and the uncertainties
                    // convert; the instance with the current date range wins,
                    // approximated by the first (CATZOC is Enumerated).
                    Add(bag, "CATZOC", node.Sub("categoryOfZoneOfConfidenceInData"));
                    Add(bag, "POSACC", node.All("horizontalPositionUncertainty").FirstOrDefault()?.Sub("uncertaintyFixed"));
                    Add(bag, "SOUACC", node.All("verticalUncertainty").FirstOrDefault()?.Sub("uncertaintyFixed"));
                    break;
                case "horizontalPositionUncertainty":
                    // §29.8: POSACC is the fixed uncertainty (the variable
                    // factor has no value to scale here).
                    Add(bag, "POSACC", node.Sub("uncertaintyFixed"));
                    break;
                case "verticalUncertainty":
                    // §29.40: on a depth, the vertical uncertainty is SOUACC.
                    Add(bag, "SOUACC", node.Sub("uncertaintyFixed"));
                    break;
                case "vesselSpeedLimit":
                    // §27.165: the first instance only, as text.
                    if (!_speedLimitDone && node.Sub("speedLimit") is { } limit)
                    {
                        _speedLimitDone = true;
                        var units = node.Sub("speedUnits") switch
                        {
                            "1" => "metres per second",
                            "2" => "kilometres per hour",
                            "3" => "miles per hour",
                            "4" => "knots",
                            _ => string.Empty,
                        };
                        bag.Inform.Add($"Speed limit is {FormatNumber(limit)} {units}".TrimEnd());
                        _diagnostics?.RecordFoldedIntoInform("vesselSpeedLimit");
                    }
                    break;
                case "frequencyPair":
                    // §29.5: the transmitting frequency is SIGFRQ.
                    Add(bag, "SIGFRQ", node.Sub("frequencyShoreStationTransmits"));
                    break;
                case "measuredDistanceValue":
                    // §27.203 / §27.91: a waterway distance as text, e.g. 12.5 kilometres.
                    if (node.Sub("waterwayDistance") is { } distance)
                    {
                        var unit = node.Sub("distanceUnitOfMeasurement") switch
                        {
                            "1" => "metres",
                            "2" => "yards",
                            "3" => "kilometres",
                            "4" => "statute miles",
                            "5" => "nautical miles",
                            _ => string.Empty,
                        };
                        bag.Inform.Add($"{FormatNumber(distance)} {unit}".TrimEnd());
                        _diagnostics?.RecordFoldedIntoInform("measuredDistanceValue");
                    }
                    break;
                case "tidalStreamPanelValues":
                    _pendingPanels.Add(node);
                    break;
                case "featuresDetected" or "onlineResource" or "scheduleByDayOfWeek" or "timeIntervalsByDayOfWeek"
                    or "spatialAccuracy" or "telecommunications":
                    // §29.3, §29.13, §29.18, §29.26, §29.30, §29.33: S-101 enhancements.
                    _diagnostics?.RecordDroppedAttribute(node.Code!, S101ToS57Rules.AttributeClause(node.Code!));
                    break;
                case "surfaceCharacteristics":
                    _pendingSurfaces.Add(node);
                    break;
                case "horizontalClearanceFixed":
                case "horizontalClearanceOpen":
                    Add(bag, "HORCLR", node.Sub("horizontalClearanceValue"));
                    Add(bag, "HORACC", node.Sub("horizontalDistanceUncertainty"));
                    break;
                case "verticalClearanceFixed":
                    AddVerticalClearance(bag, node, "VERCLR");
                    break;
                case "verticalClearanceClosed":
                    AddVerticalClearance(bag, node, "VERCCL");
                    break;
                case "verticalClearanceOpen":
                    AddVerticalClearance(bag, node, "VERCOP");
                    break;
                case "verticalClearanceSafe":
                    AddVerticalClearance(bag, node, "VERCSA");
                    break;
                case "orientation":
                    Add(bag, "ORIENT", node.Sub("orientationValue"));
                    break;
                case "speed":
                    Add(bag, "CURVEL", node.Sub("speedMaximum"));
                    break;
                case "radarWaveLength":
                    if (node.Sub("waveLengthValue") is { } wave && node.Sub("radarBand") is { } band)
                        _pendingRadarWaveLengths.Add($"{wave}-{band}");
                    break;
                case "valueOfLocalMagneticAnomaly":
                    // §27.122: decimal degrees in S-101, decimal minutes in S-57.
                    Add(bag, "VALLMA", DegreesToMinutes(node.Sub("magneticAnomalyValue")));
                    break;
                case "sourceIndication":
                    Add(bag, "SORIND", node.Sub("source"));
                    break;
                default:
                    _diagnostics?.RecordUnmappedAttribute(s101Class, node.Code!);
                    break;
            }
        }

        // Complex attributes whose instances combine into one S-57 attribute,
        // gathered while a feature's attributes are read.
        private readonly List<AttributeNode> _pendingNames = [];
        private readonly List<AttributeNode> _pendingSequences = [];
        private readonly List<AttributeNode> _pendingSurfaces = [];
        private readonly List<string> _pendingRadarWaveLengths = [];
        private readonly List<AttributeNode> _pendingPanels = [];
        private string? _pendingMmsi;
        private bool _speedLimitDone;

        private void AddDateRange(AttributeBag bag, AttributeNode node, string start, string end)
        {
            // §2.4.8: dates convert, without the dashes S-101 uses for an
            // omitted day or month.
            if (node.Sub("dateStart") is { } dateStart) Add(bag, start, RemoveDashes(dateStart));
            if (node.Sub("dateEnd") is { } dateEnd) Add(bag, end, RemoveDashes(dateEnd));
        }

        private void AddVerticalClearance(AttributeBag bag, AttributeNode node, string acronym)
        {
            // verticalClearanceUnlimited = true has no S-57 encoding.
            if (node.Sub("verticalClearanceValue") is { } value)
                Add(bag, acronym, value);
            if (node.All("verticalUncertainty").FirstOrDefault()?.Sub("uncertaintyFixed") is { } accuracy)
                Add(bag, "VERACC", accuracy);
        }

        // §2.1.1 / §27.131: multiplicityOfFeatures has no S-57 attribute; it
        // folds into INFORM as "[n] features", or "Multiple features" when the
        // number isn't known.
        private void FoldMultiplicity(AttributeNode node, AttributeBag bag)
        {
            if (!_options.IncludeOptionalInform)
            {
                _diagnostics?.RecordDroppedAttribute("multiplicityOfFeatures", "2.1.1");
                return;
            }
            var known = NormaliseBoolean(node.Sub("multiplicityKnown") ?? string.Empty) == S101ToS57Rules.BooleanTrue;
            var number = node.Sub("numberOfFeatures");
            bag.Inform.Add(known && !string.IsNullOrEmpty(number) ? $"{number} features" : "Multiple features");
            _diagnostics?.RecordFoldedIntoInform("multiplicityOfFeatures");
        }

        // §2.4.6, §2.4.12.1, §27.177: the text of an `information` instance
        // goes to INFORM when its language is English or empty, otherwise to
        // NINFOM; a fileReference likewise to TXTDSC or NTXTDS.
        private void FoldInformation(AttributeNode node, AttributeBag bag, string source)
        {
            var language = node.Sub("language");
            var english = IsEnglish(language);
            if (node.Sub("text") is { Length: > 0 } text)
            {
                if (english || PrefersNational(language))
                {
                    (english ? bag.Inform : bag.Ninfom).Add(text);
                    _diagnostics?.RecordFoldedIntoInform(source);
                }
            }
            if (node.Sub("fileReference") is { Length: > 0 } file)
            {
                if (english)
                    Add(bag, "TXTDSC", file);
                else if (PrefersNational(language))
                    Add(bag, "NTXTDS", file);
            }
            if (node.Sub("headline") is not null)
                _diagnostics?.RecordDroppedAttribute("headline", "29.9");
        }

        private static bool IsEnglish(string? language)
            => string.IsNullOrEmpty(language) || language.Equals("eng", StringComparison.OrdinalIgnoreCase);

        private bool PrefersNational(string? language)
            => _options.NationalLanguage is null
                || string.Equals(language, _options.NationalLanguage, StringComparison.OrdinalIgnoreCase);

        // Information types linked to the feature: NauticalInformation folds
        // into INFORM/NINFOM/TXTDSC/NTXTDS like an `information` on the feature
        // (§2.4.6, §24.4). Then the collected names, signal sequences, surface
        // characteristics and radar wavelengths are written.
        private void FoldInformationTypes(S101FeatureRecord feature, AttributeBag bag)
        {
            _speedLimitDone = false;
            foreach (var association in feature.InformationAssociations)
            {
                if (!_s101.InformationTypes.TryGetValue(association.RecordId, out var info)) continue;
                var type = _s101.InformationTypeCatalogue.GetValueOrDefault(info.InformationTypeCode) ?? $"#{info.InformationTypeCode}";
                switch (type)
                {
                    case "NauticalInformation" or "ContactDetails":
                        // §24.4: text, files and pictures convert as on the
                        // feature. §24.1: so do call sign, channel and the
                        // contact instructions; addresses, telecommunications
                        // and online resources are S-101 enhancements.
                        foreach (var node in BuildTree(info.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings))
                        {
                            if (node.Code == "information")
                                FoldInformation(node, bag, type);
                            else if (node.Code is "contactAddress" or "onlineResource" or "telecommunications" or "frequencyPair")
                            {
                                if (node.Code == "frequencyPair") TranslateComplex(type, node, bag);
                                else _diagnostics?.RecordDroppedAttribute(node.Code, "24.1");
                            }
                            else if (node.Code is { } code && !_owner._bindings.IsComplexAttribute(code))
                                TranslateSimple(type, code, node.Value, bag);
                            else if (node.Code is { } complex)
                                _diagnostics?.RecordDroppedAttribute(complex, S101ToS57AnnexCClauses.InformationTypes.GetValueOrDefault(type, "24"));
                        }
                        break;
                    case "ServiceHours" or "NonStandardWorkingDay":
                        // §24.2 / §24.3: no S-57 encoding.
                        _diagnostics?.RecordDroppedInformationType(type, S101ToS57AnnexCClauses.InformationTypes.GetValueOrDefault(type, "24"));
                        break;
                    default:
                        _diagnostics?.RecordUnmappedInformationType(type);
                        break;
                }
            }

            FoldNames(bag);

            if (_pendingPanels.Count > 0)
            {
                Add(bag, "TS_TSP", TidalStreamPanel(_pendingTspHeader, _pendingPanels));
                _pendingPanels.Clear();
            }

            if (_pendingSequences.Count > 0)
            {
                Add(bag, "SIGSEQ", JoinSignalSequence(_pendingSequences));
                _pendingSequences.Clear();
            }

            if (_pendingSurfaces.Count > 0)
            {
                // NATSUR/NATQUA are positional lists (S-57 UOC 7.1.2): one
                // position per surfaceCharacteristics instance.
                Add(bag, "NATSUR", PositionalList(_pendingSurfaces, "natureOfSurface"));
                Add(bag, "NATQUA", PositionalList(_pendingSurfaces, "natureOfSurfaceQualifyingTerms"));
                _pendingSurfaces.Clear();
            }

            if (_pendingRadarWaveLengths.Count > 0)
            {
                Add(bag, "RADWAL", string.Join(',', _pendingRadarWaveLengths));
                _pendingRadarWaveLengths.Clear();
            }
        }

        // One position per instance, empty where the instance has no value,
        // without trailing empty positions; null when no instance has one.
        // §12.1: an instance that is an underlying layer follows a "/"
        // rather than a comma (e.g. NATSUR 4,1/9).
        private static string? PositionalList(List<AttributeNode> instances, string code)
        {
            // A position holds one value, so of several qualifying terms the
            // first is kept (§2.4.1).
            var values = instances.Select(i => i.Sub(code) ?? string.Empty).ToList();
            if (values.All(v => v.Length == 0)) return null;
            var text = new System.Text.StringBuilder(values[0]);
            for (int i = 1; i < values.Count; i++)
            {
                bool underlying = NormaliseLayer(instances[i].Sub("underlyingLayer"));
                text.Append(underlying ? '/' : ',').Append(values[i]);
            }
            // Trailing empty positions go, but a "/" stays to mark the layer
            // (§12.1: NATQUA 1,/).
            return text.ToString().TrimEnd(',');
        }

        private static bool NormaliseLayer(string? value)
            => value is not null && (value.Trim() == "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

        // §10.5 / §29.31: TS_TSP is the station number, station name and
        // reference tide (HW or LW), then the direction and rate at each hour
        // from six before to six after it, comma-separated.
        private readonly Dictionary<string, string> _pendingTspHeader = new(StringComparer.Ordinal);

        private string TidalStreamPanel(Dictionary<string, string> header, List<AttributeNode> panels)
        {
            var parts = new List<string>
            {
                header.GetValueOrDefault("stationNumber", string.Empty),
                header.GetValueOrDefault("stationName", string.Empty),
            };
            var panel = panels[0];
            parts.Add(panel.Sub("referenceTide") switch { "1" => "HW", "2" => "LW", _ => string.Empty });
            var byHour = new SortedDictionary<int, AttributeNode>();
            foreach (var value in panel.All("tidalStreamValue"))
            {
                if (int.TryParse(value.Sub("timeRelativeToTide"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour))
                    byHour.TryAdd(hour, value);
            }
            for (int hour = -6; hour <= 6; hour++)
            {
                var value = byHour.GetValueOrDefault(hour);
                parts.Add(value?.All("orientation").FirstOrDefault()?.Sub("orientationValue") ?? value?.Sub("orientationValue") ?? string.Empty);
                parts.Add(value?.All("speed").FirstOrDefault()?.Sub("speedMaximum") ?? value?.Sub("speedMaximum") ?? string.Empty);
            }
            header.Clear();
            return string.Join(',', parts);
        }

        private static string? DegreesToMinutes(string? degrees)
            => decimal.TryParse(degrees, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? FormatNumber((d * 60).ToString(CultureInfo.InvariantCulture))
                : degrees;

        // §2.5.8 / §27.132: nameUsage 1 → OBJNAM, 2 → NOBJNM (by preferred
        // language, else the first). Names with no nameUsage follow
        // NameWithoutUsage.
        private void FoldNames(AttributeBag bag)
        {
            if (_pendingNames.Count == 0 && _pendingMmsi is null) return;
            var names = _pendingNames.Select(n => (Name: n.Sub("name"), Language: n.Sub("language"), Usage: n.Sub("nameUsage")))
                .Where(n => !string.IsNullOrEmpty(n.Name))
                .ToList();
            _pendingNames.Clear();

            string? objnam = names.FirstOrDefault(n => n.Usage == "1").Name;
            var alternates = names.Where(n => n.Usage == "2").ToList();
            string? nobjnm = PreferredNational(alternates);

            var unused = names.Where(n => string.IsNullOrEmpty(n.Usage)).ToList();
            if (unused.Count > 0 && names.All(n => string.IsNullOrEmpty(n.Usage)))
            {
                if (_options.NameWithoutUsage == S101NameWithoutUsage.Drop)
                {
                    _diagnostics?.RecordDroppedAttribute("featureName", "2.5.8");
                }
                else
                {
                    // The first English or untagged name is the default; the
                    // next name, in any language, the national one.
                    var first = unused.FindIndex(n => IsEnglish(n.Language));
                    if (first < 0) first = 0;
                    objnam = unused[first].Name;
                    unused.RemoveAt(first);
                    nobjnm = PreferredNational(unused);
                }
            }

            // §27.129: an MMSI code is appended to the name.
            if (_pendingMmsi is { } mmsi)
            {
                objnam = objnam is null ? $"MMSI {mmsi}" : $"{objnam}; MMSI {mmsi}";
                _pendingMmsi = null;
            }

            Add(bag, "OBJNAM", objnam);
            Add(bag, "NOBJNM", nobjnm);
        }

        private string? PreferredNational(List<(string? Name, string? Language, string? Usage)> candidates)
        {
            if (candidates.Count == 0) return null;
            if (_options.NationalLanguage is { } preferred)
            {
                var match = candidates.Find(n => string.Equals(n.Language, preferred, StringComparison.OrdinalIgnoreCase));
                if (match.Name is not null) return match.Name;
            }
            return candidates[0].Name;
        }

        private static string? SignalSequence(AttributeNode rhythm)
        {
            var sequences = rhythm.All("signalSequence").ToList();
            return sequences.Count == 0 ? null : JoinSignalSequence(sequences);
        }

        // §29.25: the signal sequence instances join with "+", an eclipsed or
        // silent phase (signalStatus 2) in brackets, e.g. 00.8+(02.2).
        private static string JoinSignalSequence(IEnumerable<AttributeNode> sequences)
            => string.Join('+', sequences.Select(s =>
            {
                var duration = FormatDuration(s.Sub("signalDuration"));
                return s.Sub("signalStatus") == "2" ? $"({duration})" : duration;
            }));

        // S-57 SIGSEQ writes each duration with at least two integer digits and
        // one decimal (S-57 Appendix A, SIGSEQ), e.g. 02.0.
        private static string FormatDuration(string? duration)
        {
            if (!decimal.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return duration ?? string.Empty;
            var text = seconds.ToString("00.0##", CultureInfo.InvariantCulture);
            return text;
        }

        // A LightSectored's sectorCharacteristics: each lightSector becomes a
        // LIGHTS object carrying its characteristic, colour, range, visibility,
        // bearings and, for a directional light, ORIENT.
        private List<LightSector> SplitSectors(List<AttributeNode> sectors)
        {
            var result = new List<LightSector>();
            foreach (var characteristics in sectors)
            {
                foreach (var lightSector in characteristics.All("lightSector"))
                {
                    var bag = new AttributeBag();
                    Add(bag, "LITCHR", characteristics.Sub("lightCharacteristic"));
                    Add(bag, "SIGGRP", characteristics.Sub("signalGroup"));
                    Add(bag, "SIGPER", characteristics.Sub("signalPeriod"));
                    Add(bag, "SIGSEQ", SignalSequence(characteristics));
                    foreach (var colour in lightSector.All("colour"))
                        Add(bag, "COLOUR", colour.Value);
                    Add(bag, "VALNMR", lightSector.Sub("valueOfNominalRange"));
                    foreach (var visibility in lightSector.All("lightVisibility"))
                        Add(bag, "LITVIS", visibility.Value);
                    var limit = lightSector.All("sectorLimit").FirstOrDefault();
                    Add(bag, "SECTR1", limit?.All("sectorLimitOne").FirstOrDefault()?.Sub("sectorBearing"));
                    Add(bag, "SECTR2", limit?.All("sectorLimitTwo").FirstOrDefault()?.Sub("sectorBearing"));
                    // §19.3 / §29.1: a directional light is CATLIT 1, and 16 too
                    // when it has a moiré effect.
                    if (lightSector.All("directionalCharacter").FirstOrDefault() is { } directional)
                    {
                        Add(bag, "CATLIT", "1");
                        if (NormaliseBoolean(directional.Sub("moireEffect") ?? string.Empty) == S101ToS57Rules.BooleanTrue)
                            Add(bag, "CATLIT", "16");
                        Add(bag, "ORIENT", directional.All("orientation").FirstOrDefault()?.Sub("orientationValue"));
                    }
                    foreach (var information in lightSector.All("sectorInformation"))
                        FoldInformation(information, bag, "sectorInformation");
                    result.Add(new LightSector(bag));
                }
            }
            return result;
        }

        // §18.1 / §18.2: a topmark becomes a TOPMAR at the structure's position,
        // the structure's slave in a master-to-slave relationship. The
        // structure's date ranges are repeated on it.
        private void EmitTopmark(
            S101FeatureRecord feature,
            AttributeNode topmark,
            List<AttributeNode> tree,
            IReadOnlyList<S57SpatialPointer> pointers,
            S57GeometricPrimitive primitive,
            PendingObject master,
            (int Agency, int Fidn, int Fids) masterFoid)
        {
            var bag = new AttributeBag();
            foreach (var colour in topmark.All("colour"))
                Add(bag, "COLOUR", colour.Value);
            foreach (var pattern in topmark.All("colourPattern"))
                Add(bag, "COLPAT", pattern.Value);
            Add(bag, "TOPSHP", topmark.Sub("topmarkDaymarkShape"));
            foreach (var information in topmark.All("shapeInformation"))
                FoldInformation(information, bag, "shapeInformation");
            foreach (var node in tree)
            {
                if (node.Code == "fixedDateRange") AddDateRange(bag, node, "DATSTA", "DATEND");
                else if (node.Code == "periodicDateRange") AddDateRange(bag, node, "PERSTA", "PEREND");
            }

            var rule = new S101ToS57FeatureRule("topmark", S101ToS57Rules.S57Objl("TOPMAR"), "TOPMAR", "18.1", FrozenDictionary<string, string>.Empty);
            var slave = Emit(feature, rule, primitive, pointers, bag, NextFoid(masterFoid));
            _diagnostics?.RecordSynthesised("TOPMAR");
            AddSlave(master, slave);
        }

        private static void AddSlave(PendingObject master, PendingObject slave)
        {
            var name = new S57LongName((ushort)slave.Foid.Agency, unchecked((uint)slave.Foid.Fidn), (ushort)slave.Foid.Fids);
            if (master.FeaturePointers.Exists(p => p.Name.Equals(name))) return;
            master.FeaturePointers.Add(new S57FeaturePointer { Name = name, Relationship = S57RelationshipIndicator.Slave });
        }

        // ── Geometry ────────────────────────────────────────────────────

        private (S57GeometricPrimitive Primitive, List<S57SpatialPointer> Pointers) TranslateGeometry(S101FeatureRecord feature)
        {
            var pointers = new List<S57SpatialPointer>();
            var primitive = S57GeometricPrimitive.None;
            foreach (var association in feature.SpatialAssociations)
            {
                switch (association.RecordName)
                {
                    case S101RcnmPoint when _s101.Points.TryGetValue(association.RecordId, out var point):
                        primitive = Prefer(primitive, S57GeometricPrimitive.Point);
                        pointers.Add(Pointer(S57RcnmIsolatedNode, IsolatedNode(point), Null, Null));
                        break;
                    case S101RcnmMultiPoint when _s101.MultiPoints.TryGetValue(association.RecordId, out var multi):
                        primitive = Prefer(primitive, S57GeometricPrimitive.Point);
                        pointers.Add(Pointer(S57RcnmIsolatedNode, SoundingNode(multi), Null, Null));
                        break;
                    case S101RcnmCurve or S101RcnmCompositeCurve:
                        primitive = Prefer(primitive, S57GeometricPrimitive.Line);
                        foreach (var (curveId, orientation) in Curves(association.RecordName, association.RecordId, association.Orientation))
                            pointers.Add(Pointer(S57RcnmEdge, Edge(curveId), orientation, Null));
                        break;
                    case S101RcnmSurface when _s101.Surfaces.TryGetValue(association.RecordId, out var surface):
                        primitive = Prefer(primitive, S57GeometricPrimitive.Area);
                        foreach (var ring in surface.RingAssociations)
                        {
                            foreach (var (curveId, orientation) in Curves(ring.RecordName, ring.RecordId, ring.Orientation))
                                pointers.Add(Pointer(S57RcnmEdge, Edge(curveId), orientation, ring.Usage));
                        }
                        break;
                }
            }
            return (primitive, pointers);
        }

        private static S57GeometricPrimitive Prefer(S57GeometricPrimitive current, S57GeometricPrimitive next)
            => current == S57GeometricPrimitive.None ? next : current;

        private static S57SpatialPointer Pointer(int rcnm, uint rcid, int orientation, int usage)
            => new()
            {
                Name = new S57RecordName { RecordNameCode = rcnm, RecordId = (int)rcid },
                Orientation = (S57Orientation)orientation,
                Usage = (S57UsageIndicator)usage,
                Mask = (S57MaskingIndicator)Null,
            };

        // The curve segments of a curve or composite curve, in traversal order,
        // with the orientation each is used in (1 forward, 2 reverse).
        private IEnumerable<(uint CurveId, int Orientation)> Curves(byte rcnm, uint id, byte orientation)
        {
            bool reverse = orientation == 2;
            if (rcnm == S101RcnmCurve)
            {
                if (_s101.CurveSegments.ContainsKey(id))
                    yield return (id, reverse ? 2 : 1);
                yield break;
            }
            if (rcnm != S101RcnmCompositeCurve || !_s101.CompositeCurves.TryGetValue(id, out var composite))
                yield break;

            var components = reverse ? composite.CurveComponents.Reverse() : composite.CurveComponents;
            foreach (var component in components)
            {
                var componentOrientation = (byte)((component.Orientation == 2) ^ reverse ? 2 : 1);
                foreach (var curve in Curves(component.RecordName, component.RecordId, componentOrientation))
                    yield return curve;
            }
        }

        private uint IsolatedNode(S101PointRecord point)
        {
            if (!_isolatedNodes.ContainsKey(point.RecordId))
                _isolatedNodes[point.RecordId] = Node(S57RcnmIsolatedNode, point.RecordId, point);
            return point.RecordId;
        }

        private uint ConnectedNode(uint pointId)
        {
            if (!_connectedNodes.ContainsKey(pointId) && _s101.Points.TryGetValue(pointId, out var point))
                _connectedNodes[pointId] = Node(S57RcnmConnectedNode, pointId, point);
            return pointId;
        }

        private S57VectorRecord Node(int rcnm, uint id, S101PointRecord point)
            => new()
            {
                RecordName = new S57RecordName { RecordNameCode = rcnm, RecordId = (int)id },
                RecordVersion = 1,
                UpdateInstruction = S57UpdateInstruction.Insert,
                VectorPointers = [],
                Coordinates2D = [new S57Coordinate2D { X = point.X, Y = ScaleY(point.Y) }],
                Soundings = [],
                Attributes = [],
            };

        private uint SoundingNode(S101MultiPointRecord multi)
        {
            var id = _soundingNodeBase + multi.RecordId;
            if (!_soundingNodes.ContainsKey(id))
            {
                _soundingNodes[id] = new S57VectorRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RcnmIsolatedNode, RecordId = (int)id },
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    VectorPointers = [],
                    Coordinates2D = [],
                    Soundings = multi.Points.Select(p => new S57Sounding { X = p.X, Y = ScaleY(p.Y), Depth = p.Z }).ToArray(),
                    Attributes = [],
                };
            }
            return id;
        }

        private int ScaleY(int y)
            => _cmfy == _comf ? y : (int)Math.Round((double)y * _comf / _cmfy, MidpointRounding.AwayFromZero);

        private uint Edge(uint curveId)
        {
            if (_edges.ContainsKey(curveId)) return curveId;
            var curve = _s101.CurveSegments[curveId];
            var pointers = new List<S57VectorPointer>();
            foreach (var association in curve.PointAssociations.OrderBy(p => p.Topology))
            {
                var node = ConnectedNode(association.RecordId);
                // S-100 Part 10a TOPI 3 is one point that both begins and ends
                // a closed curve. S-57 VRPT has no such value, so a closed edge
                // points at the same node as its beginning and its end.
                if (association.Topology == 3)
                {
                    pointers.Add(NodePointer(node, S57TopologyIndicator.Beginning));
                    pointers.Add(NodePointer(node, S57TopologyIndicator.End));
                }
                else
                {
                    pointers.Add(NodePointer(node, (S57TopologyIndicator)association.Topology));
                }
            }
            _edges[curveId] = new S57VectorRecord
            {
                RecordName = new S57RecordName { RecordNameCode = S57RcnmEdge, RecordId = (int)curveId },
                RecordVersion = 1,
                UpdateInstruction = S57UpdateInstruction.Insert,
                VectorPointers = pointers,
                Coordinates2D = curve.IntermediateCoordinates.Select(c => new S57Coordinate2D { X = c.X, Y = ScaleY(c.Y) }).ToArray(),
                Soundings = [],
                Attributes = [],
            };
            return curveId;
        }
    }

    // §2.4.2 / §2.4.8: S-57 drops the dashes S-101 writes for an omitted
    // month or day ("2019----" → "2019"); an omitted year ("--0315") keeps its
    // leading dashes, which S-57 shares.
    internal static string RemoveDashes(string value) => value.TrimEnd('-');

    // §27.125: a measurement in text without trailing zeros, e.g. 12.5 or 12.
    internal static string FormatNumber(string value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.##########", CultureInfo.InvariantCulture)
            : value;
}
