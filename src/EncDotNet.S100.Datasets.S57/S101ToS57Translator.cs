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
/// the general rules (Annex C §2), the attribute mapping of the meta features
/// (§3) and the commonest feature and attribute classes.
/// </summary>
/// <remarks>
/// <para>
/// The output uses the <c>EncDotNet.S57</c> record types, so a writer for
/// those types can encode it. Geometry converts feature by feature: an S-101
/// point becomes an isolated node, a curve segment an edge between connected
/// nodes, and a surface its rings of edges. Chain-node topology clean-up, the
/// skin-of-earth cut (§2.5.1.1) and the <c>M_COVR</c>/<c>M_CSCL</c> tiling
/// (§3.5) are not done yet.
/// </para>
/// <para>
/// The structural inverses of the forward translation are not done yet
/// either: a <c>SpanFixed</c> or <c>SpanOpening</c> isn't folded back into
/// its <c>BRIDGE</c>, nor a <c>RangeSystem</c> into a <c>C_AGGR</c>. They are
/// reported as deferred in <see cref="S101ToS57TranslationDiagnostics.DeferredFeatures"/>.
/// </para>
/// </remarks>
public sealed class S101ToS57Translator
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

    // Feature classes whose inverse is structural and comes in Phase 2.
    private static readonly FrozenSet<string> DeferredClasses = new[]
    {
        "SpanFixed", "SpanOpening", "RangeSystem",
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

    private sealed class Context
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
        private readonly List<S57FeatureRecord> _features = [];
        private readonly HashSet<(int Agency, int Fidn, int Fids)> _usedFoids = [];
        private readonly uint _soundingNodeBase;
        private int _nextRecordId = 1;

        public Context(S101ToS57Translator owner, S101Document s101, S101ToS57TranslationDiagnostics? diagnostics)
        {
            _owner = owner;
            _s101 = s101;
            _diagnostics = diagnostics;
            _rules = owner._rules;
            _options = owner._options;
            _soundingNodeBase = s101.Points.Count == 0 ? 0 : s101.Points.Keys.Max();
            foreach (var f in s101.Features)
                _usedFoids.Add((f.ProducingAgency, (int)f.FeatureIdentificationNumber, f.FeatureIdentificationSubdivision));
        }

        public S57Document Run()
        {
            var (soundingDatum, droppedSoundingDatums) = DatasetDatum("SoundingDatum", "3.9");
            var (verticalDatum, droppedVerticalDatums) = DatasetDatum("VerticalDatumOfData", "3.10");

            foreach (var feature in _s101.Features)
            {
                if (_diagnostics is not null) _diagnostics.FeaturesRead++;
                if (droppedSoundingDatums.Contains(feature.RecordId) || droppedVerticalDatums.Contains(feature.RecordId))
                    continue;
                TranslateFeature(feature);
            }

            var structure = _s101.StructureInfo;
            var comf = structure.CoordinateMultiplicationFactorX == 0 ? 10_000_000u : structure.CoordinateMultiplicationFactorX;
            var somf = structure.CoordinateMultiplicationFactorZ == 0 ? 10u : structure.CoordinateMultiplicationFactorZ;
            var id = _s101.Identification;

            var vectors = new List<S57VectorRecord>();
            vectors.AddRange(_isolatedNodes.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            vectors.AddRange(_soundingNodes.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            vectors.AddRange(_connectedNodes.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            vectors.AddRange(_edges.OrderBy(kv => kv.Key).Select(kv => kv.Value));

            return new S57Document
            {
                DataSetIdentification = new S57DataSetIdentification
                {
                    DataSetName = id.DatasetName,
                    EditionNumber = id.EditionNumber?.ToString(CultureInfo.InvariantCulture) ?? id.DatasetEdition,
                    UpdateNumber = id.UpdateNumber.ToString(CultureInfo.InvariantCulture),
                    IssueDate = RemoveDashes(id.DatasetReferenceDate),
                },
                DataSetParameters = new S57DataSetParameters
                {
                    CoordinateMultiplicationFactor = (int)comf,
                    SoundingMultiplicationFactor = (int)somf,
                    SoundingDatum = soundingDatum ?? 0,
                    VerticalDatum = verticalDatum ?? 0,
                },
                VectorRecords = vectors,
                FeatureRecords = _features,
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
            if (DeferredClasses.Contains(s101Class))
            {
                _diagnostics?.RecordDeferredFeature(s101Class);
                return;
            }
            if (_rules.ResolveFeature(s101Class) is not { } rule)
            {
                _diagnostics?.RecordUnmappedFeature(s101Class);
                return;
            }

            var (primitive, pointers) = TranslateGeometry(feature);
            var tree = BuildTree(feature.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings);

            var bag = new AttributeBag();
            AttributeNode? topmark = null;
            var sectors = new List<AttributeNode>();
            foreach (var node in tree)
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
                        TranslateTopLevel(s101Class, node, bag);
                        break;
                }
            }

            FoldInformationTypes(feature, bag);

            foreach (var (acronym, value) in rule.FixedAttributes)
            {
                if (!bag.Has(acronym) && S101ToS57Rules.S57AttributeCodes.TryGetValue(acronym, out var attl))
                    bag.Add(acronym, attl, value);
                else if (!bag.Has(acronym) && FlatAttl(acronym) is { } flat)
                    bag.Add(acronym, flat, value);
            }

            if (s101Class == "DataCoverage" && !bag.Has("CATCOV"))
                bag.Add("CATCOV", S101ToS57Rules.S57AttributeCodes["CATCOV"], "1"); // §3.5: coverage available

            // A LightSectored: one LIGHTS object per light sector, the first
            // keeping the feature's identity.
            var lightSectors = SplitSectors(sectors);

            var foid = (Agency: (int)feature.ProducingAgency, Fidn: (int)feature.FeatureIdentificationNumber, Fids: (int)feature.FeatureIdentificationSubdivision);
            var primaryBag = lightSectors.Count > 0 ? Merge(bag, lightSectors[0].Attributes) : bag;
            var primary = Emit(rule, primitive, pointers, primaryBag, foid);

            for (int i = 1; i < lightSectors.Count; i++)
            {
                Emit(rule, primitive, pointers, Merge(bag, lightSectors[i].Attributes), NextFoid(foid));
                _diagnostics?.RecordSynthesised(rule.S57Acronym);
            }

            if (topmark is not null)
                EmitTopmark(topmark, tree, pointers, primitive, primary, foid);
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

        private int Emit(
            S101ToS57FeatureRule rule,
            S57GeometricPrimitive primitive,
            IReadOnlyList<S57SpatialPointer> pointers,
            AttributeBag bag,
            (int Agency, int Fidn, int Fids) foid,
            IReadOnlyList<S57FeaturePointer>? featurePointers = null)
        {
            if (S101ToS57Rules.S57MandatoryAttributes.TryGetValue(rule.S57Acronym, out var mandatory))
            {
                foreach (var acronym in mandatory)
                {
                    if (bag.Has(acronym)) continue;
                    bag.Add(acronym, S101ToS57Rules.S57AttributeCodes[acronym], string.Empty);
                    _diagnostics?.RecordMandatoryNulled(rule.S57Acronym, acronym);
                }
            }

            var (attributes, national) = Finish(bag);
            var index = _features.Count;
            _features.Add(new S57FeatureRecord
            {
                RecordName = new S57RecordName
                {
                    RecordNameCode = FeatureRecordName,
                    RecordId = _nextRecordId++,
                    AgencyCode = foid.Agency,
                    FeatureId = foid.Fidn,
                    FeatureSubdivision = foid.Fids,
                },
                Primitive = primitive,
                Group = rule.Objl >= 300 ? Null : SkinOfTheEarth.Contains(rule.S57Acronym) ? 1 : 2,
                ObjectCode = (S57ObjectCode)rule.Objl,
                Attributes = attributes,
                NationalAttributes = national,
                SpatialPointers = pointers,
                FeaturePointers = featurePointers ?? [],
            });
            if (_diagnostics is not null) _diagnostics.FeaturesWritten++;
            return index;
        }

        // Turns the assembled values into ATTF and NATF attributes: a List
        // attribute joins its values with commas, any other keeps the first
        // (§2.4.1); the folded text strings join with the separator (§2.4.6).
        private (List<S57AttributeValue> Attributes, List<S57AttributeValue> National) Finish(AttributeBag bag)
        {
            var values = new List<(ushort Attl, string Acronym, string Value)>();
            foreach (var (acronym, attl, list) in bag.Values)
            {
                if (list.Count > 1 && !S101ToS57Rules.S57ListAttributes.Contains(acronym))
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
                values.Add((S101ToS57Rules.S57AttributeCodes[acronym], acronym, joined));
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

            if (_rules.ResolveAttribute(s101Class, code) is not { } rule)
            {
                _diagnostics?.RecordUnmappedAttribute(s101Class, code);
                return;
            }

            if (valueType == "boolean")
                value = NormaliseBoolean(value);
            else if (valueType == "S100_TruncatedDate")
                value = RemoveDashes(value);

            if (rule.ValueRemap.TryGetValue(value, out var remapped))
            {
                if (remapped is null)
                {
                    _diagnostics?.RecordDroppedAttribute(code, rule.Clause);
                    return;
                }
                value = remapped;
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
            var attl = S101ToS57Rules.S57AttributeCodes.TryGetValue(acronym, out var known)
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
                    // §3.8: only the category converts; the instance with the
                    // current date range wins, which Phase 1 approximates by
                    // the first (CATZOC is Enumerated).
                    Add(bag, "CATZOC", node.Sub("categoryOfZoneOfConfidenceInData"));
                    break;
                case "surfaceCharacteristics":
                    _pendingSurfaces.Add(node);
                    break;
                case "horizontalClearanceFixed":
                case "horizontalClearanceOpen":
                    Add(bag, "HORCLR", node.Sub("horizontalClearanceValue"));
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
                    Add(bag, "VALLMA", node.Sub("magneticAnomalyValue"));
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
            foreach (var association in feature.InformationAssociations)
            {
                if (!_s101.InformationTypes.TryGetValue(association.RecordId, out var info)) continue;
                var type = _s101.InformationTypeCatalogue.GetValueOrDefault(info.InformationTypeCode) ?? $"#{info.InformationTypeCode}";
                if (type != "NauticalInformation")
                {
                    _diagnostics?.RecordUnmappedInformationType(type);
                    continue;
                }
                foreach (var node in BuildTree(info.Attributes, _s101.AttributeTypeCatalogue, _owner._bindings))
                {
                    if (node.Code == "information")
                        FoldInformation(node, bag, "NauticalInformation");
                    else if (node.Code is { } code)
                        TranslateSimple(type, code, node.Value, bag);
                }
            }

            FoldNames(bag);

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
        private static string? PositionalList(List<AttributeNode> instances, string code)
        {
            var values = instances.Select(i => i.Sub(code) ?? string.Empty).ToList();
            while (values.Count > 0 && values[^1].Length == 0)
                values.RemoveAt(values.Count - 1);
            return values.Count == 0 ? null : string.Join(',', values);
        }

        // §2.5.8 / §27.132: nameUsage 1 → OBJNAM, 2 → NOBJNM (by preferred
        // language, else the first). Names with no nameUsage follow
        // NameWithoutUsage.
        private void FoldNames(AttributeBag bag)
        {
            if (_pendingNames.Count == 0) return;
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
            AttributeNode topmark,
            List<AttributeNode> tree,
            IReadOnlyList<S57SpatialPointer> pointers,
            S57GeometricPrimitive primitive,
            int masterIndex,
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

            var foid = NextFoid(masterFoid);
            var rule = new S101ToS57FeatureRule("topmark", 144, "TOPMAR", "18.1", FrozenDictionary<string, string>.Empty);
            Emit(rule, primitive, pointers, bag, foid);
            _diagnostics?.RecordSynthesised("TOPMAR");

            var master = _features[masterIndex];
            _features[masterIndex] = new S57FeatureRecord
            {
                RecordName = master.RecordName,
                Primitive = master.Primitive,
                Group = master.Group,
                ObjectCode = master.ObjectCode,
                Attributes = master.Attributes,
                NationalAttributes = master.NationalAttributes,
                SpatialPointers = master.SpatialPointers,
                FeaturePointers =
                [
                    .. master.FeaturePointers,
                    new S57FeaturePointer
                    {
                        Name = new S57LongName((ushort)foid.Agency, (uint)foid.Fidn, (ushort)foid.Fids),
                        Relationship = S57RelationshipIndicator.Slave,
                    },
                ],
            };
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

        private static S57VectorRecord Node(int rcnm, uint id, S101PointRecord point)
            => new()
            {
                RecordName = new S57RecordName { RecordNameCode = rcnm, RecordId = (int)id },
                VectorPointers = [],
                Coordinates2D = [new S57Coordinate2D { X = point.X, Y = point.Y }],
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
                    VectorPointers = [],
                    Coordinates2D = [],
                    Soundings = multi.Points.Select(p => new S57Sounding { X = p.X, Y = p.Y, Depth = p.Z }).ToArray(),
                    Attributes = [],
                };
            }
            return id;
        }

        private uint Edge(uint curveId)
        {
            if (_edges.ContainsKey(curveId)) return curveId;
            var curve = _s101.CurveSegments[curveId];
            var pointers = new List<S57VectorPointer>();
            foreach (var association in curve.PointAssociations.OrderBy(p => p.Topology))
            {
                pointers.Add(new S57VectorPointer
                {
                    Name = new S57RecordName { RecordNameCode = S57RcnmConnectedNode, RecordId = (int)ConnectedNode(association.RecordId) },
                    Orientation = (S57Orientation)Null,
                    Usage = (S57UsageIndicator)Null,
                    Topology = (S57TopologyIndicator)association.Topology,
                    Mask = (S57MaskingIndicator)Null,
                });
            }
            _edges[curveId] = new S57VectorRecord
            {
                RecordName = new S57RecordName { RecordNameCode = S57RcnmEdge, RecordId = (int)curveId },
                VectorPointers = pointers,
                Coordinates2D = curve.IntermediateCoordinates.Select(c => new S57Coordinate2D { X = c.X, Y = c.Y }).ToArray(),
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
}
