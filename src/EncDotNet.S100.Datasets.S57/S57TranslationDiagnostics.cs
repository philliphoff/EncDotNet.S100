namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Identifies a specific S-57 attribute drop, distinguishing the owning S-57
/// object class from the attribute code so a corpus-wide audit can tell whether
/// an unmapped attribute matters (a critical attribute on a common feature) or
/// is incidental (an attribute the S-101 destination class never carries).
/// </summary>
/// <param name="ObjectClass">S-57 numeric object class (OBJL) that owned the attribute.</param>
/// <param name="AttributeCode">S-57 numeric attribute code (ATTL) that was dropped.</param>
public readonly record struct S57AttributeDrop(ushort ObjectClass, ushort AttributeCode);

/// <summary>
/// Identifies an S-101 enumerated value that was dropped during translation
/// because the destination S-101 Feature Catalogue does not list it as an
/// allowable code for the target attribute (see
/// <see cref="S101AllowedEnumValues"/>).
/// </summary>
/// <param name="S101Attribute">Target S-101 simple-attribute code.</param>
/// <param name="Value">The (already remapped) value that the FC rejected.</param>
public readonly record struct S57EnumValueDrop(string S101Attribute, string Value);

/// <summary>
/// Optional, opt-in diagnostics collector for
/// <see cref="S57ToS101Translator.Translate(EncDotNet.S57.S57Document, S57TranslationDiagnostics?)"/>.
/// It records — as compact aggregate counters rather than per-instance events —
/// exactly what the translator dropped and why, so a caller (e.g. a corpus-wide
/// conversion audit) can quantify coverage gaps in the embedded
/// <see cref="S57S101Mapping"/> without re-deriving the translator's logic
/// externally.
/// </summary>
/// <remarks>
/// <para>
/// A fresh instance is created per translation. Passing <c>null</c> to the
/// translator disables collection entirely (the default parameterless
/// <see cref="S57ToS101Translator.Translate(EncDotNet.S57.S57Document)"/>
/// overload does exactly that), so there is no cost on the normal render path.
/// </para>
/// <para>
/// The counters distinguish the two conversion-guidance outcomes that both
/// manifest as "the feature/attribute is absent from the S-101 output":
/// a genuine <em>gap</em> (no rule exists — see
/// <see cref="UnmappedObjectClasses"/> / <see cref="UnmappedAttributes"/>) versus
/// a <em>by-design drop</em> (a rule exists but resolves to nothing — see
/// <see cref="RuleDroppedObjectClasses"/> / <see cref="RuleDroppedAttributes"/>).
/// </para>
/// </remarks>
public sealed class S57TranslationDiagnostics
{
    private readonly Dictionary<ushort, int> _unmappedObjl = new();
    private readonly Dictionary<ushort, int> _ruleDroppedObjl = new();
    private readonly Dictionary<string, int> _noGeometry = new(StringComparer.Ordinal);
    private readonly Dictionary<S57AttributeDrop, int> _unmappedAttributes = new();
    private readonly Dictionary<ushort, int> _ruleDroppedAttributes = new();
    private readonly Dictionary<S57EnumValueDrop, int> _droppedEnumValues = new();

    /// <summary>
    /// Number of non-sounding S-57 feature records the translator iterated,
    /// including records later absorbed, deferred, dropped, or unmapped.
    /// </summary>
    public int FeatureRecordsRead { get; internal set; }

    /// <summary>
    /// Number of S-101 feature records emitted from non-sounding S-57 features
    /// (i.e. features that resolved to an S-101 class <em>and</em> produced
    /// geometry).
    /// </summary>
    public int FeaturesEmitted { get; internal set; }

    /// <summary>Number of S-57 SOUNDG (OBJL=129) feature records seen.</summary>
    public int SoundingFeaturesRead { get; internal set; }

    /// <summary>
    /// Number of co-located S-57 sector-light (<c>LIGHTS</c> with
    /// <c>SECTR1</c>/<c>SECTR2</c>) feature records absorbed into a neighbouring
    /// sector light at the same spatial node during the sector-light merge pass.
    /// An absorbed record produces no S-101 feature of its own, so it is counted
    /// here <em>instead of</em> in <see cref="FeaturesEmitted"/>. It usually adds
    /// a <c>sectorCharacteristics</c> instance to the surviving
    /// <c>LightSectored</c> feature, but members with missing or FC-rejected
    /// <c>LITCHR</c> contribute no instance and have their diverted sector inputs
    /// dropped to match the single-feature path. This counter therefore reflects
    /// records absorbed, which may exceed the instances actually added.
    /// </summary>
    public int SectorLightsMerged { get; internal set; }

    /// <summary>
    /// Number of S-57 <c>TOPMAR</c> (topmark/daymark, OBJL 144) feature records
    /// absorbed by a master buoy/beacon during the topmark-fold pass. S-101 models
    /// the topmark as an attribute of the parent structure (reached via the S-57
    /// master/slave feature-to-feature relationship) rather than a standalone
    /// feature, so an absorbed TOPMAR produces no S-101 feature of its own and is
    /// counted here <em>instead of</em> under <see cref="UnmappedObjectClasses"/>.
    /// This counts every TOPMAR consumed by a topmark-binding master; the parent's
    /// <c>topmark</c> complex instance may still be dropped afterward (for example
    /// a missing or FC-rejected <c>TOPSHP</c>), so an absorbed TOPMAR does not
    /// guarantee an emitted <c>topmark</c> instance. A TOPMAR referenced by no
    /// topmark-binding master is still recorded as unmapped (IHO S-57→S-101
    /// Conversion Guidance: TOPMAR → parent attribute).
    /// </summary>
    public int TopmarksAbsorbed { get; internal set; }

