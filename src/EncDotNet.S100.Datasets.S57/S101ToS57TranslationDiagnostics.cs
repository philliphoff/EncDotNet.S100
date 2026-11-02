namespace EncDotNet.S100.Datasets.S57;

/// <summary>An S-101 feature class dropped by an S-65 Annex C rule.</summary>
/// <param name="S101Code">The S-101 feature class code.</param>
/// <param name="Clause">The Annex C clause that drops it.</param>
public readonly record struct S101FeatureDrop(string S101Code, string Clause);

/// <summary>An S-101 attribute dropped by an S-65 Annex C rule.</summary>
/// <param name="S101Code">The S-101 attribute code.</param>
/// <param name="Clause">The Annex C clause that drops it.</param>
public readonly record struct S101AttributeDrop(string S101Code, string Clause);

/// <summary>An S-101 attribute with no conversion rule on a feature class.</summary>
/// <param name="S101FeatureCode">The S-101 feature class (or information type) carrying it.</param>
/// <param name="S101AttributeCode">The S-101 attribute code.</param>
public readonly record struct S101UnmappedAttribute(string S101FeatureCode, string S101AttributeCode);

/// <summary>A mandatory S-57 attribute written empty because the S-101 source had no value.</summary>
/// <param name="ObjectClass">The S-57 object class acronym.</param>
/// <param name="Attribute">The S-57 attribute acronym.</param>
public readonly record struct S57AttributeNulled(string ObjectClass, string Attribute);

/// <summary>
/// Optional diagnostics for
/// <see cref="S101ToS57Translator.Translate(EncDotNet.S100.Datasets.S101.S101Document, S101ToS57TranslationDiagnostics?)"/>:
/// aggregate counts of what the translation read, wrote, dropped, folded and
/// truncated, with the S-65 Annex C clause where a rule caused it. It mirrors
/// <see cref="S57TranslationDiagnostics"/> for the forward direction.
/// </summary>
/// <remarks>
/// Create a fresh instance per translation. Passing <c>null</c> to the
/// translator turns collection off.
/// </remarks>
public sealed class S101ToS57TranslationDiagnostics
{
    private readonly Dictionary<S101FeatureDrop, int> _droppedFeatures = new();
    private readonly Dictionary<string, int> _deferredFeatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unmappedFeatures = new(StringComparer.Ordinal);
    private readonly Dictionary<S101AttributeDrop, int> _droppedAttributes = new();
    private readonly Dictionary<S101UnmappedAttribute, int> _unmappedAttributes = new();
    private readonly Dictionary<string, int> _foldedIntoInform = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _truncatedToFirst = new(StringComparer.Ordinal);
    private readonly Dictionary<S57AttributeNulled, int> _mandatoryNulled = new();
    private readonly Dictionary<string, int> _featuresSynthesised = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unmappedInformationTypes = new(StringComparer.Ordinal);

    /// <summary>Number of S-101 feature records read.</summary>
    public int FeaturesRead { get; internal set; }

    /// <summary>Number of S-57 feature records written, including synthesised ones.</summary>
    public int FeaturesWritten { get; internal set; }

    /// <summary>Number of <c>INFORM</c>/<c>NINFOM</c> values longer than the configured maximum.</summary>
    public int InformOverLength { get; internal set; }

    /// <summary>
    /// Number of S-101 Boolean values that were neither <c>true</c>/<c>1</c> nor
    /// <c>false</c>/<c>0</c>, read as false as the S-100 Lua framework reads them.
    /// </summary>
    public int NonBooleanValues { get; internal set; }

    /// <summary>S-101 feature classes dropped by an Annex C rule, with the clause.</summary>
    public IReadOnlyDictionary<S101FeatureDrop, int> DroppedFeatures => _droppedFeatures;

    /// <summary>
    /// S-101 feature classes whose conversion needs a structural inverse that
    /// is deferred to a later phase (for example <c>SpanFixed</c>, folded back
    /// into its <c>BRIDGE</c> in Phase 2), so they are not written yet.
    /// </summary>
    public IReadOnlyDictionary<string, int> DeferredFeatures => _deferredFeatures;

    /// <summary>S-101 feature classes with no conversion rule.</summary>
    public IReadOnlyDictionary<string, int> UnmappedFeatureClasses => _unmappedFeatures;

    /// <summary>S-101 attributes dropped by an Annex C rule, with the clause.</summary>
    public IReadOnlyDictionary<S101AttributeDrop, int> DroppedAttributes => _droppedAttributes;

    /// <summary>S-101 attributes with no conversion rule, by owning class.</summary>
    public IReadOnlyDictionary<S101UnmappedAttribute, int> UnmappedAttributes => _unmappedAttributes;

    /// <summary>
    /// Text strings folded into <c>INFORM</c>/<c>NINFOM</c>, by source (for
    /// example <c>information</c>, <c>NauticalInformation</c> or
    /// <c>multiplicityOfFeatures</c>).
    /// </summary>
    public IReadOnlyDictionary<string, int> FoldedIntoInform => _foldedIntoInform;

    /// <summary>
    /// S-57 attributes of type Enumerated, by acronym, that kept only the first
    /// of several S-101 values (Annex C §2.4.1).
    /// </summary>
    public IReadOnlyDictionary<string, int> TruncatedToFirst => _truncatedToFirst;

    /// <summary>Mandatory S-57 attributes written empty for lack of a source value (Annex C §2.4.3).</summary>
    public IReadOnlyDictionary<S57AttributeNulled, int> MandatoryAttributesNulled => _mandatoryNulled;

    /// <summary>
    /// S-57 objects created from part of an S-101 feature, by acronym: a
    /// <c>TOPMAR</c> from a <c>topmark</c> (Annex C §18.1), or a further
    /// <c>LIGHTS</c> for each extra sector of a <c>LightSectored</c>.
    /// </summary>
    public IReadOnlyDictionary<string, int> FeaturesSynthesised => _featuresSynthesised;

    /// <summary>Information types linked to a feature that Phase 1 does not fold into <c>INFORM</c>.</summary>
    public IReadOnlyDictionary<string, int> UnmappedInformationTypes => _unmappedInformationTypes;

    internal void RecordDroppedFeature(string s101Code, string clause) => Increment(_droppedFeatures, new S101FeatureDrop(s101Code, clause));

    internal void RecordDeferredFeature(string s101Code) => Increment(_deferredFeatures, s101Code);

    internal void RecordUnmappedFeature(string s101Code) => Increment(_unmappedFeatures, s101Code);

    internal void RecordDroppedAttribute(string s101Code, string clause) => Increment(_droppedAttributes, new S101AttributeDrop(s101Code, clause));

    internal void RecordUnmappedAttribute(string owner, string s101Code) => Increment(_unmappedAttributes, new S101UnmappedAttribute(owner, s101Code));

    internal void RecordFoldedIntoInform(string source) => Increment(_foldedIntoInform, source);

    internal void RecordTruncatedToFirst(string s57Acronym) => Increment(_truncatedToFirst, s57Acronym);

    internal void RecordMandatoryNulled(string objectClass, string attribute) => Increment(_mandatoryNulled, new S57AttributeNulled(objectClass, attribute));

    internal void RecordSynthesised(string s57Acronym) => Increment(_featuresSynthesised, s57Acronym);

    internal void RecordUnmappedInformationType(string s101Code) => Increment(_unmappedInformationTypes, s101Code);

    private static void Increment<TKey>(Dictionary<TKey, int> map, TKey key) where TKey : notnull
        => map[key] = map.TryGetValue(key, out var n) ? n + 1 : 1;
}
