namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// S-401-only replacements for the anchorage rules of
/// <see cref="S57S101Mapping.Default"/>: <c>ACHARE</c> / <c>achare</c> (OBJL 4 /
/// 17001) and <c>CATACH</c> / <c>catach</c> (ATTL 8 / 17000). Applied by
/// <see cref="S57S101Mapping.ForSpec"/> after <see cref="InlandRules"/>, so they
/// replace both the standard rules and their inland twins.
/// </summary>
/// <remarks>
/// <para>
/// Source: IEHG "S-57 ENC to S-401 Conversion Guidance" (Edition 1.3.0, draft 2),
/// clauses 3.3 (Anchorage Area), 3.4 (Anchor Berth) and 3.85 (Mooring Area).
/// The guidance's "S-57 Acronyms" section states that its rules, written with
/// upper-case acronyms, also apply to the lower-case inland elements, so each rule here
/// covers both codes. These rules stay out of <see cref="DefaultRules"/> because
/// S-101 has no <c>MooringArea</c> class and its <c>categoryOfAnchorage</c> has
/// no value 16.
/// </para>
/// <para>
/// <b>catach 10 → 16</b> (clauses 3.3 and 3.4): IENC <c>catach</c> 10 means
/// "anchorage for pushing-navigation vessels", which S-401
/// <c>categoryOfAnchorage</c> encodes as 16; its own value 10 means "anchorage for
/// a limited period of time". The remap sits on the attribute rule, so it covers
/// Anchor Berth as well as Anchorage Area. The S-401 target is only selected for
/// inland cells (<c>DSID</c>/<c>PRSP</c> = 10), whose <c>CATACH</c> uses the IENC
/// enumeration.
/// </para>
/// <para>
/// <b>Mooring Area</b> (clause 3.85): the guidance is self-contradictory. Its title is
/// "Mooring Area (achare, catach=1, 2, 3)", and its automatic-conversion text
/// says only that <c>achare</c> with <c>catach</c> 4–13 becomes
/// <c>anchorageArea</c>. Read literally, <c>catach</c> 1–3 would become
/// <c>MooringArea</c>. But S-57/IENC <c>catach</c> 1–3 are unrestricted,
/// deep-water and tanker <em>anchorages</em>, and clause 3.3 converts them to
/// <c>categoryOfAnchorage</c> 1–3 on Anchorage Area. The S-401 DCEG (Edition
/// 1.3.0, clause 16.3.1) even directs recommended anchorages to be encoded with
/// <c>catach</c> = 1. The "1, 2, 3" are instead the codes of S-401
/// <c>categoryOfMooringArea</c> (1 small craft mooring area, 2 mooring area for
/// visitors, 3 mooring area for tankers). The DCEG lists that attribute under
/// the S-57 acronym <c>CATACH</c>. The only S-57 <c>CATACH</c> value that denotes
/// a mooring area is 8 (small craft mooring area). S-57 has no mooring
/// category for visitors or tankers; <c>CATACH</c> 3 is a tanker
/// <em>anchorage</em>. So an anchorage area whose <c>CATACH</c> is exactly 8
/// becomes <c>MooringArea</c> with <c>categoryOfMooringArea</c> 1. Every other
/// value, including 1–3, stays an Anchorage Area.
/// </para>
/// <para>
/// <b>List values</b>: <c>CATACH</c> is list-valued. The redirect matches the
/// whole value, so only a lone 8 redirects. A list that mixes 8 with anchorage
/// categories (e.g. <c>"7,8"</c>) stays an Anchorage Area and keeps every code.
/// Nothing is lost, because S-401 <c>categoryOfAnchorage</c> binds 8 (small craft
/// mooring area) too.
/// </para>
/// </remarks>
internal static class S401AnchorageRules
{
    private const ushort Achare = 4;
    private const ushort InlandAchare = 17001;
    private const ushort Catach = 8;
    private const ushort InlandCatach = 17000;

    // S-57 CATACH 8: small craft mooring area.
    private const string SmallCraftMooringArea = "8";

    // On a MooringArea, CATACH becomes categoryOfMooringArea: 8 maps to its value
    // 1 (small craft mooring area). Every other anchorage code is dropped, since
    // the redirect only fires on a lone 8.
    private static readonly S57FeatureRedirect MooringAreaRedirect = new()
    {
        ConditionAttribute = "CATACH",
        ConditionValues = [SmallCraftMooringArea],
        TargetS101Code = "MooringArea",
        AttributeOverrides = new Dictionary<string, S57AttributeOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["CATACH"] = new S57AttributeOverride
            {
                S101Code = "categoryOfMooringArea",
                ValueRemap = Enumerable.Range(1, 16).ToDictionary(
                    code => code.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    code => code == 8 ? "1" : (string?)null),
            },
        },
    };

    /// <summary>
    /// The <c>ACHARE</c> rule and its inland twin <c>achare</c>, extended with the
    /// Mooring Area redirect.
    /// </summary>
    public static IEnumerable<S57FeatureRule> FeatureRules(S57S101Mapping standard)
    {
        ArgumentNullException.ThrowIfNull(standard);

        var standardRule = standard.FeatureRules[Achare];
        var achare = standardRule with { Redirects = [MooringAreaRedirect, .. standardRule.Redirects] };
        yield return achare;
        yield return achare with { Objl = InlandAchare, S57Acronym = "achare" };
    }

    /// <summary>
    /// The <c>CATACH</c> rule and its inland twin <c>catach</c>, extended with the
    /// value remap for <c>categoryOfAnchorage</c>.
    /// </summary>
    public static IEnumerable<S57AttributeRule> AttributeRules(S57S101Mapping standard)
    {
        ArgumentNullException.ThrowIfNull(standard);

        var standardRule = standard.AttributeRules[Catach];
        var remap = new Dictionary<string, string?>(standardRule.DefaultValueRemap)
        {
            ["10"] = "16", // IENC: anchorage for pushing-navigation vessels
        };

        var catach = standardRule with { DefaultValueRemap = remap };
        yield return catach;
        yield return catach with { Attl = InlandCatach, S57Acronym = "catach" };
    }
}
