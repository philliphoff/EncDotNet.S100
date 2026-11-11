using System.Collections.Frozen;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Chooses the ATTF and NATF lexical levels (DSSI <c>AALL</c>/<c>NALL</c>,
/// S-57 Part 3 §2.4) for translated features, and moves text the ATTF levels
/// can't encode to NATF.
/// </summary>
/// <remarks>
/// ATTF is ASCII (level 0) or ISO 8859-1 (level 1); NATF may also be UCS-2
/// (level 2, Basic Multilingual Plane only). Each field gets the lowest level
/// that encodes all its text. A free-text ATTF value outside ISO 8859-1, such
/// as a Greek <c>OBJNAM</c>, moves to its national counterpart (<c>NOBJNM</c>,
/// <c>NINFOM</c>, <c>NTXTDS</c>); a value with nowhere to go is dropped and
/// counted in <see cref="S101ToS57TranslationDiagnostics.UnencodableText"/>.
/// </remarks>
internal static class S101ToS57LexicalLevels
{
    private const char MaxAscii = '\u007F';
    private const char MaxLatin1 = 'ÿ';

    // Free-text ATTF attributes and their NATF counterparts (S-57 Appendix A).
    private static readonly FrozenDictionary<int, int> NationalCounterparts = new Dictionary<int, int>
    {
        [S101ToS57Rules.S57Attl("INFORM")] = S101ToS57Rules.S57Attl("NINFOM"),
        [S101ToS57Rules.S57Attl("OBJNAM")] = S101ToS57Rules.S57Attl("NOBJNM"),
        [S101ToS57Rules.S57Attl("TXTDSC")] = S101ToS57Rules.S57Attl("NTXTDS"),
    }.ToFrozenDictionary();

    /// <summary>
    /// Returns the features with text moved where the levels need it, and the
    /// ATTF and NATF levels to declare.
    /// </summary>
    /// <param name="features">The translated features.</param>
    /// <param name="separator">Joins a moved value onto an existing <c>NINFOM</c>.</param>
    /// <param name="diagnostics">Collects the moves and drops, when not <c>null</c>.</param>
    public static (List<S57FeatureRecord> Features, int AttfLevel, int NatfLevel) Assign(
        IReadOnlyList<S57FeatureRecord> features,
        string separator,
        S101ToS57TranslationDiagnostics? diagnostics)
    {
        var result = new List<S57FeatureRecord>(features.Count);
        var attfLevel = 0;
        var natfLevel = 0;
        foreach (var feature in features)
        {
            var moved = feature;
            if (feature.Attributes.Any(a => !Fits(a.Value, MaxLatin1)) || feature.NationalAttributes.Any(a => HasSurrogate(a.Value)))
                moved = Move(feature, separator, diagnostics);

            foreach (var attribute in moved.Attributes)
                attfLevel = Math.Max(attfLevel, Fits(attribute.Value, MaxAscii) ? 0 : 1);
            foreach (var attribute in moved.NationalAttributes)
                natfLevel = Math.Max(natfLevel, Fits(attribute.Value, MaxAscii) ? 0 : Fits(attribute.Value, MaxLatin1) ? 1 : 2);
            result.Add(moved);
        }
        return (result, attfLevel, natfLevel);
    }

    private static S57FeatureRecord Move(S57FeatureRecord feature, string separator, S101ToS57TranslationDiagnostics? diagnostics)
    {
        var attributes = new List<S57AttributeValue>(feature.Attributes.Count);
        var national = new List<S57AttributeValue>();
        foreach (var attribute in feature.NationalAttributes)
        {
            if (HasSurrogate(attribute.Value))
                diagnostics?.RecordUnencodableText(Acronym(attribute.AttributeCode));
            else
                national.Add(attribute);
        }

        foreach (var attribute in feature.Attributes)
        {
            if (Fits(attribute.Value, MaxLatin1))
            {
                attributes.Add(attribute);
                continue;
            }

            var acronym = Acronym(attribute.AttributeCode);
            if (HasSurrogate(attribute.Value) || !NationalCounterparts.TryGetValue(attribute.AttributeCode, out var counterpart))
            {
                diagnostics?.RecordUnencodableText(acronym);
                continue;
            }

            var existing = national.FindIndex(a => a.AttributeCode == counterpart);
            if (existing < 0)
            {
                national.Add(new S57AttributeValue { AttributeCode = counterpart, Value = attribute.Value });
            }
            else if (acronym == "INFORM")
            {
                national[existing] = national[existing] with { Value = national[existing].Value + separator + attribute.Value };
            }
            else
            {
                // A name or file reference can't be joined onto another.
                diagnostics?.RecordUnencodableText(acronym);
                continue;
            }
            diagnostics?.RecordMovedToNational(acronym);
        }

        national.Sort((a, b) => a.AttributeCode.CompareTo(b.AttributeCode));
        return feature with { Attributes = attributes, NationalAttributes = national };
    }

    private static string Acronym(int attl) => S101ToS57Rules.S57AttributeAcronym(attl) ?? $"#{attl}";

    private static bool Fits(string value, char max)
    {
        foreach (var c in value)
        {
            if (c > max) return false;
        }
        return true;
    }

    private static bool HasSurrogate(string value)
    {
        foreach (var c in value)
        {
            if (char.IsSurrogate(c)) return true;
        }
        return false;
    }
}
