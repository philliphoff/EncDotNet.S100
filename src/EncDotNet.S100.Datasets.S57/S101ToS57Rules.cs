using System.Collections.Frozen;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// How an S-101 feature class converts to an S-57 object class in
/// <see cref="S101ToS57Translator"/>.
/// </summary>
/// <param name="S101Code">The S-101 feature class code, e.g. <c>Coastline</c>.</param>
/// <param name="Objl">The S-57 object class code (OBJL).</param>
/// <param name="S57Acronym">The S-57 object class acronym, e.g. <c>COALNE</c>.</param>
/// <param name="Clause">The S-65 Annex C clause the rule follows.</param>
/// <param name="FixedAttributes">
/// S-57 attributes, by acronym, that the S-57 object takes from the class
/// alone, e.g. <c>CATMOR</c> 3 on a <c>MORFAC</c> converted from a
/// <c>Bollard</c>. Empty for most classes.
/// </param>
public sealed record S101ToS57FeatureRule(
    string S101Code,
    ushort Objl,
    string S57Acronym,
    string Clause,
    IReadOnlyDictionary<string, string> FixedAttributes)
{
    /// <summary>
    /// Conditions under which the feature converts to another S-57 class
    /// instead, tried in order; the first that matches wins (e.g. a
    /// <c>Landmark</c> with <c>categoryOfLandmark</c> 26 becomes a
    /// <c>BRIDGE</c>, §7.2).
    /// </summary>
    public IReadOnlyList<S101ToS57FeatureRedirect> Redirects { get; init; } = [];

    /// <summary>
    /// Further S-57 objects created at the feature's position when their
    /// condition matches (e.g. a <c>PILPNT</c> for a landmark standing in the
    /// water, §30). They take the rule's fixed attributes and the feature's
    /// <c>SCAMIN</c>.
    /// </summary>
    public IReadOnlyList<S101ToS57FeatureRedirect> Companions { get; init; } = [];

    /// <summary>How the S-57 object's geometric primitive differs from the S-101 feature's (Annex C Table 2-1).</summary>
    public S101ToS57PrimitiveChange PrimitiveChange { get; init; }
}

/// <summary>
/// How <see cref="S101ToS57Translator"/> changes a feature's geometric
/// primitive where S-57 doesn't allow the S-101 one (Annex C Table 2-1).
/// </summary>
public enum S101ToS57PrimitiveChange
{
    /// <summary>The primitive is kept.</summary>
    None,

    /// <summary>
    /// A curve becomes a narrow area along it, 0.3 mm wide at the compilation
    /// scale (e.g. a curve <c>CollisionRegulationsLimit</c> → <c>CTNARE</c>, §16.27).
    /// </summary>
    CurveToArea,

    /// <summary>A surface becomes a point at its centre (e.g. <c>CGUSTA</c>, §22.3).</summary>
    SurfaceToPoint,

    /// <summary>A curve isn't converted (e.g. a curve <c>LandRegion</c>, §5.11).</summary>
    DropCurve,
}

/// <summary>
/// A condition under which an S-101 feature converts to another S-57 object
/// class (see <see cref="S101ToS57FeatureRule.Redirects"/>), or creates a
/// further object (see <see cref="S101ToS57FeatureRule.Companions"/>).
/// </summary>
/// <param name="Target">The rule to apply when the condition matches.</param>
/// <param name="Attribute">
/// The top-level S-101 simple attribute tested, or <c>null</c> to test the
/// primitive only.
/// </param>
/// <param name="Values">
/// The attribute values that match; empty to match a Boolean attribute that is
/// true.
/// </param>
/// <param name="Primitives">The S-101 primitives that match; empty for any.</param>
public sealed record S101ToS57FeatureRedirect(
    S101ToS57FeatureRule Target,
    string? Attribute,
    IReadOnlyList<string> Values,
    IReadOnlyList<S57GeometricPrimitive> Primitives)
{
    /// <summary>
    /// Whether the tested attribute is used up by the redirect rather than
    /// converted as well (true for most: <c>categoryOfLandmark</c> 22 chooses
    /// <c>CTRPNT</c> and has no <c>CATLMK</c> there; false for a <c>Lake</c>
    /// whose <c>status</c> 5 makes it a <c>RIVERS</c> with <c>STATUS</c> 5).
    /// </summary>
    public bool ConsumesAttribute { get; init; } = true;
}

