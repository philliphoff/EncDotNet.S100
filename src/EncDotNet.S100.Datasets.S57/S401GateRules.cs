namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// S-401-only replacement for the gate rules of
/// <see cref="S57S101Mapping.Default"/>: <c>GATCON</c> / <c>gatcon</c> (OBJL 61 /
/// 17031). Applied by <see cref="S57S101Mapping.ForSpec"/> after
/// <see cref="InlandRules"/>, so it replaces both the standard rule and its
/// inland twin.
/// </summary>
/// <remarks>
/// Source: IEHG "S-57 ENC to S-401 Conversion Guidance" (Edition 1.3.0, draft 2),
/// clause 3.55 (Gate). It converts <c>VERCLR</c> into the
/// <c>verticalClearanceValue</c> of <c>verticalClearanceOpen</c>, the only
/// vertical clearance a Gate binds, instead of the <c>verticalClearanceFixed</c>
/// that <c>VERCLR</c> targets elsewhere. S-65 Annex B (the S-101 guidance) has no
/// such rule, so S-101 keeps the default and a gate's <c>VERCLR</c> is dropped
/// there.
/// </remarks>
internal static class S401GateRules
{
    private const ushort Gatcon = 61;
    private const ushort InlandGatcon = 17031;

    /// <summary>
    /// The <c>GATCON</c> rule and its inland twin <c>gatcon</c>, with
    /// <c>VERCLR</c> sent to <c>verticalClearanceOpen</c>.
    /// </summary>
    public static IEnumerable<S57FeatureRule> FeatureRules(S57S101Mapping standard)
    {
        ArgumentNullException.ThrowIfNull(standard);

        var standardRule = standard.FeatureRules[Gatcon];
        var overrides = new Dictionary<string, S57AttributeOverride>(
            standardRule.AttributeOverrides, StringComparer.OrdinalIgnoreCase)
        {
            ["VERCLR"] = new S57AttributeOverride { S101Code = "verticalClearanceOpen" },
        };

        var gatcon = standardRule with { AttributeOverrides = overrides };
        yield return gatcon;
        yield return gatcon with { Objl = InlandGatcon, S57Acronym = "gatcon" };
    }
}
