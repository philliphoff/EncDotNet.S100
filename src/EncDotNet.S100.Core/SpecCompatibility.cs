namespace EncDotNet.S100.Core;

/// <summary>
/// Match policies governing whether a <see cref="CatalogueRef"/> is considered
/// acceptable for a given <see cref="SpecRef"/>. The rules follow S-100
/// Edition 5.2.1 Part 2 §6 (Maintenance) — see <see cref="SpecVersion"/> for
/// the underlying compatibility semantics.
/// </summary>
public enum SpecMatchPolicy
{
    /// <summary>
    /// Catalogue must match the spec on both name and edition exactly.
    /// </summary>
    Exact,

    /// <summary>
    /// Catalogue must share the spec's name and major version. Its minor
    /// version must be greater than or equal to the spec's minor version
    /// (so that the catalogue carries every feature the spec declares).
    /// Clarification-level differences are ignored.
    /// </summary>
    SameMajor,

    /// <summary>
    /// Catalogue must share the spec's name; edition is ignored. Use only
    /// when the caller has independently verified that semantic differences
    /// are acceptable.
    /// </summary>
    AnyVersion,
}

/// <summary>
/// Helpers for matching a requested <see cref="SpecRef"/> against an available
/// <see cref="CatalogueRef"/>. The match policy is supplied by the caller —
/// providers themselves do not apply hidden fallback rules.
/// </summary>
public static class SpecCompatibility
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="catalogue"/> satisfies
    /// <paramref name="spec"/> under the given <paramref name="policy"/>.
    /// </summary>
    /// <param name="spec">
    /// The spec being declared by the dataset (or otherwise requested).
    /// </param>
    /// <param name="catalogue">
    /// The catalogue we have available.
    /// </param>
    /// <param name="policy">
    /// The match policy — see <see cref="SpecMatchPolicy"/>.
    /// </param>
    public static bool IsMatch(SpecRef spec, CatalogueRef catalogue, SpecMatchPolicy policy)
    {
        if (spec.Name is null || catalogue.Name is null) return false;
        if (!string.Equals(spec.Name, catalogue.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return policy switch
        {
            SpecMatchPolicy.Exact => spec.Edition == catalogue.Version,
            SpecMatchPolicy.SameMajor =>
                spec.Edition.Major == catalogue.Version.Major
                && catalogue.Version.Minor >= spec.Edition.Minor,
            SpecMatchPolicy.AnyVersion => true,
            _ => false,
        };
    }

    /// <summary>
    /// Classifies the relationship between the version a dataset declares
    /// (<paramref name="declared"/>) and the version a catalogue we resolved
    /// for it carries (<paramref name="catalogue"/>). Useful for surfacing a
    /// structured warning when the two diverge.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="SpecMatchKind.Exact"/> when the two versions are
    /// equal; <see cref="SpecMatchKind.CatalogueNewerCompatible"/> when the
    /// catalogue is on the same major and at least the same minor (a S-100
    /// Part 2 §6 backward-compatible read); <see cref="SpecMatchKind.CatalogueOlder"/>
    /// when the catalogue is on the same major but a lower minor (the
    /// dataset may use features the catalogue doesn't know about); and
    /// <see cref="SpecMatchKind.MajorDivergence"/> when the two are on
    /// different majors (incompatible — decoding may misinterpret data).
    /// A <see cref="default(SpecVersion)"/> on either side falls through to
    /// <see cref="SpecMatchKind.Unknown"/>.
    /// </remarks>
    public static SpecMatchKind Classify(SpecVersion declared, SpecVersion catalogue)
    {
        if (declared == default || catalogue == default) return SpecMatchKind.Unknown;
        if (declared == catalogue) return SpecMatchKind.Exact;
        if (declared.Major != catalogue.Major) return SpecMatchKind.MajorDivergence;
        return catalogue.Minor >= declared.Minor
            ? SpecMatchKind.CatalogueNewerCompatible
            : SpecMatchKind.CatalogueOlder;
    }
}

/// <summary>
/// The relationship between a declared dataset version and the version of
/// the catalogue resolved for it.
/// </summary>
public enum SpecMatchKind
{
    /// <summary>One or both versions were unspecified (<see cref="default(SpecVersion)"/>).</summary>
    Unknown,

    /// <summary>Versions are equal in all components but Clarification.</summary>
    Exact,

    /// <summary>Same major; catalogue's minor ≥ declared minor (backward-compatible read).</summary>
    CatalogueNewerCompatible,

    /// <summary>Same major; catalogue's minor &lt; declared minor (catalogue may be missing features).</summary>
    CatalogueOlder,

    /// <summary>Different majors — semantically incompatible per S-100 Part 2 §6.</summary>
    MajorDivergence,
}