/// <summary>
/// How an S-101 simple attribute converts to an S-57 attribute in
/// <see cref="S101ToS57Translator"/>.
/// </summary>
/// <param name="S101Code">The S-101 attribute code, e.g. <c>depthRangeMinimumValue</c>.</param>
/// <param name="Attl">The S-57 attribute code (ATTL).</param>
/// <param name="S57Acronym">The S-57 attribute acronym, e.g. <c>DRVAL1</c>.</param>
/// <param name="Clause">The S-65 Annex C clause the rule follows.</param>
/// <param name="ValueRemap">
/// S-101 value → S-57 value. A key mapped to <c>null</c> isn't converted;
/// a missing key passes the value through.
/// </param>
public sealed record S101ToS57AttributeRule(
    string S101Code,
    ushort Attl,
    string S57Acronym,
    string Clause,
    IReadOnlyDictionary<string, string?> ValueRemap)
{
    /// <summary>
    /// S-101 values that S-57 lacks and that fold into <c>INFORM</c> as the
    /// given text instead of converting (Annex C Table A-2), e.g.
    /// <c>categoryOfHulk</c> 6 → "Casino".
    /// </summary>
    public IReadOnlyDictionary<string, string> InformText { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// When set, the whole value folds into <c>INFORM</c> as this composite
    /// format with the value as <c>{0}</c> (e.g. "Maximum draught permitted =
    /// {0} metres", §27.125), and <see cref="Attl"/> is not written.
    /// </summary>
    public string? InformFormat { get; init; }
}

/// <summary>
/// The rule tables for <see cref="S101ToS57Translator"/>: S-101 feature
/// classes and attributes to S-57 object classes and attributes, following
/// S-65 Annex C (<em>S-101 to S-57 ENC Conversion Guidance</em>, Ed 1.0.0).
/// </summary>
/// <remarks>
/// <para>
/// Most rules are derived by inverting the forward mapping
/// (<see cref="S57S101Mapping"/>): every S-57 class or attribute whose default
/// target is an S-101 code converts back to it, and per-value remaps are
/// reversed. Annex C overrides follow for the cases that don't invert, such as
/// the classes a forward redirect reaches (<c>Bollard</c> → <c>MORFAC</c>
/// with <c>CATMOR</c> 3) and the Boolean attributes S-57 encodes as
/// enumerations (§2.4.2). Each rule cites the Annex C clause it follows.
/// </para>
/// <para>
/// Use <see cref="Default"/>, or build a variant with
/// <see cref="WithFeatureRule"/> and <see cref="WithAttributeRule"/>.
/// </para>
/// </remarks>
public sealed partial class S101ToS57Rules
{
    private readonly FrozenDictionary<string, S101ToS57FeatureRule> _features;
    private readonly FrozenDictionary<string, S101ToS57AttributeRule> _attributes;
    private readonly FrozenDictionary<(string Feature, string Attribute), S101ToS57AttributeRule> _featureAttributes;

    private S101ToS57Rules(
        IDictionary<string, S101ToS57FeatureRule> features,
        IDictionary<string, S101ToS57AttributeRule> attributes,
        IDictionary<(string Feature, string Attribute), S101ToS57AttributeRule> featureAttributes)
    {
        _features = features.ToFrozenDictionary(StringComparer.Ordinal);
        _attributes = attributes.ToFrozenDictionary(StringComparer.Ordinal);
        _featureAttributes = featureAttributes.ToFrozenDictionary();
    }

    /// <summary>The rules derived from <see cref="S57S101Mapping.Default"/> plus the Annex C overrides.</summary>
    public static S101ToS57Rules Default { get; } = Build(S57S101Mapping.Default);

    /// <summary>Feature class rules keyed by S-101 feature class code.</summary>
    public IReadOnlyDictionary<string, S101ToS57FeatureRule> FeatureRules => _features;

    /// <summary>Attribute rules keyed by S-101 attribute code, used on any class without a class-specific rule.</summary>
    public IReadOnlyDictionary<string, S101ToS57AttributeRule> AttributeRules => _attributes;

    /// <summary>Attribute rules that apply on one S-101 feature class only, keyed by (feature class, attribute).</summary>
    public IReadOnlyDictionary<(string Feature, string Attribute), S101ToS57AttributeRule> FeatureAttributeRules => _featureAttributes;

    /// <summary>The rule for an S-101 feature class, or <c>null</c> when it has none.</summary>
    public S101ToS57FeatureRule? ResolveFeature(string s101Code)
        => _features.GetValueOrDefault(s101Code);

    /// <summary>
    /// The rule for an S-101 simple attribute on a feature class: the
    /// class-specific rule when there is one, otherwise the general rule.
    /// </summary>
    public S101ToS57AttributeRule? ResolveAttribute(string s101FeatureCode, string s101AttributeCode)
        => _featureAttributes.GetValueOrDefault((s101FeatureCode, s101AttributeCode))
            ?? _attributes.GetValueOrDefault(s101AttributeCode);

    /// <summary>Returns a copy with <paramref name="rule"/> added or replacing the rule for its class.</summary>
    public S101ToS57Rules WithFeatureRule(S101ToS57FeatureRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var features = new Dictionary<string, S101ToS57FeatureRule>(_features, StringComparer.Ordinal) { [rule.S101Code] = rule };
        return new S101ToS57Rules(features, new Dictionary<string, S101ToS57AttributeRule>(_attributes), new Dictionary<(string, string), S101ToS57AttributeRule>(_featureAttributes));
    }

    /// <summary>
    /// Returns a copy with <paramref name="rule"/> added: for one feature class
    /// when <paramref name="s101FeatureCode"/> is given, otherwise as the
    /// general rule for its attribute.
    /// </summary>
    public S101ToS57Rules WithAttributeRule(S101ToS57AttributeRule rule, string? s101FeatureCode = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var attributes = new Dictionary<string, S101ToS57AttributeRule>(_attributes, StringComparer.Ordinal);
        var featureAttributes = new Dictionary<(string, string), S101ToS57AttributeRule>(_featureAttributes);
        if (s101FeatureCode is null)
            attributes[rule.S101Code] = rule;
        else
            featureAttributes[(s101FeatureCode, rule.S101Code)] = rule;
        return new S101ToS57Rules(new Dictionary<string, S101ToS57FeatureRule>(_features, StringComparer.Ordinal), attributes, featureAttributes);
    }

    private static IReadOnlyDictionary<string, string> NoFixedAttributes => FrozenDictionary<string, string>.Empty;

    internal static IReadOnlyDictionary<string, string> NoFixedAttributesValue => NoFixedAttributes;
    private static IReadOnlyDictionary<string, string?> NoRemap => FrozenDictionary<string, string?>.Empty;

    /// <summary>
    /// Builds the rules by inverting <paramref name="forward"/> and applying the
    /// Annex C overrides.
    /// </summary>
    /// <param name="forward">The S-57 → S-101 mapping to invert.</param>
    /// <returns>The reverse rules.</returns>
    public static S101ToS57Rules Build(S57S101Mapping forward)
    {
        ArgumentNullException.ThrowIfNull(forward);

        // ── Feature classes: invert each rule's default target. When two S-57
        // classes default to the same S-101 class, the lower OBJL wins, which
        // keeps the primary S-57 class (none collide in the bundled mapping).
        var features = new Dictionary<string, S101ToS57FeatureRule>(StringComparer.Ordinal);
        foreach (var rule in forward.FeatureRules.Values.OrderBy(r => r.Objl))
        {
            if (rule.DefaultS101Code is not { } code || features.ContainsKey(code))
                continue;
            features[code] = new S101ToS57FeatureRule(
                code, rule.Objl, rule.S57Acronym, FeatureClause(code), NoFixedAttributes);
        }

        // ── Attributes: invert each rule's default target and value remap.
        var attributes = new Dictionary<string, S101ToS57AttributeRule>(StringComparer.Ordinal);
        foreach (var rule in forward.AttributeRules.Values.OrderBy(r => r.Attl))
        {
            if (rule.DefaultS101Code is not { } code || attributes.ContainsKey(code))
                continue;
            attributes[code] = new S101ToS57AttributeRule(
                code, rule.Attl, rule.S57Acronym, AttributeClause(code), Invert(rule.DefaultValueRemap));
        }

        // ── Class-specific attribute rules: invert the per-class overrides
        // whose target differs from the attribute's default, on the class the
        // rule converts back from.
        var featureAttributes = new Dictionary<(string, string), S101ToS57AttributeRule>();
        foreach (var rule in forward.FeatureRules.Values)
        {
            if (rule.DefaultS101Code is not { } featureCode
                || !features.TryGetValue(featureCode, out var reverse)
                || reverse.Objl != rule.Objl)
                continue;
            foreach (var (acronym, ov) in rule.AttributeOverrides)
            {
                if (!forward.TryGetAttl(acronym, out var attl)) continue;
                if (ov.S101Code is { } target)
                {
                    featureAttributes[(featureCode, target)] = new S101ToS57AttributeRule(
                        target, attl, acronym, AttributeClause(target), Invert(ov.ValueRemap));
                }
                foreach (var group in ov.S101CodeByValue.GroupBy(kv => kv.Value, kv => kv.Key))
                {
                    var remap = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var s57Value in group)
                    {
                        var s101Value = ov.ValueRemap.GetValueOrDefault(s57Value) ?? s57Value;
                        remap.TryAdd(s101Value, s57Value);
                    }
                    featureAttributes[(featureCode, group.Key)] = new S101ToS57AttributeRule(
                        group.Key, attl, acronym, AttributeClause(group.Key), remap);
                }
            }
        }

        // ── Annex C overrides ──────────────────────────────────────────────

        // Classes a forward redirect reaches, which the inversion above can't
        // see (only defaults invert). The class implies the S-57 category.
        AddFeature(features, "LightSectored", 75, "LIGHTS");
        AddFeature(features, "LocalDirectionOfBuoyage", 306, "M_NSYS");
        AddFeature(features, "Bollard", 84, "MORFAC", ("CATMOR", "3"));
        AddFeature(features, "MooringBuoy", 84, "MORFAC", ("CATMOR", "7"));

        // §2.4.2 / §2.4.11 and §27: Boolean attributes become S-57 enumerations.
        // The forward mapping carries the S-57 code through unchanged, so the
        // general rules above would pass "true"/"false" on; these replace them.
        AddBoolean(attributes, "radarConspicuous", 82, "CONRAD", "27.146", whenTrue: "1", whenFalse: "2");
        AddBoolean(attributes, "basedOnFixedMarks", 54, "CATTRK", "27.1", whenTrue: "1", whenFalse: "2");
        AddBoolean(attributes, "iMOAdopted", 67, "CATTSS", "27.112", whenTrue: "1", whenFalse: "2");
        AddBoolean(attributes, "distanceMarkVisible", 21, "CATDIS", "27.90", whenTrue: "4", whenFalse: "1");
        AddBoolean(attributes, "openingBridge", 9, "CATBRG", "27.140", whenTrue: "2", whenFalse: "1");

        // §27.142: pictorialRepresentation → PICREP (first value only, §2.4.12.2).
        // §27.154: reportedDate → SORDAT. §27.156: scaleMinimum → SCAMIN.
        AddAttribute(attributes, "reportedDate", 147, "SORDAT");
        // categoryOfOpeningBridge keeps the S-57 CATBRG codes (forward § 4.8.10).
        AddAttribute(attributes, "categoryOfOpeningBridge", 9, "CATBRG");

        AddAnnexC(forward, features, attributes, featureAttributes);

        return new S101ToS57Rules(features, attributes, featureAttributes);
    }

    private static void AddFeature(
        Dictionary<string, S101ToS57FeatureRule> features,
        string s101Code, ushort objl, string acronym, params (string Acronym, string Value)[] fixedAttributes)
    {
        features[s101Code] = new S101ToS57FeatureRule(
            s101Code, objl, acronym, FeatureClause(s101Code),
            fixedAttributes.Length == 0
                ? NoFixedAttributes
                : fixedAttributes.ToDictionary(a => a.Acronym, a => a.Value, StringComparer.Ordinal));
    }

    private static void AddAttribute(
        Dictionary<string, S101ToS57AttributeRule> attributes, string s101Code, ushort attl, string acronym)
        => attributes[s101Code] = new S101ToS57AttributeRule(s101Code, attl, acronym, AttributeClause(s101Code), NoRemap);

    private static void AddBoolean(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        string s101Code, ushort attl, string acronym, string clause, string whenTrue, string whenFalse)
        => attributes[s101Code] = new S101ToS57AttributeRule(
            s101Code, attl, acronym, clause,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [BooleanTrue] = whenTrue,
                [BooleanFalse] = whenFalse,
            });

    // The canonical keys a Boolean value is normalised to before its remap
    // applies (see S101ToS57Translator.NormaliseBoolean).
    internal const string BooleanTrue = "true";
    internal const string BooleanFalse = "false";

    private static IReadOnlyDictionary<string, string?> Invert(IReadOnlyDictionary<string, string?> remap)
    {
        if (remap.Count == 0) return NoRemap;
        var inverse = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (s57Value, s101Value) in remap)
        {
            if (s101Value is not null)
                inverse.TryAdd(s101Value, s57Value);
        }
        return inverse;
    }

    /// <summary>
    /// The Annex C clause under which an S-101 attribute isn't converted on a
    /// feature class: one the class's clause names, one Annex C never
    /// converts, or (for those it doesn't name) a §2.6 enhancement; otherwise
    /// <c>null</c>. Attributes a value remap drops are not included.
    /// </summary>
    /// <param name="s101FeatureCode">The S-101 feature or information type code.</param>
    /// <param name="s101AttributeCode">The S-101 attribute code.</param>
    public static string? NotConvertedClause(string s101FeatureCode, string s101AttributeCode)
        => S101ToS57AnnexCClauses.NotConvertedOnClass.GetValueOrDefault((s101FeatureCode, s101AttributeCode))
            ?? NotConvertedOnClass.GetValueOrDefault((s101FeatureCode, s101AttributeCode))
            ?? S101ToS57AnnexCClauses.NotConverted.GetValueOrDefault(s101AttributeCode)
            ?? NotConverted.GetValueOrDefault(s101AttributeCode);

    internal static string FeatureClause(string s101Code)
        => S101ToS57AnnexCClauses.FeatureTypes.GetValueOrDefault(s101Code, "2.1");

    internal static string AttributeClause(string s101Code)
        => S101ToS57AnnexCClauses.SimpleAttributes.GetValueOrDefault(s101Code)
            ?? S101ToS57AnnexCClauses.ComplexAttributes.GetValueOrDefault(s101Code, "2.4");

    /// <summary>
    /// Attributes Annex C says are not converted on one feature class,
    /// keyed by (S-101 feature class, S-101 attribute), with the clause.
    /// </summary>
    internal static readonly FrozenDictionary<(string Feature, string Attribute), string> NotConvertedOnClass =
        new Dictionary<(string, string), string>
        {
            // §3.7: on LocalDirectionOfBuoyage only for portrayal; the M_NSYS
            // converted from NavigationalSystemOfMarks carries it.
            [("LocalDirectionOfBuoyage", "marksNavigationalSystemOf")] = "3.7",
            // §3.11: enhancements on QualityOfSurvey.
            [("QualityOfSurvey", "techniqueOfVerticalMeasurement")] = "3.11",
            [("QualityOfSurvey", "depthRangeMaximumValue")] = "3.11",
            [("QualityOfSurvey", "depthRangeMinimumValue")] = "3.11",
            // §3.5: optimumDisplayScale converts only onto a synthesised M_CSCL,
            // which Phase 1 does not create.
            [("DataCoverage", "optimumDisplayScale")] = "3.5",
            [("DataCoverage", "maximumDisplayScale")] = "3.5",
            [("DataCoverage", "minimumDisplayScale")] = "3.5",
        }.ToFrozenDictionary();

    /// <summary>
    /// S-101 attributes that have no S-57 counterpart and that Annex C (or,
    /// for those it doesn't name, §2.6 convention 3a) leaves unconverted, in
    /// addition to <see cref="S101ToS57AnnexCClauses.NotConverted"/>.
    /// </summary>
    internal static readonly FrozenDictionary<string, string> NotConverted =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["surroundingDepth"] = "2.6",
            ["defaultClearanceDepth"] = "2.6",
            ["language"] = "27.117",
            ["nameUsage"] = "27.134",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // ── S-57 attribute catalogue facts ──────────────────────────────────

    /// <summary>
    /// Whether the S-57 attribute is of type List (comma-separated values)
    /// rather than Enumerated, per S-57 Ed 3.1 Appendix A.
    /// </summary>
    /// <param name="s57Acronym">The S-57 attribute acronym, e.g. <c>COLOUR</c>.</param>
    public static bool IsS57ListAttribute(string s57Acronym) => S57ListAttributes.Contains(s57Acronym);

    /// <summary>
    /// S-57 attributes of type List (S-57 Ed 3.1 Appendix A, Chapter 2): their
    /// values are comma-separated, so several S-101 values join (§2.4.1). Any
    /// other enumerated S-57 attribute is of type Enumerated and keeps the
    /// first value only. <c>EncDotNet.S57</c> 0.8.1 ships no attribute
    /// catalogue, so the list is held here.
    /// </summary>
    internal static readonly FrozenSet<string> S57ListAttributes = new[]
    {
        "CATACH", "CATAIR", "CATBRG", "CATDPG", "CATHAF", "CATHLK", "CATLMK", "CATLND",
        "CATLIT", "CATMPA", "CATOFP", "CATPIP", "CATREA", "CATROS", "CATRSC", "CATSCF",
        "CATSIT", "CATSIW", "CATSPM", "CATVEG", "COLOUR", "COLPAT", "FUNCTN", "LITVIS",
        "NATCON", "NATQUA", "NATSUR", "PRODCT", "QUASOU", "RESTRN", "STATUS", "SURTYP",
        "TECSOU",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// S-57 attributes that an ENC must populate on an object class (S-57
    /// Appendix B.1 Annex A, and S-58), written empty when the S-101 source has
    /// no value (§2.4.1, §2.4.3), for the classes whose Annex C clause names
    /// a mandatory attribute.
    /// </summary>
    internal static readonly FrozenDictionary<string, string[]> S57MandatoryAttributes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DEPARE"] = ["DRVAL1", "DRVAL2"],
            ["DRGARE"] = ["DRVAL1"],
            ["DEPCNT"] = ["VALDCO"],
            ["M_COVR"] = ["CATCOV"],
            ["M_QUAL"] = ["CATZOC"],
            ["M_CSCL"] = ["CSCALE"],
            ["NEWOBJ"] = ["CLSDEF", "CLSNAM", "SYMINS"],
            ["CTRPNT"] = ["CATCTR"],
            ["BCNSPP"] = ["BCNSHP", "CATSPM", "COLOUR"],
            ["CTNARE"] = ["INFORM"],
            ["LNDMRK"] = ["CATLMK", "CONVIS"],
            ["MORFAC"] = ["CATMOR"],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// S-57 acronym → ATTL for the attributes the translator assembles itself
    /// (from complex attributes, information types and Boolean values), where
    /// the forward mapping has no flat rule to invert.
    /// </summary>
    internal static readonly FrozenDictionary<string, ushort> S57AttributeCodes =
        new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["CATBRG"] = 9,
            ["CATZOC"] = 72,
            ["COLOUR"] = 75,
            ["COLPAT"] = 76,
            ["CURVEL"] = 84,
            ["DATEND"] = 85,
            ["DATSTA"] = 86,
            ["HORCLR"] = 98,
            ["INFORM"] = 102,
            ["LITCHR"] = 107,
            ["LITVIS"] = 108,
            ["MLTYLT"] = 110,
            ["NATSUR"] = 113,
            ["NATQUA"] = 114,
            ["OBJNAM"] = 116,
            ["ORIENT"] = 117,
            ["PEREND"] = 118,
            ["PERSTA"] = 119,
            ["PICREP"] = 120,
            ["RADWAL"] = 126,
            ["SECTR1"] = 136,
            ["SECTR2"] = 137,
            ["SIGGRP"] = 141,
            ["SIGPER"] = 142,
            ["SIGSEQ"] = 143,
            ["SORDAT"] = 147,
            ["SORIND"] = 148,
            ["SUREND"] = 151,
            ["SURSTA"] = 152,
            ["TXTDSC"] = 158,
            ["TOPSHP"] = 171,
            ["VALLMA"] = 175,
            ["VALNMR"] = 178,
            ["VERACC"] = 180,
            ["VERCLR"] = 181,
            ["VERCCL"] = 182,
            ["VERCOP"] = 183,
            ["VERCSA"] = 184,
            ["CATCOV"] = 18,
            ["NINFOM"] = 300,
            ["NOBJNM"] = 301,
            ["NTXTDS"] = 304,
            ["DRVAL1"] = 87,
            ["DRVAL2"] = 88,
            ["VALDCO"] = 174,
            ["CLSDEF"] = 190,
            ["CLSNAM"] = 191,
            ["SYMINS"] = 192,
            ["JRSDTN"] = 103,
            ["CATCTR"] = 16,
            ["CATRUN"] = 58,
            ["CATWED"] = 70,
            ["CATSCF"] = 65,
            ["BCNSHP"] = 2,
            ["CATSPM"] = 66,
            ["CONRAD"] = 82,
            ["CATMOR"] = 40,
            ["CATOBS"] = 42,
            ["WATLEV"] = 187,
            ["QUASOU"] = 125,
            ["CATLIT"] = 37,
            ["CATLMK"] = 35,
            ["CONVIS"] = 83,
            ["CATHAF"] = 30,
            ["CATVEG"] = 68,
            ["CSCALE"] = 80,
            ["CATACH"] = 8,
            ["STATUS"] = 149,
            ["POSACC"] = 401,
            ["SOUACC"] = 144,
            ["QUAPOS"] = 402,
            ["CALSGN"] = 6,
            ["COMCHA"] = 77,
            ["SIGFRQ"] = 139,
            ["TS_TSP"] = 159,
            ["PILDST"] = 121,
            ["NPLDST"] = 302,
            ["HORACC"] = 97,
            ["VALSOU"] = 179,
            ["SCAMIN"] = 133,
            ["NATION"] = 111,
        }.ToFrozenDictionary(StringComparer.Ordinal);
}
