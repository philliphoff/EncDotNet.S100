using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Mapping from the S-57 (Edition 3.1) Object Catalogue to the S-101 Feature
/// Catalogue. Encodes feature-class and attribute mappings, plus richer rule
/// types used by the S-57 to S-101 conversion: cross-class redirects,
/// per-feature attribute overrides, and per-value enum remaps.
/// </summary>
/// <remarks>
/// <para>
/// Rule data is sourced primarily from the IHO draft "S-57 to S-101
/// Conversion Guidance" (S-100WG / S-101 Project Team 6, January 2021),
/// cross-checked against the IHO S-57 Object Catalogue (Appendix A, Edition
/// 3.1) for OBJL/ATTL numeric codes.
/// </para>
/// <para>
/// The <see cref="Default"/> mapping is built from compiled-in seed data; no
/// resource files are loaded at runtime. Use the <see cref="Builder"/> to
/// compose custom mappings.
/// </para>
/// </remarks>
public sealed class S57S101Mapping
{
    private readonly ImmutableDictionary<string, ushort> _attlByAcronym;

    /// <summary>Feature-class rules keyed by S-57 OBJL.</summary>
    public ImmutableDictionary<ushort, S57FeatureRule> FeatureRules { get; }

    /// <summary>Attribute rules keyed by S-57 ATTL.</summary>
    public ImmutableDictionary<ushort, S57AttributeRule> AttributeRules { get; }

