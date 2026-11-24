using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

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
}

/// <summary>
/// Why a picked feature was drawn with one part, in words: the conditions of
/// the rule's cases for that part that hold for the feature's values and the
/// current display settings (R11, #1680).
/// </summary>
/// <param name="Kind">How sure the reason is.</param>
/// <param name="Lines">The reason, a line each, with the names of attributes, values and settings as terms.</param>
/// <param name="Settings">The display settings the reason names, each once, in order.</param>
internal sealed record PickReason(PickReasonKind Kind, IReadOnlyList<TermText> Lines, IReadOnlyList<ReferenceKey> Settings)
{
    /// <summary>The reason as plain text, its lines joined by line breaks.</summary>
    public string Text => string.Join('\n', Lines.Select(l => l.Text));
}

/// <summary>What a reason is matched against: the picked feature's values and the display settings.</summary>
/// <param name="Attributes">The pick's attributes (S-101 codes, or S-57 acronyms with an alias).</param>
/// <param name="Geometry">The feature's geometry, or <see cref="S100GeometryType.None"/> when unknown.</param>
/// <param name="Setting">
/// A context parameter's current value, invariant ("true", "30"), or
/// <see langword="null"/> when the viewer doesn't set it, so the
/// catalogue's default holds.
/// </param>
internal sealed record PickFacts(IReadOnlyList<PickAttribute> Attributes, S100GeometryType Geometry, Func<string, string?> Setting);

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
/// makes the reason "Likely". R12's rule trace replaces this for Lua
/// products and keeps its words.
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

    private enum Truth
    {
        Holds,
        Fails,
        Unknown,
    }

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
        var candidates = new List<Candidate>();
        foreach (var use in uses)
        {
            var held = new List<RuleCondition>();
            var unknown = new List<RuleCondition>();
            bool fails = false;
            foreach (var condition in use.Conditions)
            {
                switch (context.Evaluate(condition))
                {
                    case Truth.Holds:
                        held.Add(condition);
                        break;
                    case Truth.Unknown:
                        unknown.Add(condition);
                        break;
                    default:
                        fails = true;
                        break;
                }

                if (fails)
                    break;
            }

            if (!fails)
                candidates.Add(new Candidate(use, held, unknown));
        }

        candidates = Distinct(candidates);
        if (candidates.Count == 0)
            return new PickReason(PickReasonKind.NoMatch, [Strings.Pick_Why_NoMatch], []);

        bool geometry = feature.PermittedPrimitives.Count != 1;
        var settings = SettingsOf(candidates.SelectMany(c => c.Held), index);
        if (candidates.Count == 1)
        {
            var only = candidates[0];
            var held = TermText.Join(RuleConditionText.Separator, RuleConditionText.DescribeParts(only.Held, index, geometry));
            if (only.Unknown.Count == 0)
                return new PickReason(PickReasonKind.Matched, [held.IsEmpty ? Strings.Pick_Why_Always : held], settings);

            var lines = new List<TermText>
            {
                held.IsEmpty ? Strings.Pick_Why_Likely : TermText.Format(Strings.Pick_Why_LikelyFormat, held),
            };
            var unknown = TermText.Join(RuleConditionText.Separator, RuleConditionText.DescribeParts(only.Unknown, index, geometry));
            if (!unknown.IsEmpty)
                lines.Add(TermText.Format(Strings.Pick_Why_NotCheckedFormat, unknown.LowerFirst()));
            return new PickReason(PickReasonKind.Likely, lines, settings);
        }

        // Several cases hold: what each says, with what they share said once
        // ("When simplified symbols is on:"), as the Reference tab says them.
        // A case with a condition that couldn't be checked says so, rather than
        // reading "Always" for want of a condition that held.
        var checkedCases = candidates.Where(c => c.Unknown.Count == 0).ToList();
        var cases = checkedCases.Count == 0
            ? []
            : FeatureTypeEntryViewModel.CasesOf(
                checkedCases.Select(c => new HeldCase { RuleFile = c.Use.RuleFile, Line = c.Use.Line, Conditions = c.Held }),
                index,
                geometry,
                MaxCases);
        foreach (var partial in candidates.Where(c => c.Unknown.Count > 0))
        {
            var unknown = TermText.Join(RuleConditionText.Separator, RuleConditionText.DescribeParts(partial.Unknown, index, geometry));
            var notChecked = TermText.Format(Strings.Pick_Why_NotCheckedFormat, unknown.LowerFirst());
            cases.Add(TermText.Join(RuleConditionText.Separator, [.. RuleConditionText.DescribeParts(partial.Held, index, geometry), notChecked]));
        }

        return new PickReason(PickReasonKind.Likely, [Strings.Pick_Why_LikelyOneOf, .. cases], settings);
    }

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
            .Where(code => context.ValuesOf(code).Count == 0)
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
        var distinct = candidates.DistinctBy(c => string.Join('|', c.Held.Concat(c.Unknown).Select(Identity))).ToList();
        return
        [
            .. distinct.Where(c => !distinct.Any(other =>
                !ReferenceEquals(other, c)
                && other.Unknown.Count == 0
                && other.Held.Count > c.Held.Count + c.Unknown.Count
                && c.Held.Concat(c.Unknown).All(x => other.Held.Any(o => Identity(o) == Identity(x))))),
        ];
    }

    private static string Identity(RuleCondition c) =>
        string.Create(CultureInfo.InvariantCulture, $"{c.Kind}:{c.Subject}:{c.Index}:{c.Operator}:{c.Value}:{c.Text}");

    private static IReadOnlyList<ReferenceKey> SettingsOf(IEnumerable<RuleCondition> conditions, ChartReferenceIndex index) =>
    [
        .. conditions
            .Where(c => c.Kind == RuleConditionKind.ContextParameter && c.Subject is { Length: > 0 })
            .Select(c => new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, c.Subject!))
            .Where(k => index.TryGet(k, out _))
            .Distinct(),
    ];

    private sealed record Candidate(RuleUse Use, List<RuleCondition> Held, List<RuleCondition> Unknown);

    // A case as it holds for the feature: its conditions that were checked.
    private sealed class HeldCase : RuleUse
    {
    }

    // Checks conditions against one pick.
    private sealed class Context(FeatureTypeEntry feature, ChartReferenceIndex index, PickFacts facts)
    {
        public Truth Evaluate(RuleCondition condition) => condition.Kind switch
        {
            RuleConditionKind.Attribute => Attribute(condition),
            RuleConditionKind.ContextParameter => Setting(condition),
            RuleConditionKind.FeatureType => Same(condition, condition.Value is { } code
                && (string.Equals(code, feature.Code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(code, feature.Alias, StringComparison.OrdinalIgnoreCase))),
            RuleConditionKind.Primitive => Primitive(condition),
            // A computed value, an 'or', an else after several cases: not checked.
            _ => Truth.Unknown,
        };

        /// <summary>
        /// The pick's values of an attribute: one per repeated row, and a
        /// listed value's codes from a comma-separated list ("3,4,3"). Empty
        /// when the feature doesn't have it.
        /// </summary>
        public IReadOnlyList<string> ValuesOf(string code)
        {
            var rows = Rows(facts.Attributes, code, topLevel: true).ToList();
            if (rows.Count == 0)
                rows = [.. Rows(facts.Attributes, code, topLevel: false)];
            var entry = PickTerms.Attribute(index, code);
            var values = new List<string>();
            foreach (var row in rows)
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

        private Truth Attribute(RuleCondition condition)
        {
            if (condition.Subject is not { Length: > 0 } code)
                return Truth.Unknown;
            var values = ValuesOf(code);
            switch (condition.Operator)
            {
                case ConditionOperator.Present:
                    return Of(values.Count > 0);
                case ConditionOperator.Absent:
                    return Of(values.Count == 0);
                case ConditionOperator.Holds:
                case ConditionOperator.DoesNotHold:
                    return Truth.Unknown;
            }

            string? value;
            if (condition.Index is { } i)
                value = i >= 1 && i <= values.Count ? values[i - 1] : null;
            else if (values.Count <= 1)
                value = values.Count == 0 ? null : values[0];
            else
                return Truth.Unknown; // A list compared as a whole.

            return Compare(condition, value);
        }

        private Truth Setting(RuleCondition condition)
        {
            if (condition.Subject is not { Length: > 0 } id)
                return Truth.Unknown;
            var value = facts.Setting(id);
            if (value is null
                && index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, id), out var entry)
                && entry is DisplaySettingEntry setting)
            {
                // A parameter the viewer doesn't set: the catalogue's default holds.
                value = setting.Default;
            }

            if (value is null)
                return Truth.Unknown;
            return condition.Operator switch
            {
                ConditionOperator.Present => Of(value.Length > 0),
                ConditionOperator.Absent => Of(value.Length == 0),
                ConditionOperator.Holds or ConditionOperator.DoesNotHold => Truth.Unknown,
                _ => Compare(condition, value),
            };
        }

        private Truth Primitive(RuleCondition condition)
        {
            var primitive = facts.Geometry switch
            {
                S100GeometryType.Point => "Point",
                S100GeometryType.Curve => "Curve",
                S100GeometryType.Surface => "Surface",
                // A feature type drawn one way only is drawn that way.
                _ when feature.PermittedPrimitives.Count == 1 => feature.PermittedPrimitives[0],
                _ => null,
            };
            if (primitive is null || condition.Value is not { } value)
                return Truth.Unknown;
            return Same(condition, string.Equals(primitive, value, StringComparison.OrdinalIgnoreCase));
        }

        private static Truth Same(RuleCondition condition, bool same) => condition.Operator switch
        {
            ConditionOperator.Equal or ConditionOperator.Holds => Of(same),
            ConditionOperator.NotEqual or ConditionOperator.DoesNotHold => Of(!same),
            _ => Truth.Unknown,
        };

        // A comparison with the feature's value, or with nothing when it has
        // none: Lua's nil equals nothing and is false.
        private static Truth Compare(RuleCondition condition, string? value)
        {
            switch (condition.Operator)
            {
                case ConditionOperator.IsTrue:
                    return value is null ? Truth.Fails : Bool(value) is { } on ? Of(on) : Truth.Unknown;
                case ConditionOperator.IsFalse:
                    return value is null ? Truth.Holds : Bool(value) is { } off ? Of(!off) : Truth.Unknown;
            }

            if (value is null)
                return condition.Operator == ConditionOperator.NotEqual ? Truth.Holds : Truth.Fails;
            if (condition.Value is not { } other)
                return Truth.Unknown;

            int order;
            if (Number(value) is { } a && Number(other) is { } b)
                order = a.CompareTo(b);
            else if (Bool(value) is { } x && Bool(other) is { } y)
                order = x.CompareTo(y);
            else if (condition.Operator is ConditionOperator.Equal or ConditionOperator.NotEqual)
                order = string.Compare(value, other.Trim('\'', '"'), StringComparison.OrdinalIgnoreCase);
            else
                return Truth.Unknown;

            return condition.Operator switch
            {
                ConditionOperator.Equal => Of(order == 0),
                ConditionOperator.NotEqual => Of(order != 0),
                ConditionOperator.LessThan => Of(order < 0),
                ConditionOperator.LessThanOrEqual => Of(order <= 0),
                ConditionOperator.GreaterThan => Of(order > 0),
                ConditionOperator.GreaterThanOrEqual => Of(order >= 0),
                _ => Truth.Unknown,
            };
        }

        private static double? Number(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

        private static bool? Bool(string text) => text.Trim().ToLowerInvariant() switch
        {
            "true" or "on" => true,
            "false" or "off" => false,
            _ => null,
        };

        private static Truth Of(bool holds) => holds ? Truth.Holds : Truth.Fails;
    }
}
