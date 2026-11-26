using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// Translates one cell of an S-57 Nautical Information System into an
/// in-memory S-57 base cell (<see cref="S57Document"/>) that
/// <see cref="S57DocumentWriter"/> can encode.
/// </summary>
/// <remarks>
/// <para>
/// The translation works from the source-neutral NIS model, so it is the same
/// whichever <see cref="INisSource"/> the features come from. It interprets the
/// NIS conventions:
/// </para>
/// <list type="bullet">
/// <item><description>Fields named after S-57 attributes (S-57 Appendix A) become attributes,
/// national ones (NOBJNM, NINFOM, NTXTDS) going to NATF. Esri bookkeeping
/// fields are not attributes and are skipped.</description></item>
/// <item><description><c>-32767</c> in a field means the value is unknown, and is written
/// as an empty value (S-57 Part 3 §2.1).</description></item>
/// <item><description>List values stored as comma-separated text keep their commas;
/// enumerations stored as floating-point numbers become integers.</description></item>
/// <item><description>The long name (<c>LNAM</c>) becomes the feature object identifier
/// (FOID); relationships become feature pointers (FFPT).</description></item>
/// <item><description><c>P_QUAPOS</c>, <c>P_POSACC</c> and <c>P_HORDAT</c> on a point
/// become the QUAPOS, POSACC and HORDAT of its isolated node.</description></item>
/// </list>
/// <para>
/// Geometry is rebuilt as S-57 chain-node topology (DSTR 2): points become
/// isolated nodes, and every line and area ring is noded against all the
/// others so that features meeting along a boundary share its edges. Area
/// rings are written exterior first, clockwise, then interior,
/// anticlockwise (S-57 Part 3 §4.7.3). Soundings with the same attributes are
/// grouped into SOUNDG objects. A cell with no M_COVR gets one over the extent
/// of its data.
/// </para>
/// </remarks>
public sealed partial class NisToS57Translator
{
    private const int Null = 255;
    private const ushort FeatureRecordName = 100;
    private const int RcnmIsolatedNode = 110;
    private const int RcnmConnectedNode = 120;
    private const int RcnmEdge = 130;
    private const uint CoordinateFactor = 10_000_000;
    private const uint SoundingFactor = 10;
    private const string UnknownMarker = "-32767";

    // S-57 Group 1 (skin of the earth) object classes (S-57 Appendix B.1 §3.5).
    private static readonly HashSet<string> SkinOfTheEarth = new(StringComparer.Ordinal)
    {
        "DEPARE", "DRGARE", "FLODOC", "HULKES", "LNDARE", "PONTON", "UNSARE",
    };

    // The depth features whose VERDAT is the sounding datum.
    private static readonly HashSet<string> DepthClasses = new(StringComparer.Ordinal)
    {
        "SOUNDG", "DEPARE", "DEPCNT", "DRGARE",
    };

