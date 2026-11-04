using System.Globalization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// The feature an S-57 object becomes when its object class has no rule in the
/// <see cref="S57S101Mapping"/>: the translator keeps it, with its geometry,
/// as an <see cref="FeatureTypeCode"/> feature instead of dropping it.
/// </summary>
/// <remarks>
/// <para>
/// S-52 draws an object of unknown class as a "?" (SY(QUESMRK1), or LC(QUESMRK1)
/// round a line or area), and S-64 3.2.1 checks it does. The S-101 and S-401
/// Feature Catalogues don't define <see cref="FeatureTypeCode"/>, so the
/// portrayal has no rule for it and falls back to its Default rule, which
/// draws exactly that, by primitive. The "?" is also how a translation gap
/// shows on the chart: a class the mapping doesn't cover yet.
/// </para>
/// <para>
/// The feature carries the S-57 object class in <see cref="ObjectClassAttribute"/>
/// and each S-57 attribute under its S-57 acronym, or
/// <see cref="AttributeCode(ushort)"/> when the mapping doesn't know the attribute,
/// with its value as encoded. A class a rule drops on purpose (a
/// <see cref="S57FeatureRule"/> with no S-101 class) is still dropped.
/// </para>
/// </remarks>
public static class S57UnknownObject
{
    /// <summary>The feature type code of an object whose class has no rule.</summary>
    public const string FeatureTypeCode = "S57UnknownObject";

    /// <summary>The attribute that holds the S-57 object class code (OBJL), e.g. <c>"163"</c>.</summary>
    public const string ObjectClassAttribute = "OBJL";

    /// <summary>
    /// The attribute code for an S-57 attribute the mapping has no acronym
    /// for, e.g. <c>"ATTL17001"</c>.
    /// </summary>
    /// <param name="attl">The S-57 attribute code.</param>
    /// <returns>The code.</returns>
    public static string AttributeCode(ushort attl) => "ATTL" + attl.ToString(CultureInfo.InvariantCulture);

    // ATTL → acronym for the attributes the reverse translator writes itself,
    // which the forward mapping has no flat rule for (OBJNAM, INFORM, …).
    private static readonly Lazy<Dictionary<ushort, string>> AssembledAttributeAcronyms = new(() =>
        S101ToS57Rules.S57AttributeCodes.ToDictionary(kv => kv.Value, kv => kv.Key));

    /// <summary>
    /// The code an S-57 attribute goes by on an <see cref="FeatureTypeCode"/>
    /// feature: its acronym where <paramref name="mapping"/> or the S-57
    /// attributes the translators know name it, else <see cref="AttributeCode(ushort)"/>.
    /// </summary>
    internal static string AttributeCode(ushort attl, S57S101Mapping mapping) =>
        mapping.AttributeRules.TryGetValue(attl, out var rule) ? rule.S57Acronym
        : AssembledAttributeAcronyms.Value.TryGetValue(attl, out var acronym) ? acronym
        : AttributeCode(attl);

    /// <summary>
    /// The S-57 acronym of an object class, e.g. <c>"TS_PAD"</c> for 138 or
    /// <c>"$AREAS"</c> for 500, or <see langword="null"/> when the S-57 Object
    /// Catalogue doesn't define it.
    /// </summary>
    /// <param name="objl">The S-57 object class code.</param>
    /// <returns>The acronym, or <see langword="null"/>.</returns>
    public static string? Acronym(int objl) =>
        S57ObjectCatalogue.Iho.GetObjectClass(objl)?.Acronym;

    /// <summary>
    /// The name an <see cref="FeatureTypeCode"/> feature goes by in a pick
    /// report: <c>"Unknown S-57 object TS_PAD (138)"</c>, or
    /// <c>"Unknown S-57 object (999)"</c> for a class the S-57 Object
    /// Catalogue doesn't define.
    /// </summary>
    /// <param name="objectClass">The <see cref="ObjectClassAttribute"/> value, or <see langword="null"/>.</param>
    /// <returns>The name.</returns>
    public static string Name(string? objectClass)
    {
        if (!int.TryParse(objectClass, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objl))
            return "Unknown S-57 object";

        return Acronym(objl) is { } acronym
            ? $"Unknown S-57 object {acronym} ({objl.ToString(CultureInfo.InvariantCulture)})"
            : $"Unknown S-57 object ({objl.ToString(CultureInfo.InvariantCulture)})";
    }
}
