using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>How sure a <see cref="PickReason"/> is (symbol dictionary R11, #1680).</summary>
internal enum PickReasonKind
{
    /// <summary>One case of the rule holds, and every condition in it could be checked.</summary>
    Matched,

    /// <summary>More than one case holds, or a condition couldn't be checked: "Likely: …".</summary>
    Likely,

    /// <summary>The rule names the part, but none of its cases holds for this feature.</summary>
    NoMatch,

    /// <summary>The part is the catalogue's fallback symbol: nothing else matched.</summary>
    Fallback,

    /// <summary>The rule draws the part in a way the reference doesn't read: text, or an item it assembles.</summary>
    NotRead,

    /// <summary>A rule trace showed the case that drew the part (R12b, #1719): no "Likely", no note.</summary>
    Traced,
}

/// <summary>
/// Why a picked feature was drawn with one part, in words: the conditions of
/// the rule's cases for that part that hold for the feature's values and the
/// current display settings (R11, #1680).
/// </summary>
/// <param name="Kind">How sure the reason is.</param>
/// <param name="Lines">The reason, a line each, with the names of attributes, values and settings as terms.</param>
/// <param name="Settings">The display settings the reason names, each once, in order.</param>
/// <param name="Note">
/// A quiet note under the reason, "Also depends on values the rule works
/// out", when tests of values the rule computes were left out; or <see langword="null"/>.
/// </param>
internal sealed record PickReason(PickReasonKind Kind, IReadOnlyList<TermText> Lines, IReadOnlyList<ReferenceKey> Settings, string? Note = null)
{
    /// <summary>The reason as plain text, its lines and note joined by line breaks.</summary>
    public string Text => string.Join('\n', Lines.Select(l => l.Text).Append(Note).OfType<string>());
}

/// <summary>What a reason is matched against: the picked feature's values and the display settings.</summary>
/// <param name="Attributes">The pick's attributes (S-101 codes, or S-57 acronyms with an alias).</param>
/// <param name="Geometry">The feature's geometry, or <see cref="S100GeometryType.None"/> when unknown.</param>
/// <param name="Setting">
/// A context parameter's current value, invariant ("true", "30"), or
/// <see langword="null"/> when the viewer doesn't set it, so the
/// catalogue's default holds.
/// </param>
/// <param name="SettingText">A context parameter's current value in words ("30 m", "On"), or null for <paramref name="Setting"/>'s.</param>
internal sealed record PickFacts(
    IReadOnlyList<PickAttribute> Attributes,
    S100GeometryType Geometry,
    Func<string, string?> Setting,
    Func<string, string?>? SettingText = null);

/// <summary>
/// Matches a picked feature against the conditions the rule reader recorded
/// for each part it drew (symbol dictionary design note §4.3, R11, #1680).
/// </summary>
/// <remarks>
/// <para>
/// This is a match, not a trace: for each drawn part the feature type's
/// cases that draw it are kept when their conditions hold. Attribute
/// conditions are checked against the pick's values, context parameter
/// conditions against the current display settings. A condition on a value
/// the rule computes can't be checked; it, or more than one case holding,
/// makes the reason "Likely". For Lua products, a rule trace replaces
/// the match where it can (R12b, #1719) and keeps its words.
/// </para>
/// <para>
/// A case whose conditions are all among another holding case's is left
/// out: what the fuller case says holds, so the shorter adds nothing. That is
/// the elseif chain's order too, where the fuller case comes first.
/// </para>
/// </remarks>
internal static class PickReasons
{
    /// <summary>The most cases a Likely reason lists before "and N more cases".</summary>
    public const int MaxCases = 4;

    /// <summary>Explains one drawn part.</summary>
    /// <param name="part">The part.</param>
    /// <param name="feature">The picked feature's type in <paramref name="index"/>.</param>
    /// <param name="index">The index of the product the feature is portrayed with.</param>
    /// <param name="facts">The feature's values and the display settings.</param>
    /// <returns>The reason.</returns>
    public static PickReason Explain(PickDrawnPart part, FeatureTypeEntry feature, ChartReferenceIndex index, PickFacts facts)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(facts);

        if (part.IsFallback)
            return Fallback(feature, index, facts);
        if (part.Kind == PickDrawnPartKind.Text)
            return new PickReason(PickReasonKind.NotRead, [Strings.Pick_Why_Text], []);

        var uses = UsesOf(part, feature);
        if (uses.Count == 0)
            return new PickReason(PickReasonKind.NotRead, [Strings.Pick_Why_NotRead], []);

        var context = new Context(feature, index, facts);
        bool geometry = feature.PermittedPrimitives.Count != 1;
        var candidates = new List<Candidate>();
        foreach (var use in uses)
        {
            if (Say(use, index, context, geometry, traced: false) is { } candidate)
                candidates.Add(candidate);
        }

        candidates = Distinct(candidates);
        if (candidates.Count == 0)
            return new PickReason(PickReasonKind.NoMatch, [Strings.Pick_Why_NoMatch], []);

        var depends = candidates.Any(c => c.Partial);
        var lines = new List<TermText>();
        PickReasonKind kind;
        var cases = candidates.Select(c => c.Words).Where(w => w.Count > 0).DistinctBy(Key).ToList();
        if (candidates.Count == 1)
        {
            kind = PickReasonKind.Matched;
            lines.Add(cases.Count == 0 ? Strings.Pick_Why_Always : TermText.Join(RuleConditionText.Separator, cases[0]));
        }
        else if (cases.Count <= 1)
        {
            // Several cases, one thing to say: "Likely: …".
            kind = PickReasonKind.Likely;
            lines.Add(cases.Count == 0 ? Strings.Pick_Why_Likely : TermText.Format(Strings.Pick_Why_LikelyFormat, TermText.Join(RuleConditionText.Separator, cases[0])));
        }
        else
        {
            // Several cases are still possible: what each says, with what they
            // share said once ("When simplified symbols is on:"), as the
            // Reference tab says them. A case whose conditions can't be checked
            // stays possible but says nothing.
            kind = PickReasonKind.Likely;
            lines.Add(Strings.Pick_Why_LikelyOneOf);
            lines.AddRange(Factor(cases));
        }

        return new PickReason(kind, lines, SettingsOf(lines), depends ? Strings.Pick_Why_AlsoDepends : null);
    }

    /// <summary>
    /// Explains one drawn part from a rule trace (R12b, #1719): the case
    /// whose line ran, with every condition on the way to it, said as the
    /// match says them. Not "Likely", and with no note: a test of a value the
    /// rule works out is left out quietly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A use's line running shows that every enclosing branch was taken. An
    /// <c>else</c> branch's conditions sit on the <c>else</c> line, where Lua
    /// runs no instruction, so that line is never in the trace; the use's
    /// line, in the branch's body, is the evidence instead.
    /// </para>
    /// <para>
    /// A part the trace can't map falls back to <see cref="Explain"/>: the
    /// fallback symbol and text, which the match already says plainly; a part
    /// the traced portrayal didn't draw (the map was drawn with other
    /// settings); a part none of whose uses' lines ran (an item named on a
    /// line Lua doesn't report); and a part whose uses that ran differ.
    /// </para>
    /// </remarks>
    /// <param name="part">The part.</param>
    /// <param name="feature">The picked feature's type in <paramref name="index"/>.</param>
    /// <param name="index">The index of the product the feature is portrayed with.</param>
    /// <param name="facts">The feature's values and the display settings.</param>
    /// <param name="trace">The feature's rule trace.</param>
    /// <returns>The traced reason, or the match.</returns>
    public static PickReason Explain(PickDrawnPart part, FeatureTypeEntry feature, ChartReferenceIndex index, PickFacts facts, LuaFeatureTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        return Trace(part, feature, index, facts, trace) ?? Explain(part, feature, index, facts);
    }

    /// <summary>The traced reason, or <see langword="null"/> when the trace can't be mapped to the part.</summary>
    internal static PickReason? Trace(PickDrawnPart part, FeatureTypeEntry feature, ChartReferenceIndex index, PickFacts facts, LuaFeatureTrace trace)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(trace);
        if (part.IsFallback || part.Kind == PickDrawnPartKind.Text)
            return null;

        // The traced portrayal drew the part, as the map did.
        var spec = part.Key?.Spec ?? index.Spec;
        if (!trace.Instructions.SelectMany(i => PickDrawnParts.PartsOf(i, spec)).Any(p => Same(p, part)))
            return null;

        var ran = new HashSet<LuaTraceLine>(trace.Lines);
        var taken = UsesOf(part, feature)
            .Where(u => ran.Contains(new LuaTraceLine(u.RuleFile, u.Line)))
            .DistinctBy(u => string.Join('|', u.Conditions.Select(Identity)))
            .ToList();
        // Several uses ran, by different cases: the trace doesn't say which drew it.
        if (taken is not [var use])
            return null;

        var context = new Context(feature, index, facts);
        var said = Say(use, index, context, feature.PermittedPrimitives.Count != 1, traced: true)!;
        var line = said.Words.Count == 0 ? (TermText)Strings.Pick_Why_Always : TermText.Join(RuleConditionText.Separator, said.Words);
        return new PickReason(PickReasonKind.Traced, [line], SettingsOf([line]));
    }

    // A part the trace drew is the part the map drew: the same item, colour
    // or line, whatever the feature reference.
    private static bool Same(PickDrawnPart traced, PickDrawnPart drawn) =>
        traced.Kind == drawn.Kind
        && Nullable.Equals(traced.Key, drawn.Key)
        && string.Equals(traced.Colour, drawn.Colour, StringComparison.Ordinal)
        && string.Equals(traced.Dash, drawn.Dash, StringComparison.Ordinal);

    /// <summary>
    /// A use's conditions in words, with the pick's values: what held, said
    /// as the Reference tab says it. <see langword="null"/> when a condition
    /// fails, unless <paramref name="traced"/>: the trace shows it held, so
    /// it is said as it is written.
    /// </summary>
    private static Candidate? Say(RuleUse use, ChartReferenceIndex index, Context context, bool geometry, bool traced)
    {
        var held = new List<RuleCondition>();
        var words = new List<TermText>();
        var asWritten = new List<RuleCondition>();
        bool partial = false;

        // Conditions said as they are written ("Colour is red", "Simplified
        // symbols is on") are said together, so the bands of one attribute
        // read as one run; the others say what the value is.
        void Flush()
        {
            words.AddRange(RuleConditionText.DescribeParts(asWritten, index, geometry));
            asWritten.Clear();
        }

        foreach (var condition in use.Conditions)
        {
            var check = RuleConditionText.Check(condition, index, context);
            if (check.Truth == ConditionTruth.Fails)
            {
                if (!traced)
                    return null;
                // The pick's values disagree with what the rule saw (a value
                // it works out from them): the condition as written.
                if (condition.Kind is RuleConditionKind.Expression or RuleConditionKind.Otherwise)
                    continue;
                held.Add(condition);
                asWritten.Add(condition);
                continue;
            }

            partial |= check.Partial;
            if (check.Truth == ConditionTruth.Unknown && check.Held.Count == 0)
                continue;
            held.Add(condition);
            if (check.AsWritten)
            {
                asWritten.Add(condition);
            }
            else
            {
                Flush();
                words.AddRange(check.Held);
            }
        }

        Flush();
        return new Candidate(use, held, partial, [.. words.Distinct()]);
    }

    // The cases' words, with the parts every case shares said once first.
    private static List<TermText> Factor(List<List<TermText>> cases)
    {
        var lines = new List<TermText>();
        var common = cases.Count > 1 ? cases[0].Where(p => cases.All(c => c.Contains(p))).ToList() : [];
        if (common.Count > 0)
            lines.Add(TermText.Format(Strings.Reference_CasesWhen, TermText.Join(RuleConditionText.Separator, common).LowerFirst()));
        var rest = cases
            .Select(c => TermText.Join(RuleConditionText.Separator, c.Where(p => !common.Contains(p))))
            .Select(c => c.IsEmpty ? (TermText)Strings.Reference_NoFurtherCondition : c)
            .Distinct()
            .ToList();
        if (rest.Count > MaxCases)
            rest = [.. rest.Take(MaxCases), string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_MoreCases, rest.Count - MaxCases)];
        lines.AddRange(rest);
        return lines;
    }

    private static string Key(List<TermText> words) => string.Join(RuleConditionText.Separator, words.Select(w => w.Text));

    /// <summary>
    /// The rule's uses of the part: the cases that draw the symbol, line
    /// style or fill, or the colour fill or line in its colour.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <param name="feature">The feature type.</param>
    /// <returns>The uses, in the rule's order.</returns>
    internal static IReadOnlyList<RuleUse> UsesOf(PickDrawnPart part, FeatureTypeEntry feature)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(feature);
        switch (part.Kind)
        {
            case PickDrawnPartKind.Symbol:
            case PickDrawnPartKind.LineStyle:
            case PickDrawnPartKind.AreaFill:
                {
                    var key = part.Key!.Value;
                    return
                    [
                        .. feature.Uses.Where(u => u.ItemKind == key.Kind && (
                            string.Equals(u.ItemId, key.Code, StringComparison.OrdinalIgnoreCase)
                            || (u.IsFamily && key.Code.StartsWith(u.ItemId, StringComparison.OrdinalIgnoreCase)))),
                    ];
                }

            case PickDrawnPartKind.ColourFill:
                return [.. feature.ColorFills.Where(f => string.Equals(f.Token, part.Colour, StringComparison.Ordinal))];

            case PickDrawnPartKind.Line:
                {
                    var pens = feature.Pens.Where(p => string.Equals(p.Token, part.Colour, StringComparison.Ordinal)).ToList();
                    // The dash read from the display list, when the rule's pens differ by it.
                    var dashed = pens.Where(p => string.Equals(p.Dash, part.Dash, StringComparison.OrdinalIgnoreCase)).ToList();
                    return dashed.Count > 0 ? dashed : pens;
                }

            default:
                return [];
        }
    }

    // The fallback: nothing else matched. What was missing is knowable when
    // the rule's other cases test attributes the feature doesn't have.
    private static PickReason Fallback(FeatureTypeEntry feature, ChartReferenceIndex index, PickFacts facts)
    {
        var context = new Context(feature, index, facts);
        var missing = feature.Uses
            .Where(u => !LeadGlyph.DefaultSymbols.Contains(u.ItemId))
            .SelectMany(u => u.Conditions)
            .Where(c => c.Kind == RuleConditionKind.Attribute && c.Subject is { Length: > 0 })
            .Select(c => c.Subject!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => context.Values(code).Count == 0)
            .Select(code => index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Attribute, code), out var entry)
                ? TermText.Term(RuleConditionText.SentenceCase(entry.Name), entry.Key)
                : TermText.Plain(code))
            .ToList();
        var lines = new List<TermText> { Strings.Pick_Why_Fallback };
        if (missing.Count > 0)
            lines.Add(TermText.Format(Strings.Pick_Why_MissingFormat, TermText.Join(", ", missing)));
        return new PickReason(PickReasonKind.Fallback, lines, []);
    }

    // Each case once, and none whose conditions all sit in a fuller case that
    // holds as surely.
    private static List<Candidate> Distinct(List<Candidate> candidates)
    {
        var distinct = candidates.DistinctBy(c => string.Join('|', c.Held.Select(Identity)) + (c.Partial ? "?" : string.Empty)).ToList();
        return
        [
            .. distinct.Where(c => !distinct.Any(other =>
                !ReferenceEquals(other, c)
                && !other.Partial
                && other.Held.Count > c.Held.Count
                && c.Held.All(x => other.Held.Any(o => Identity(o) == Identity(x))))),
        ];
    }

    private static string Identity(RuleCondition c) =>
        string.Create(CultureInfo.InvariantCulture, $"{c.Kind}:{c.Subject}:{c.Index}:{c.Operator}:{c.Value}:{c.Text}");

    // The display settings the reason says held, each once: what it links to.
    private static IReadOnlyList<ReferenceKey> SettingsOf(IEnumerable<TermText> lines) =>
    [
        .. lines
            .SelectMany(l => l.Runs)
            .Where(r => r.Key is { Kind: ReferenceKind.DisplaySetting })
            .Select(r => r.Key!.Value)
            .Distinct(),
    ];

    private sealed record Candidate(RuleUse Use, List<RuleCondition> Held, bool Partial, List<TermText> Words);

    // One pick's values and the display settings, as the conditions read them.
    private sealed class Context(FeatureTypeEntry feature, ChartReferenceIndex index, PickFacts facts) : IRuleFacts
    {
        public string? Primitive => facts.Geometry switch
        {
            S100GeometryType.Point => "Point",
            S100GeometryType.Curve => "Curve",
            S100GeometryType.Surface => "Surface",
            // A feature type drawn one way only is drawn that way.
            _ when feature.PermittedPrimitives.Count == 1 => feature.PermittedPrimitives[0],
            _ => null,
        };

        public bool IsFeatureType(string code) =>
            string.Equals(code, feature.Code, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, feature.Alias, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The pick's values of an attribute: one per repeated row, and a
        /// listed value's codes from a comma-separated list ("3,4,3"). Empty
        /// when the feature doesn't have it.
        /// </summary>
        public IReadOnlyList<string> Values(string code)
        {
            var entry = PickTerms.Attribute(index, code);
            var values = new List<string>();
            foreach (var row in RowsOf(code))
            {
                if (string.IsNullOrWhiteSpace(row.RawValue))
                    continue;
                if (entry is { ListedValues.Count: > 0 })
                    values.AddRange(row.RawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                else
                    values.Add(row.RawValue.Trim());
            }

            return values;
        }

        public string? ValueText(string code) =>
            RowsOf(code).ToList() is [{ } only] && !string.IsNullOrWhiteSpace(only.RawValue) ? only.DisplayText : null;

        public string? Setting(string id)
        {
            if (facts.Setting(id) is { } value)
                return value;
            // A parameter the viewer doesn't set: the catalogue's default holds.
            return index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, id), out var entry) && entry is DisplaySettingEntry setting
                ? setting.Default
                : null;
        }

        public string? SettingText(string id) => facts.SettingText?.Invoke(id) ?? Setting(id);

        // Top-level rows first; a sub-attribute's rows when no top-level row names it.
        private IEnumerable<PickAttribute> RowsOf(string code)
        {
            var rows = Rows(facts.Attributes, code, topLevel: true).ToList();
            return rows.Count > 0 ? rows : Rows(facts.Attributes, code, topLevel: false);
        }

        private IEnumerable<PickAttribute> Rows(IEnumerable<PickAttribute> rows, string code, bool topLevel)
        {
            foreach (var row in rows)
            {
                if (Names(row.Code, code))
                    yield return row;
                if (!topLevel)
                {
                    foreach (var child in Rows(row.Children, code, topLevel: false))
                        yield return child;
                }
            }
        }

        // The pick's code names the rule's: "colour", or an S-57 acronym with that alias, "COLOUR".
        private bool Names(string pickCode, string ruleCode) =>
            string.Equals(pickCode, ruleCode, StringComparison.Ordinal)
            || (PickTerms.Attribute(index, pickCode) is { } entry && string.Equals(entry.Code, ruleCode, StringComparison.Ordinal));
    }
}