    private S57S101Mapping(
        ImmutableDictionary<ushort, S57FeatureRule> featureRules,
        ImmutableDictionary<ushort, S57AttributeRule> attributeRules)
    {
        FeatureRules = featureRules;
        AttributeRules = attributeRules;

        var byAcronym = ImmutableDictionary.CreateBuilder<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var (attl, rule) in attributeRules)
            byAcronym[rule.S57Acronym] = attl;
        _attlByAcronym = byAcronym.ToImmutable();
    }

    /// <summary>
    /// The default mapping bundled with this assembly. Suitable for most ENC
    /// base cells; extend by composing your own <see cref="Builder"/> when a
    /// producer uses additional object classes.
    /// </summary>
    public static S57S101Mapping Default { get; } = BuildDefault();

    // ── Legacy back-compat lookups ──────────────────────────────────────

    /// <summary>
    /// Resolves a numeric S-57 OBJL code to its default S-101 Feature
    /// Catalogue code. Returns <c>null</c> when the code is not in the
    /// mapping table or has no default code (because every concrete
    /// destination depends on a redirect condition).
    /// </summary>
    public string? ResolveFeatureCode(ushort objl)
        => FeatureRules.TryGetValue(objl, out var r) ? r.DefaultS101Code : null;

    /// <summary>
    /// Resolves a numeric S-57 ATTL code to its default S-101 Feature
    /// Catalogue attribute name. Returns <c>null</c> when the code is not in
    /// the mapping table or has no flat S-101 equivalent.
    /// </summary>
    public string? ResolveAttributeCode(ushort attl)
        => AttributeRules.TryGetValue(attl, out var r) ? r.DefaultS101Code : null;

    /// <summary>Number of feature classes in the mapping.</summary>
    public int FeatureCount => FeatureRules.Count;

    /// <summary>Number of attributes in the mapping.</summary>
    public int AttributeCount => AttributeRules.Count;

    // ── Rule-aware resolution ───────────────────────────────────────────

    /// <summary>
    /// Resolves an S-57 feature instance to the S-101 feature class it should
    /// map to, evaluating any conditional redirects defined for the OBJL
    /// against the supplied S-57 attribute values.
    /// </summary>
    /// <param name="objl">S-57 numeric object class (OBJL).</param>
    /// <param name="s57AttributesByAcronym">
    /// The feature's S-57 attributes keyed by acronym. Use
    /// <see cref="BuildAcronymView"/> to construct this view from a feature's
    /// raw attribute records.
    /// </param>
    /// <returns>
    /// A <see cref="ResolvedFeature"/>, or <c>null</c> if the OBJL has no
    /// rule or all matching rules drop the feature.
    /// </returns>
    public ResolvedFeature? ResolveFeature(ushort objl, IReadOnlyDictionary<string, string> s57AttributesByAcronym)
    {
        if (!FeatureRules.TryGetValue(objl, out var rule)) return null;

        foreach (var redirect in rule.Redirects)
        {
            if (s57AttributesByAcronym.TryGetValue(redirect.ConditionAttribute, out var v)
                && redirect.ConditionValues.Contains(v, StringComparer.Ordinal))
            {
                var combined = MergeOverrides(rule.AttributeOverrides, redirect.AttributeOverrides);
                return new ResolvedFeature(redirect.TargetS101Code, combined);
            }
        }

        if (rule.DefaultS101Code is null) return null;
        return new ResolvedFeature(rule.DefaultS101Code, rule.AttributeOverrides);
    }

    /// <summary>
    /// Resolves an S-57 attribute, in the context of a previously resolved
    /// feature, to the S-101 attribute name and value to emit.
    /// </summary>
    /// <param name="s57Acronym">S-57 attribute acronym (e.g. <c>CATCOA</c>).</param>
    /// <param name="value">Raw S-57 attribute value as a string.</param>
    /// <param name="feature">
    /// The resolved feature whose effective overrides apply to this
    /// attribute.
    /// </param>
    /// <returns>
    /// A <see cref="ResolvedAttribute"/>, or <c>null</c> if the attribute is
    /// not mapped or a value remap drops it.
    /// </returns>
    public ResolvedAttribute? ResolveAttribute(string s57Acronym, string value, ResolvedFeature feature)
    {
        ArgumentNullException.ThrowIfNull(s57Acronym);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(feature);

        if (!_attlByAcronym.TryGetValue(s57Acronym, out var attl)) return null;
        if (!AttributeRules.TryGetValue(attl, out var rule)) return null;

        string? s101Code = rule.DefaultS101Code;
        var valueRemap = rule.DefaultValueRemap;

        if (feature.AttributeOverrides.TryGetValue(s57Acronym, out var ov))
        {
            if (ov.S101Code is not null) s101Code = ov.S101Code;
            // Per-value code override wins over both rule default and the
            // override's S101Code.
            if (ov.S101CodeByValue.TryGetValue(value, out var perValueCode))
                s101Code = perValueCode;
            if (ov.ValueRemap.Count > 0)
                valueRemap = MergeValueRemap(valueRemap, ov.ValueRemap);
        }

        if (s101Code is null) return null;

        if (valueRemap.TryGetValue(value, out var newValue))
        {
            if (newValue is null) return null; // explicit drop
            value = newValue;
        }

        return new ResolvedAttribute(s101Code, value);
    }

    /// <summary>
    /// Builds a {acronym → value} view of an S-57 feature's raw attributes,
    /// suitable for passing to <see cref="ResolveFeature"/>. Values for
    /// attributes whose ATTL is unknown to this mapping are omitted.
    /// </summary>
    public ImmutableDictionary<string, string> BuildAcronymView(IEnumerable<S57Attribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var b = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes)
        {
            if (AttributeRules.TryGetValue(a.Code, out var rule))
                b[rule.S57Acronym] = a.Value;
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// Try to look up the numeric ATTL for a given S-57 attribute acronym.
    /// </summary>
    public bool TryGetAttl(string s57Acronym, out ushort attl)
        => _attlByAcronym.TryGetValue(s57Acronym, out attl);

    private static ImmutableDictionary<string, S57AttributeOverride> MergeOverrides(
        ImmutableDictionary<string, S57AttributeOverride> ruleLevel,
        ImmutableDictionary<string, S57AttributeOverride> redirectLevel)
    {
        if (redirectLevel.IsEmpty) return ruleLevel;
        if (ruleLevel.IsEmpty) return redirectLevel;

        var b = ruleLevel.ToBuilder();
        foreach (var kv in redirectLevel)
            b[kv.Key] = kv.Value; // redirect-level wins
        return b.ToImmutable();
    }

    private static ImmutableDictionary<string, string?> MergeValueRemap(
        ImmutableDictionary<string, string?> defaultRemap,
        ImmutableDictionary<string, string?> overrideRemap)
    {
        if (overrideRemap.IsEmpty) return defaultRemap;
        if (defaultRemap.IsEmpty) return overrideRemap;

        var b = defaultRemap.ToBuilder();
        foreach (var kv in overrideRemap)
            b[kv.Key] = kv.Value;
        return b.ToImmutable();
    }

    // ── Builder ─────────────────────────────────────────────────────────

    /// <summary>Builder for composing custom mapping tables.</summary>
    public sealed class Builder
    {
        private readonly ImmutableDictionary<ushort, S57FeatureRule>.Builder _features
            = ImmutableDictionary.CreateBuilder<ushort, S57FeatureRule>();
        private readonly ImmutableDictionary<ushort, S57AttributeRule>.Builder _attributes
            = ImmutableDictionary.CreateBuilder<ushort, S57AttributeRule>();

        /// <summary>Adds or replaces a feature-class rule.</summary>
        public Builder AddFeatureRule(S57FeatureRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);
            _features[rule.Objl] = rule;
            return this;
        }

        /// <summary>
        /// Convenience overload that adds a 1:1 feature-class mapping.
        /// </summary>
        public Builder AddFeature(ushort objl, string s57Acronym, string s101Code)
        {
            ArgumentException.ThrowIfNullOrEmpty(s57Acronym);
            ArgumentException.ThrowIfNullOrEmpty(s101Code);
            _features[objl] = new S57FeatureRule
            {
                Objl = objl,
                S57Acronym = s57Acronym,
                DefaultS101Code = s101Code,
            };
            return this;
        }

        /// <summary>
        /// Convenience overload that adds a 1:1 feature-class mapping by S-101
        /// code only. Use when the S-57 acronym is not relevant for the test
        /// being constructed.
        /// </summary>
        public Builder AddFeature(ushort objl, string s101Code)
            => AddFeature(objl, $"OBJL{objl}", s101Code);

        /// <summary>Adds or replaces an attribute rule.</summary>
        public Builder AddAttributeRule(S57AttributeRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);
            _attributes[rule.Attl] = rule;
            return this;
        }

        /// <summary>
        /// Convenience overload that adds a 1:1 attribute mapping.
        /// </summary>
        public Builder AddAttribute(ushort attl, string s57Acronym, string s101Code)
        {
            ArgumentException.ThrowIfNullOrEmpty(s57Acronym);
            ArgumentException.ThrowIfNullOrEmpty(s101Code);
            _attributes[attl] = new S57AttributeRule
            {
                Attl = attl,
                S57Acronym = s57Acronym,
                DefaultS101Code = s101Code,
            };
            return this;
        }

        /// <summary>
        /// Convenience overload that adds a 1:1 attribute mapping by S-101
        /// code only. Use when the S-57 acronym is not relevant for the test
        /// being constructed.
        /// </summary>
        public Builder AddAttribute(ushort attl, string s101Code)
            => AddAttribute(attl, $"ATTL{attl}", s101Code);

        /// <summary>Pre-populates the builder with the default mapping.</summary>
        public Builder WithDefaults()
        {
            foreach (var kv in Default.FeatureRules) _features[kv.Key] = kv.Value;
            foreach (var kv in Default.AttributeRules) _attributes[kv.Key] = kv.Value;
            return this;
        }

        /// <summary>Builds an immutable mapping.</summary>
        public S57S101Mapping Build()
            => new(_features.ToImmutable(), _attributes.ToImmutable());
    }

    // ── Default rule data (compiled-in) ─────────────────────────────────

    private static S57S101Mapping BuildDefault()
    {
        var features = ImmutableDictionary.CreateBuilder<ushort, S57FeatureRule>();
        foreach (var rule in DefaultRules.FeatureRules())
            features[rule.Objl] = rule;

        var attributes = ImmutableDictionary.CreateBuilder<ushort, S57AttributeRule>();
        foreach (var rule in DefaultRules.AttributeRules())
            attributes[rule.Attl] = rule;

        return new S57S101Mapping(features.ToImmutable(), attributes.ToImmutable());
    }
}