    /// <summary>
    /// Number of <c>NauticalInformation</c> information-type records emitted by
    /// the S-57→S-101 conversion. One is created per feature that carries any of
    /// the textual attributes <c>INFORM</c>/<c>TXTDSC</c>/<c>NINFOM</c>/
    /// <c>NTXTDS</c>, bound to that feature through an
    /// <c>AdditionalInformation</c> association (IHO S-57→S-101 Conversion
    /// Guidance §2.3 "fuller path"). Equals the number of
    /// <c>AdditionalInformation</c> associations emitted.
    /// </summary>
    public int NauticalInformationTypesEmitted { get; internal set; }

    /// <summary>
    /// Number of synthesised <c>RangeSystem</c> collection features emitted by
    /// the S-57→S-101 conversion. S-101 has no generic collection object; an
    /// S-57 <c>C_AGGR</c> whose members are navigational tracks plus the
    /// navigation aids that define them is mapped to a geometry-less
    /// <c>RangeSystem</c> feature linked to each member by a
    /// <c>RangeSystemAggregation</c> association in the member's
    /// <c>theComponent</c> role (S-101 FC Ed 1.x). C_AGGR groupings that do not
    /// match this pattern (and all <c>C_ASSO</c>) have no S-101 home and are
    /// counted under <see cref="UnmappedObjectClasses"/> instead.
    /// </summary>
    public int RangeSystemsEmitted { get; internal set; }

    /// <summary>
    /// Number of S-101 <c>Sounding</c> features emitted (a SOUNDG feature with at
    /// least one depth triple yields exactly one).
    /// </summary>
    public int SoundingFeaturesEmitted { get; internal set; }

    /// <summary>Total number of depth points emitted across all soundings.</summary>
    public int SoundingPointsEmitted { get; internal set; }

    /// <summary>
    /// Number of SOUNDG feature records dropped because no resolvable depth
    /// triples were found on their referenced vector records.
    /// </summary>
    public int SoundingFeaturesWithoutPoints { get; internal set; }

    /// <summary>
    /// S-57 object classes (OBJL → occurrence count) that were dropped because
    /// the mapping has <em>no rule</em> for them (a coverage gap).
    /// </summary>
    public IReadOnlyDictionary<ushort, int> UnmappedObjectClasses => _unmappedObjl;

    /// <summary>
    /// S-57 object classes (OBJL → occurrence count) that <em>have</em> a rule
    /// which resolved to no S-101 class — either an intentional drop
    /// (<c>DefaultS101Code == null</c>) or a redirect that did not match the
    /// instance's attributes.
    /// </summary>
    public IReadOnlyDictionary<ushort, int> RuleDroppedObjectClasses => _ruleDroppedObjl;

    /// <summary>
    /// Resolved S-101 feature classes (code → occurrence count) whose instances
    /// were dropped because translation produced zero spatial associations
    /// (e.g. unresolved <c>VRPT</c>/<c>FSPT</c> pointers or a degenerate ring).
    /// </summary>
    public IReadOnlyDictionary<string, int> FeaturesDroppedForNoGeometry => _noGeometry;

    /// <summary>
    /// S-57 attributes dropped because the mapping has no attribute rule for the
    /// ATTL, keyed by (owning OBJL, ATTL) → occurrence count. Textual-info
    /// attributes (INFORM/NINFOM/TXTDSC/NTXTDS), which are transformed into the
    /// S-101 <c>information</c> complex attribute rather than dropped, are
    /// excluded.
    /// </summary>
    public IReadOnlyDictionary<S57AttributeDrop, int> UnmappedAttributes => _unmappedAttributes;

    /// <summary>
    /// S-57 attributes (ATTL → occurrence count) that had a rule but resolved to
    /// nothing — the rule (or a feature-level override) maps the value to
    /// <c>null</c>, i.e. an intentional value-level drop.
    /// </summary>
    public IReadOnlyDictionary<ushort, int> RuleDroppedAttributes => _ruleDroppedAttributes;

    /// <summary>
    /// S-101 enumerated values dropped because the destination Feature Catalogue
    /// does not list them as allowable, keyed by (S-101 attribute, value) →
    /// occurrence count. A high count here often signals a defective value
    /// remap (targeting a code the FC rejects) rather than a genuine
    /// no-equivalent value.
    /// </summary>
    public IReadOnlyDictionary<S57EnumValueDrop, int> DroppedEnumValues => _droppedEnumValues;

    internal void RecordUnmappedObjectClass(ushort objl) => Increment(_unmappedObjl, objl);

    internal void RecordRuleDroppedObjectClass(ushort objl) => Increment(_ruleDroppedObjl, objl);

    internal void RecordFeatureWithoutGeometry(string s101Code) => Increment(_noGeometry, s101Code);

    internal void RecordUnmappedAttribute(ushort objl, ushort attl)
        => Increment(_unmappedAttributes, new S57AttributeDrop(objl, attl));

    internal void RecordRuleDroppedAttribute(ushort attl) => Increment(_ruleDroppedAttributes, attl);

    internal void RecordDroppedEnumValue(string s101Attribute, string value)
        => Increment(_droppedEnumValues, new S57EnumValueDrop(s101Attribute, value));

    private static void Increment<TKey>(Dictionary<TKey, int> map, TKey key) where TKey : notnull
        => map[key] = map.TryGetValue(key, out var n) ? n + 1 : 1;
}
