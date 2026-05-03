using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Rule describing how an S-57 feature class (object class, OBJL) maps to one
/// or more S-101 feature classes.
/// </summary>
/// <remarks>
/// <para>
/// In the simplest case a rule has only a <see cref="DefaultS101Code"/>, which
/// is the S-101 Feature Catalogue code that every instance of the OBJL maps
/// to. Set <see cref="DefaultS101Code"/> to <c>null</c> to drop the feature
/// entirely (e.g. S-57 <c>M_HOPA</c> has no S-101 equivalent).
/// </para>
/// <para>
/// Cross-class redirects are expressed via <see cref="Redirects"/>. Redirects
/// are evaluated in declaration order and the first matching redirect wins;
/// if none match, <see cref="DefaultS101Code"/> applies. This models the
/// pattern used by the IHO S-57 to S-101 Conversion Guidance, e.g.
/// <c>CTRPNT</c> with <c>CATCTR ∈ {1, 5}</c> redirects to <c>Landmark</c>,
/// while other CTRPNT instances are dropped.
/// </para>
/// <para>
/// <see cref="AttributeOverrides"/> are applied to every instance regardless
/// of which redirect (if any) fires. A redirect can supply additional
/// overrides via <see cref="S57FeatureRedirect.AttributeOverrides"/>; redirect
/// overrides take precedence over the rule-level overrides.
/// </para>
/// </remarks>
public sealed record S57FeatureRule
{
    /// <summary>S-57 numeric object class code (OBJL).</summary>
    public required ushort Objl { get; init; }

    /// <summary>S-57 object class acronym (e.g. <c>COALNE</c>).</summary>
    public required string S57Acronym { get; init; }

    /// <summary>
    /// Default S-101 Feature Catalogue code. When <c>null</c>, the feature is
    /// dropped (unless a redirect matches).
    /// </summary>
    public string? DefaultS101Code { get; init; }

    /// <summary>
    /// Conditional cross-class redirects evaluated in order; the first match
    /// wins.
    /// </summary>
    public ImmutableArray<S57FeatureRedirect> Redirects { get; init; } = ImmutableArray<S57FeatureRedirect>.Empty;

    /// <summary>
    /// Attribute overrides applied whenever this rule is selected (default or
    /// redirect path). Keyed by S-57 attribute acronym.
    /// </summary>
    public ImmutableDictionary<string, S57AttributeOverride> AttributeOverrides { get; init; }
        = ImmutableDictionary<string, S57AttributeOverride>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Conditional redirect on an <see cref="S57FeatureRule"/>: when the named
/// S-57 attribute's value matches one of <see cref="ConditionValues"/>, the
/// feature is mapped to <see cref="TargetS101Code"/> instead of the rule's
/// default S-101 code.
/// </summary>
public sealed record S57FeatureRedirect
{
    /// <summary>S-57 attribute acronym used to test the redirect condition.</summary>
    public required string ConditionAttribute { get; init; }

    /// <summary>S-57 attribute values that satisfy the condition.</summary>
    public required ImmutableArray<string> ConditionValues { get; init; }

    /// <summary>Target S-101 Feature Catalogue code when the condition matches.</summary>
    public required string TargetS101Code { get; init; }

    /// <summary>
    /// Additional attribute overrides applied only when this redirect fires.
    /// Layered on top of the rule's <see cref="S57FeatureRule.AttributeOverrides"/>.
    /// Keyed by S-57 attribute acronym.
    /// </summary>
    public ImmutableDictionary<string, S57AttributeOverride> AttributeOverrides { get; init; }
        = ImmutableDictionary<string, S57AttributeOverride>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-feature attribute override: changes the target S-101 attribute name
/// and/or remaps individual enum values.
/// </summary>
public sealed record S57AttributeOverride
{
    /// <summary>
    /// Override S-101 attribute name. <c>null</c> means "keep the default
    /// attribute mapping". Set to a non-null string to redirect the attribute
    /// (e.g. <c>CATCTR</c> on a <c>Landmark</c> redirect maps to
    /// <c>category of landmark</c>).
    /// </summary>
    public string? S101Code { get; init; }

    /// <summary>
    /// Per-value remap. A key with a non-null value rewrites the S-57 value
    /// to the given S-101 value. A key with a <c>null</c> value drops the
    /// attribute entirely. Missing keys leave the value unchanged.
    /// </summary>
    public ImmutableDictionary<string, string?> ValueRemap { get; init; }
        = ImmutableDictionary<string, string?>.Empty;
}

/// <summary>
/// Rule describing how an S-57 attribute (ATTL) maps to an S-101 Feature
/// Catalogue attribute.
/// </summary>
public sealed record S57AttributeRule
{
    /// <summary>S-57 numeric attribute code (ATTL).</summary>
    public required ushort Attl { get; init; }

    /// <summary>S-57 attribute acronym (e.g. <c>DRVAL1</c>).</summary>
    public required string S57Acronym { get; init; }

    /// <summary>
    /// Default S-101 attribute name. <c>null</c> means the attribute has no
    /// flat S-101 equivalent and should be dropped.
    /// </summary>
    public string? DefaultS101Code { get; init; }

    /// <summary>
    /// Default per-value remap. Applied unless overridden by a feature-level
    /// override. A key with a non-null value rewrites the value; a key with
    /// a <c>null</c> value drops the attribute. Missing keys pass through.
    /// </summary>
    public ImmutableDictionary<string, string?> DefaultValueRemap { get; init; }
        = ImmutableDictionary<string, string?>.Empty;
}

/// <summary>
/// Result of resolving an S-57 feature against the mapping rules: the chosen
/// S-101 Feature Catalogue code and the effective attribute overrides to
/// apply when translating attributes for that feature.
/// </summary>
/// <param name="S101Code">Resolved S-101 Feature Catalogue code.</param>
/// <param name="AttributeOverrides">
/// Effective attribute overrides for this feature (rule-level overrides
/// merged with any redirect-supplied overrides). Keyed by S-57 attribute
/// acronym.
/// </param>
public sealed record ResolvedFeature(
    string S101Code,
    ImmutableDictionary<string, S57AttributeOverride> AttributeOverrides);

/// <summary>
/// Result of resolving an S-57 attribute in the context of a
/// <see cref="ResolvedFeature"/>: the chosen S-101 attribute name and the
/// (possibly remapped) value.
/// </summary>
/// <param name="S101Code">Resolved S-101 attribute name.</param>
/// <param name="Value">Value to emit, after any per-value remap.</param>
public sealed record ResolvedAttribute(string S101Code, string Value);