    // Esri fields with names shaped like S-57 attribute acronyms.
    private static readonly HashSet<string> BookkeepingFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "EDITOR",
    };

    // NIS point fields that hold the attributes of the isolated node.
    private static readonly (string Field, string Acronym)[] NodeFields =
    [
        ("P_QUAPOS", "QUAPOS"), ("P_POSACC", "POSACC"), ("P_HORDAT", "HORDAT"),
    ];

    private readonly NisToS57Options _options;

    /// <summary>Creates a translator with the default options.</summary>
    public NisToS57Translator()
        : this(NisToS57Options.Default)
    {
    }

    /// <summary>Creates a translator with the given options.</summary>
    /// <param name="options">The choices the NIS doesn't record.</param>
    public NisToS57Translator(NisToS57Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Translates one cell of an NIS into an S-57 document.</summary>
    /// <param name="source">The NIS.</param>
    /// <returns>The S-57 base cell.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="NisToS57Options.DatasetName"/> was given and the NIS holds
    /// more than one cell, or the cell has no features.
    /// </exception>
    public S57Document Translate(INisSource source) => Translate(source, null);

    /// <summary>
    /// Translates one cell of an NIS into an S-57 document, collecting
    /// diagnostics into <paramref name="diagnostics"/> when it isn't <see langword="null"/>.
    /// </summary>
    /// <param name="source">The NIS.</param>
    /// <param name="diagnostics">Collects what was read, written and dropped.</param>
    /// <returns>The S-57 base cell.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="NisToS57Options.DatasetName"/> was given and the NIS holds
    /// more than one cell, or the cell has no features.
    /// </exception>
    /// <remarks>This method is synchronous and CPU- and I/O-bound.</remarks>
    public S57Document Translate(INisSource source, NisToS57Diagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        var datasetName = _options.DatasetName;
        if (datasetName is null)
        {
            var names = source.GetDatasetNames();
            if (names.Count > 1)
                throw new InvalidOperationException($"The NIS holds {names.Count} cells ({string.Join(", ", names)}); choose one.");
            datasetName = names.Count == 1 ? names[0] : null;
        }
        return new Context(this, source, datasetName, diagnostics ?? new NisToS57Diagnostics()).Run();
    }

    /// <summary>
    /// The cell name for a dataset name: the file name without its extension,
    /// such as <c>US5CA98M</c> for <c>US5CA98M.005</c>.
    /// </summary>
    /// <param name="datasetName">The dataset name.</param>
    /// <returns>The cell name.</returns>
    public static string CellNameOf(string datasetName)
    {
        ArgumentNullException.ThrowIfNull(datasetName);
        var dot = datasetName.LastIndexOf('.');
        return (dot > 0 ? datasetName[..dot] : datasetName).Trim();
    }

    // An S-57 object under construction.
    private sealed class Pending(NisFeature feature, S57ObjectClassDefinition definition)
    {
        public NisFeature Feature { get; } = feature;
        public S57ObjectClassDefinition Definition { get; } = definition;
        public string Acronym => Definition.Acronym;
        public List<S57AttributeValue> Attributes { get; } = [];
        public List<S57AttributeValue> National { get; } = [];
        public List<S57AttributeValue> NodeAttributes { get; } = [];
        public List<S57SpatialPointer> SpatialPointers { get; } = [];
        public List<S57FeaturePointer> FeaturePointers { get; } = [];
        public S57GeometricPrimitive Primitive { get; set; } = S57GeometricPrimitive.None;
        public S57LongName LongName { get; set; }
        public bool Removed { get; set; }
    }

    private sealed partial class Context
    {
        private readonly NisToS57Options _options;
        private readonly INisSource _source;
        private readonly string? _datasetName;
        private readonly NisToS57Diagnostics _diagnostics;
        private readonly S57ObjectCatalogue _catalogue = S57ObjectCatalogue.Iho;
        private readonly Dictionary<string, int> _agencyCodes;
        private readonly List<Pending> _objects = [];
        private readonly Dictionary<string, Pending> _byLongName = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<S57LongName> _usedNames = [];
        private uint _nextVectorId = 1;
        private uint _nextFeatureId = 1;
        private int _producingAgency;

        public Context(NisToS57Translator owner, INisSource source, string? datasetName, NisToS57Diagnostics diagnostics)
        {
            _options = owner._options;
            _source = source;
            _datasetName = datasetName;
            _diagnostics = diagnostics;
            _agencyCodes = new Dictionary<string, int>(NisToS57Options.KnownAgencyCodes, StringComparer.OrdinalIgnoreCase);
            if (_options.AgencyCodes is { } extra)
            {
                foreach (var (token, code) in extra) _agencyCodes[token] = code;
            }
        }

        public S57Document Run()
        {
            var features = _source.GetFeatures(new NisQuery { DatasetName = _datasetName, Bounds = _options.Bounds }).ToList();
            _diagnostics.FeaturesRead = features.Count;
            if (features.Count == 0)
                throw new InvalidOperationException(_datasetName is null ? "The NIS has no features." : $"The NIS has no features from {_datasetName}.");

            // The cell's name as the features spell it, whatever case it was asked for in.
            var sourceName = features.Select(f => f.DatasetName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? _datasetName;
            var cellName = _options.CellName ?? (sourceName is null ? null : CellNameOf(sourceName))
                ?? throw new InvalidOperationException("The NIS features name no cell; give a cell name.");

            var resolved = new List<(NisFeature Feature, S57ObjectClassDefinition Definition)>(features.Count);
            foreach (var feature in features)
            {
                if (_catalogue.TryGetObjectClass(feature.ObjectClass, out var definition) && definition is not null)
                    resolved.Add((feature, definition));
                else
                    _diagnostics.RecordDroppedObjectClass(feature.ObjectClass.Length == 0 ? "(none)" : feature.ObjectClass);
            }

            _producingAgency = _options.ProducingAgency ?? CommonestAgency(resolved.Select(r => r.Feature)) ?? 0;
            foreach (var (feature, definition) in resolved)
            {
                var pending = new Pending(feature, definition);
                AddAttributes(pending);
                _objects.Add(pending);
            }

            var soundings = _objects.Where(o => o.Acronym == "SOUNDG").ToList();
            var others = _objects.Where(o => o.Acronym != "SOUNDG").ToList();
            _objects.Clear();
            _objects.AddRange(others);

            // Identities first, so a sounding group can take its first member's.
            foreach (var pending in others) pending.LongName = Identity(pending);

            var vectors = new List<S57VectorRecord>();
            vectors.AddRange(BuildPoints(others));
            vectors.AddRange(BuildSoundings(soundings));
            bool needsCoverage = _options.SynthesiseCoverage && !others.Any(o => o.Acronym == "M_COVR");
            vectors.AddRange(BuildLinesAndAreas(others, needsCoverage));
            _diagnostics.VectorRecordsWritten = vectors.Count;

            AddRelationships();

            var records = FinishObjects();
            var levelDiagnostics = new S101ToS57TranslationDiagnostics();
            var (features57, attfLevel, natfLevel) = S101ToS57LexicalLevels.Assign(records, ";", levelDiagnostics);
            _diagnostics.UnencodableText = levelDiagnostics.UnencodableText.Values.Sum();
            _diagnostics.FeaturesWritten = features57.Count;

            int compilationScale = _options.CompilationScale ?? Commonest(_objects.Select(o => o.Feature.CompilationScale)) ?? 0;
            var issueDate = (_options.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            int soundingDatum = _options.SoundingDatum ?? CommonestVerdat(o => DepthClasses.Contains(o.Acronym)) ?? 0;
            int verticalDatum = _options.VerticalDatum ?? CommonestVerdat(o => !DepthClasses.Contains(o.Acronym)) ?? soundingDatum;

            // A new ENC base cell (S-57 Part 3 §7.3.1, Appendix B.1 §5).
            return new S57Document
            {
                DataSetIdentification = new S57DataSetIdentification
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RecordNameCodes.DataSetGeneralInfo, RecordId = 1 },
                    ExchangePurpose = 1, // N: data set is new
                    IntendedUsage = IntendedUsage(cellName, compilationScale == 0 ? null : compilationScale),
                    DataSetName = cellName + ".000",
                    EditionNumber = _options.EditionNumber.ToString(CultureInfo.InvariantCulture),
                    UpdateNumber = "0",
                    UpdateApplicationDate = issueDate,
                    IssueDate = issueDate,
                    S57EditionNumber = "03.1",
                    ProductSpecification = 1, // ENC
                    ProductSpecificationEdition = "2.0",
                    ApplicationProfile = 1, // EN: ENC new
                    ProducingAgency = _producingAgency,
                    DataStructure = 2, // chain-node
                    AttfLexicalLevel = attfLevel,
                    NatfLexicalLevel = natfLevel,
                },
                DataSetParameters = new S57DataSetParameters
                {
                    RecordName = new S57RecordName { RecordNameCode = S57RecordNameCodes.DataSetGeoReference, RecordId = 1 },
                    HorizontalDatum = 2, // WGS 84
                    CoordinateMultiplicationFactor = (int)CoordinateFactor,
                    SoundingMultiplicationFactor = (int)SoundingFactor,
                    SoundingDatum = soundingDatum,
                    VerticalDatum = verticalDatum,
                    CompilationScale = compilationScale,
                    DepthUnits = 1, // metres
                    HeightUnits = 1, // metres
                    PositionalUnits = 1, // metres
                    CoordinateUnits = 1, // latitude/longitude
                },
                VectorRecords = vectors,
                FeatureRecords = features57,
            };
        }

        // ── Attributes ──────────────────────────────────────────────────

        private void AddAttributes(Pending pending)
        {
            foreach (var (field, value) in pending.Feature.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (BookkeepingFields.Contains(field)) continue;
                if (!_catalogue.TryGetAttribute(field.ToUpperInvariant(), out var definition) || definition is null)
                {
                    if (AcronymShape().IsMatch(field)) _diagnostics.RecordUnknownAttribute(field);
                    continue;
                }
                if (Format(definition, value) is not { } text) continue;
                var attribute = new S57AttributeValue { AttributeCode = definition.Code, Value = text };
                (definition.Code >= 300 ? pending.National : pending.Attributes).Add(attribute);
            }

            foreach (var (field, acronym) in NodeFields)
            {
                if (!pending.Feature.Attributes.TryGetValue(field, out var value)) continue;
                if (!_catalogue.TryGetAttribute(acronym, out var definition) || definition is null) continue;
                if (Format(definition, value) is { } text)
                    pending.NodeAttributes.Add(new S57AttributeValue { AttributeCode = definition.Code, Value = text });
            }
        }

        // The S-57 text of a stored value, or null when it holds none.
        private string? Format(S57AttributeDefinition definition, object value)
        {
            var text = value switch
            {
                string s => s.Trim(),
                double d => FormatDouble(definition, d),
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                DateTime t => t.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim(),
            };
            if (string.IsNullOrEmpty(text)) return null;
            if (text == UnknownMarker)
            {
                _diagnostics.UnknownValues++;
                return string.Empty;
            }
            if (definition.Type == S57AttributeType.List)
                text = string.Join(',', text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            return text;
        }

        private static string FormatDouble(S57AttributeDefinition definition, double value)
        {
            if (value == -32767) return UnknownMarker;
            bool integral = definition.Type is S57AttributeType.Enumerated or S57AttributeType.Integer or S57AttributeType.List;
            if (integral && Math.Abs(value - Math.Round(value)) < 1e-9)
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            return S101ToS57Translator.FormatNumber(value.ToString("R", CultureInfo.InvariantCulture));
        }

        // ── Identity ────────────────────────────────────────────────────

        private S57LongName Identity(Pending pending, bool synthesised = false)
        {
            var feature = pending.Feature;
            if (NisLongName.TryParse(feature.LongName, out var parsed))
            {
                int? agency = parsed.AgencyCode;
                if (agency is null && parsed.Agency is { } token)
                {
                    if (_agencyCodes.TryGetValue(token, out var code)) agency = code;
                    else _diagnostics.RecordUnknownAgency(token);
                }
                var name = new S57LongName((ushort)(agency ?? _producingAgency), parsed.FeatureId, parsed.FeatureSubdivision);
                if (_usedNames.Add(name))
                {
                    if (feature.LongName is { } text) _byLongName.TryAdd(text.Trim(), pending);
                    return name;
                }
            }
            if (!synthesised) _diagnostics.IdentitiesAssigned++;
            S57LongName assigned;
            do
            {
                assigned = new S57LongName((ushort)_producingAgency, _nextFeatureId++, 1);
            }
            while (!_usedNames.Add(assigned));
            return assigned;
        }

        private int? CommonestAgency(IEnumerable<NisFeature> features)
            => Commonest(features.Select(f =>
            {
                if (!NisLongName.TryParse(f.LongName, out var n)) return (int?)null;
                if (n.AgencyCode is { } code) return code;
                return n.Agency is { } token && _agencyCodes.TryGetValue(token, out var c) ? c : null;
            }));

        // ── Relationships ───────────────────────────────────────────────

        private void AddRelationships()
        {
            foreach (var relationship in _source.GetRelationships())
            {
                if (!_byLongName.TryGetValue(relationship.SourceLongName.Trim(), out var from)
                    || !_byLongName.TryGetValue(relationship.DestinationLongName.Trim(), out var to)
                    || from.Removed || to.Removed || ReferenceEquals(from, to))
                {
                    // Only relationships within the cell are written; a source
                    // may return every relationship in the NIS.
                    if (_byLongName.ContainsKey(relationship.SourceLongName.Trim()) || _byLongName.ContainsKey(relationship.DestinationLongName.Trim()))
                        _diagnostics.RelationshipsUnresolved++;
                    continue;
                }
                var relation = (S57RelationshipIndicator)(int)relationship.Kind;
                if (from.FeaturePointers.Exists(p => p.Name.Equals(to.LongName) && p.Relationship == relation)) continue;
                from.FeaturePointers.Add(new S57FeaturePointer { Name = to.LongName, Relationship = relation });
                _diagnostics.RelationshipsWritten++;
            }
        }

        // ── Records ─────────────────────────────────────────────────────

        private List<S57FeatureRecord> FinishObjects()
        {
            // Meta objects first, then the chart objects, then collections.
            var ordered = _objects
                .Where(o => !o.Removed)
                .OrderBy(o => o.Acronym.StartsWith("M_", StringComparison.Ordinal) ? 0 : o.Acronym.StartsWith("C_", StringComparison.Ordinal) ? 2 : 1)
                .ToList();

            var records = new List<S57FeatureRecord>(ordered.Count);
            foreach (var pending in ordered)
            {
                int objl = pending.Definition.Code;
                int group = objl >= 300 ? Null
                    : SkinOfTheEarth.Contains(pending.Acronym) && pending.Primitive == S57GeometricPrimitive.Area ? 1
                    : 2;
                records.Add(new S57FeatureRecord
                {
                    RecordName = new S57RecordName
                    {
                        RecordNameCode = FeatureRecordName,
                        RecordId = records.Count + 1,
                        AgencyCode = pending.LongName.AgencyCode,
                        FeatureId = unchecked((int)pending.LongName.FeatureId),
                        FeatureSubdivision = pending.LongName.FeatureSubdivision,
                    },
                    Primitive = pending.Primitive,
                    RecordVersion = 1,
                    UpdateInstruction = S57UpdateInstruction.Insert,
                    Group = group,
                    ObjectCode = (S57ObjectCode)objl,
                    Attributes = pending.Attributes.OrderBy(a => a.AttributeCode).ToList(),
                    NationalAttributes = pending.National.OrderBy(a => a.AttributeCode).ToList(),
                    SpatialPointers = pending.SpatialPointers,
                    FeaturePointers = pending.FeaturePointers,
                });
            }
            return records;
        }

        private int? CommonestVerdat(Func<Pending, bool> which)
        {
            if (!_catalogue.TryGetAttribute("VERDAT", out var verdat) || verdat is null) return null;
            return Commonest(_objects.Where(which).Select(o =>
            {
                int at = o.Attributes.FindIndex(a => a.AttributeCode == verdat.Code);
                string? value = at < 0 ? null : o.Attributes[at].Value;
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
            }));
        }

        private static int? Commonest(IEnumerable<int?> values)
            => values
                .OfType<int>()
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => (int?)g.Key)
                .FirstOrDefault();

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

        [GeneratedRegex("^[A-Z0-9]{6}$")]
        private static partial Regex AcronymShape();
    }
}
