using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// Reads rule probe results (R13a, #1720) for the entries that show them
/// (symbol dictionary design note §4.4, R13c, #1722): which input is which,
/// which probed value stands for a feature's own, and the part an outcome is
/// shown by.
/// </summary>
internal static class ProbeOutcomes
{
    /// <summary>The Simplified symbols display setting's id.</summary>
    public const string SimplifiedSymbols = "SimplifiedSymbols";

    private const string Symbol = "symbol:";
    private const string Colour = "colour:";
    private const string Fill = "fill:";
    private const string Line = "line:";

    /// <summary>
    /// The part an outcome is shown by, as <see cref="ProbeOutcome.Parts"/>
    /// writes it: its first point symbol other than the catalogue's default
    /// <c>?</c>, else its first colour fill, area fill or line style, else
    /// the default symbol; <see langword="null"/> when it drew none of these
    /// (text or plain lines alone).
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    public static string? MainPart(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        string? fallback = null;
        foreach (var part in outcome.Parts)
        {
            if (!part.StartsWith(Symbol, StringComparison.Ordinal))
                continue;
            if (!LeadGlyph.DefaultSymbols.Contains(part[Symbol.Length..]))
                return part;
            fallback ??= part;
        }

        foreach (var prefix in (string[])[Colour, Fill, Line])
        {
            if (outcome.Parts.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.Ordinal)) is { } part)
                return part;
        }

        return fallback;
    }

    /// <summary>Whether <paramref name="part"/> is the catalogue's default symbol, drawn when nothing else fits.</summary>
    /// <param name="part">A part, as <see cref="ProbeOutcome.Parts"/> writes it.</param>
    public static bool IsDefault(string? part) =>
        part is not null && part.StartsWith(Symbol, StringComparison.Ordinal) && LeadGlyph.DefaultSymbols.Contains(part[Symbol.Length..]);

    /// <summary>
    /// The glyph of a part: a point symbol, area fill or line style is its
    /// item; a colour fill is a patch keyed by its token.
    /// </summary>
    /// <param name="part">The part, as <see cref="ProbeOutcome.Parts"/> writes it.</param>
    /// <param name="spec">The product.</param>
    /// <returns>The glyph, or <see langword="null"/> for a part with none.</returns>
    public static LeadGlyph? GlyphOf(string? part, SpecRef spec)
    {
        if (part is null)
            return null;
        if (part.StartsWith(Symbol, StringComparison.Ordinal))
            return Item(LeadGlyphKind.Symbol, ReferenceKind.Symbol, part[Symbol.Length..]);
        if (part.StartsWith(Fill, StringComparison.Ordinal))
            return Item(LeadGlyphKind.AreaFill, ReferenceKind.AreaFill, part[Fill.Length..]);
        if (part.StartsWith(Line, StringComparison.Ordinal))
            return Item(LeadGlyphKind.LineStyle, ReferenceKind.LineStyle, part[Line.Length..]);
        if (part.StartsWith(Colour, StringComparison.Ordinal) && part.Length > Colour.Length)
        {
            // No entry has this key, so the patch's glyph is cached by its token.
            var token = part[Colour.Length..];
            return new LeadGlyph(LeadGlyphKind.Area, new ReferenceKey(spec, ReferenceKind.AreaFill, part), Tokens: [token]);
        }

        return null;

        LeadGlyph? Item(LeadGlyphKind kind, ReferenceKind entry, string id) =>
            id.Length == 0 ? null : new LeadGlyph(kind, new ReferenceKey(spec, entry, id), id);
    }

    /// <summary>The position of an input in <see cref="FeatureTypeProbe.Inputs"/>, or -1.</summary>
    /// <param name="probe">The probe.</param>
    /// <param name="kind">The input's kind.</param>
    /// <param name="code">The attribute code or context parameter id.</param>
    public static int InputOf(FeatureTypeProbe probe, ProbeInputKind kind, string code)
    {
        ArgumentNullException.ThrowIfNull(probe);
        for (var i = 0; i < probe.Inputs.Count; i++)
        {
            if (probe.Inputs[i].Kind == kind && string.Equals(probe.Inputs[i].Code, code, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The probed value that stands for <paramref name="value"/>: the same
    /// value (a list compared item by item), else for a number the nearest
    /// probed number; <see langword="null"/> stands for no value.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="value">The value, or <see langword="null"/> for none.</param>
    /// <param name="nearest">Whether a number takes the nearest probed number; an enumeration's code takes only its own.</param>
    /// <returns>Its index in <see cref="ProbeInput.Values"/>, or -1 when none stands for it.</returns>
    public static int ValueOf(ProbeInput input, string? value, bool nearest = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        var wanted = Normalise(value);
        for (var i = 0; i < input.Values.Count; i++)
        {
            if (string.Equals(Normalise(input.Values[i]), wanted, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        if (!nearest || wanted is null || !TryNumber(wanted, out var number))
            return -1;
        var best = -1;
        var distance = double.MaxValue;
        for (var i = 0; i < input.Values.Count; i++)
        {
            if (input.Values[i] is { } probed && TryNumber(probed, out var p) && Math.Abs(p - number) < distance)
            {
                best = i;
                distance = Math.Abs(p - number);
            }
        }

        return best;
    }

    /// <summary>
    /// The combination that stands for a feature: each attribute at the
    /// picked feature's value (no value when it has none), the geometry
    /// picked, and each display setting as <paramref name="setting"/> reads
    /// it. Without a pick, or for a value no probed value stands for, an
    /// input takes its first value, which the probe tried first.
    /// </summary>
    /// <param name="probe">The probe.</param>
    /// <param name="feature">The feature, or <see langword="null"/>.</param>
    /// <param name="setting">Reads a display setting's value now (<c>true</c>, <c>30</c>), or <see langword="null"/>.</param>
    /// <param name="isEnumeration">Whether an attribute is an enumeration, whose codes aren't numbers to round; <see langword="null"/> for none.</param>
    /// <returns>An index into each input's values, in <see cref="FeatureTypeProbe.Inputs"/> order.</returns>
    public static int[] Baseline(
        FeatureTypeProbe probe,
        ReferenceFeatureContext? feature,
        Func<string, string?>? setting,
        Func<string, bool>? isEnumeration = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var values = new int[probe.Inputs.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var input = probe.Inputs[i];
            var index = input.Kind switch
            {
                ProbeInputKind.Primitive => feature?.Primitive is { } primitive ? ValueOf(input, primitive) : -1,
                ProbeInputKind.Attribute => feature is { IsPick: true } pick
                    ? ValueOf(input, pick.ValueOf(input.Code), nearest: isEnumeration?.Invoke(input.Code) != true)
                    : -1,
                ProbeInputKind.ContextParameter => setting?.Invoke(input.Code) is { } now ? ValueOf(input, now) : -1,
                _ => -1,
            };
            values[i] = Math.Max(0, index);
        }

        return values;
    }

    /// <summary>Whether a value is a number, read invariantly.</summary>
    /// <param name="text">The value.</param>
    /// <param name="number">The number.</param>
    public static bool TryNumber(string? text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    // "3, 4,3" and "3,4,3" are the same list.
    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(',', value.Split(',', StringSplitOptions.TrimEntries));
}
