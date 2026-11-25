using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>Whether a rule condition holds for one feature (R11, #1680).</summary>
public enum ConditionTruth
{
    /// <summary>It holds.</summary>
    Holds,

    /// <summary>It doesn't.</summary>
    Fails,

    /// <summary>It tests a value the rule works out itself, which the pick doesn't have.</summary>
    Unknown,
}

/// <summary>
/// What a condition is checked against: one feature's values and the
/// current display settings (R11, #1680).
/// </summary>
public interface IRuleFacts
{
    /// <summary>The feature's values of an attribute, raw: listed value codes, numbers. Empty when it has none.</summary>
    /// <param name="code">The attribute code, as the rules name it.</param>
    IReadOnlyList<string> Values(string code);

    /// <summary>The attribute's value as Object information shows it, with its unit: "10 m". Null when it has none.</summary>
    /// <param name="code">The attribute code.</param>
    string? ValueText(string code);

    /// <summary>A context parameter's value, as the rules compare it ("true", "30"), or null when unknown.</summary>
    /// <param name="id">The context parameter id.</param>
    string? Setting(string id);

    /// <summary>A context parameter's value in words: "30 m", "On". Null when unknown.</summary>
    /// <param name="id">The context parameter id.</param>
    string? SettingText(string id);

    /// <summary>The feature's geometric primitive as the rules name it (<c>Point</c>, <c>Curve</c>, <c>Surface</c>), or null.</summary>
    string? Primitive { get; }

    /// <summary>Whether the feature is of the named feature type.</summary>
    /// <param name="code">A feature type code.</param>
    bool IsFeatureType(string code);
}

/// <summary>
/// A condition checked against a feature (R11, #1680): whether it holds and,
/// when it does, what held, in words with the feature's own values.
/// </summary>
/// <param name="Truth">Whether it holds.</param>
/// <param name="Held">
/// What held, in words, with the values inline: "Surrounding depth (10 m) is
/// less than the safety contour (30 m)". Empty when the condition is best
/// said as the rule says it (<see cref="AsWritten"/>).
/// </param>
/// <param name="AsWritten">Whether the condition says what held as it is written, as "Colour is red" does.</param>
/// <param name="Partial">Whether part of it tests a value the rule works out, which was left out.</param>
public sealed record CheckedCondition(ConditionTruth Truth, IReadOnlyList<TermText> Held, bool AsWritten, bool Partial)
{
    /// <summary>A condition that can't be checked.</summary>
    public static CheckedCondition Unknown { get; } = new(ConditionTruth.Unknown, [], false, true);
}

/// <summary>
/// Checks rule conditions against one feature and says what held with the
/// feature's values (symbol dictionary R11, #1680): the same reader and
/// names as the Reference tab's cases, with the values filled in.
/// </summary>
public static partial class RuleConditionText
{
    /// <summary>Checks one condition against a feature.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="index">The index whose entries name the attributes, values and settings.</param>
    /// <param name="facts">The feature's values and the display settings.</param>
    /// <returns>Whether it holds, and what held.</returns>
    public static CheckedCondition Check(RuleCondition condition, ChartReferenceIndex index, IRuleFacts facts)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(facts);

        switch (condition.Kind)
        {
            case RuleConditionKind.Attribute:
                return CheckAttribute(condition, index, facts);

            case RuleConditionKind.ContextParameter:
                {
                    if (condition.Subject is not { Length: > 0 } id || facts.Setting(id) is not { } value)
                        return CheckedCondition.Unknown;
                    var truth = condition.Operator switch
                    {
                        ConditionOperator.Present => Of(value.Length > 0),
                        ConditionOperator.Absent => Of(value.Length == 0),
                        ConditionOperator.Holds or ConditionOperator.DoesNotHold => ConditionTruth.Unknown,
                        _ => Test(condition.Operator, value, condition.Value),
                    };
                    return truth == ConditionTruth.Unknown ? CheckedCondition.Unknown : new(truth, [], AsWritten: true, Partial: false);
                }

            case RuleConditionKind.FeatureType:
                {
                    if (condition.Value is not { } code)
                        return CheckedCondition.Unknown;
                    var same = facts.IsFeatureType(code);
                    return Plain(condition.Operator switch
                    {
                        ConditionOperator.Equal or ConditionOperator.Holds => Of(same),
                        ConditionOperator.NotEqual or ConditionOperator.DoesNotHold => Of(!same),
                        _ => ConditionTruth.Unknown,
                    });
                }

            case RuleConditionKind.Primitive:
                {
                    if (facts.Primitive is not { } primitive || condition.Value is not { } value)
                        return CheckedCondition.Unknown;
                    var same = string.Equals(primitive, value, StringComparison.OrdinalIgnoreCase);
                    return Plain(condition.Operator switch
                    {
                        ConditionOperator.Equal or ConditionOperator.Holds => Of(same),
                        ConditionOperator.NotEqual or ConditionOperator.DoesNotHold => Of(!same),
                        _ => ConditionTruth.Unknown,
                    });
                }

            case RuleConditionKind.Expression:
                {
                    if (string.IsNullOrWhiteSpace(condition.Text))
                        return CheckedCondition.Unknown;
                    try
                    {
                        var parser = new Parser(Tokenize(condition.Text));
                        var node = parser.ParseOr();
                        if (!parser.AtEnd)
                            return CheckedCondition.Unknown;
                        var (truth, held, partial) = Eval(node, condition.Operator == ConditionOperator.DoesNotHold, index, facts);
                        return new(truth, held, AsWritten: false, partial || truth == ConditionTruth.Unknown);
                    }
                    catch (FormatException)
                    {
                        return CheckedCondition.Unknown;
                    }
                }

            default:
                // An else after several cases: none of them is known here.
                return CheckedCondition.Unknown;
        }
    }

    // A comparison with zero that holds, as UDWHAZ05's
    // "surroundingDepth >= scaledDecimalZero": checked, so a case where it
    // fails is dropped, but it says nothing a mariner needs.
    private static CheckedCondition Silent { get; } = new(ConditionTruth.Holds, [], AsWritten: false, Partial: false);

    private static bool IsZero(string? value) =>
        value is not null && Number(value.Trim('\'', '"')) is 0d;

    private static CheckedCondition Plain(ConditionTruth truth) =>
        truth == ConditionTruth.Unknown ? CheckedCondition.Unknown : new(truth, [], AsWritten: true, Partial: false);

    // colour[1] == 3: an equality holds as it is written; any other test is
    // said with the feature's value.
    private static CheckedCondition CheckAttribute(RuleCondition condition, ChartReferenceIndex index, IRuleFacts facts)
    {
        if (condition.Subject is not { Length: > 0 } code)
            return CheckedCondition.Unknown;
        var values = facts.Values(code);
        var attribute = Attribute(index, code);
        var name = AttributeName(attribute, code);
        switch (condition.Operator)
        {
            case ConditionOperator.Present:
            case ConditionOperator.Absent:
                {
                    bool present = values.Count > 0;
                    var truth = Of(present == (condition.Operator == ConditionOperator.Present));
                    return new(truth, [ActualValue(name, attribute, code, values, facts)], false, false);
                }

            case ConditionOperator.Holds:
            case ConditionOperator.DoesNotHold:
                return CheckedCondition.Unknown;
        }

        string? value;
        if (condition.Index is { } i)
            value = i >= 1 && i <= values.Count ? values[i - 1] : null;
        else if (values.Count <= 1)
            value = values.Count == 0 ? null : values[0];
        else
            return CheckedCondition.Unknown; // A list compared as a whole.

        var result = Test(condition.Operator, value, condition.Value);
        if (result == ConditionTruth.Unknown)
            return CheckedCondition.Unknown;
        if (result == ConditionTruth.Holds && attribute is not { ListedValues.Count: > 0 } && IsZero(condition.Value))
            return Silent;
        if (condition.Operator == ConditionOperator.Equal)
            return new(result, [], AsWritten: true, Partial: false);
        if (attribute is { ListedValues.Count: > 0 } || value is null)
            return new(result, [ActualValue(name, attribute, code, value is null ? [] : [value], facts)], false, false);
        var rhs = condition.Value is { } number && double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? TermText.Plain(n.ToString(CultureInfo.CurrentCulture))
            : TermText.Plain(condition.Value);
        return new(result, [WithValue(condition.Operator, name, facts.ValueText(code) ?? value, rhs)], false, false);
    }

    // ---- Expressions ----

    private static (ConditionTruth Truth, List<TermText> Held, bool Partial) Eval(Node node, bool negate, ChartReferenceIndex index, IRuleFacts facts)
    {
        switch (node)
        {
            case NotNode not:
                return Eval(not.Operand, !negate, index, facts);

            case JoinNode join:
                {
                    // De Morgan: "a or b" fails as "not a and not b".
                    bool isOr = join.IsOr != negate;
                    var parts = join.Parts.Select(p => Eval(p, negate, index, facts)).ToList();
                    if (isOr)
                    {
                        if (parts.FirstOrDefault(p => p.Truth == ConditionTruth.Holds) is { Held: not null } held)
                            return (ConditionTruth.Holds, held.Held, held.Partial);
                        return parts.All(p => p.Truth == ConditionTruth.Fails)
                            ? (ConditionTruth.Fails, [], false)
                            : (ConditionTruth.Unknown, [], true);
                    }

                    if (parts.Any(p => p.Truth == ConditionTruth.Fails))
                        return (ConditionTruth.Fails, [], false);
                    var all = parts.Where(p => p.Truth == ConditionTruth.Holds).SelectMany(p => p.Held).ToList();
                    bool partial = parts.Any(p => p.Partial || p.Truth == ConditionTruth.Unknown);
                    return (parts.All(p => p.Truth == ConditionTruth.Holds) ? ConditionTruth.Holds : ConditionTruth.Unknown, all, partial);
                }

            case PresenceNode presence:
                return EvalPresence(presence.Path, negate, index, facts);

            case CompareNode compare:
                return EvalCompare(compare, negate, index, facts);

            case ContainsNode contains:
                {
                    if (Subject(contains.Path, index) is not { Setting: false } subject)
                        return (ConditionTruth.Unknown, [], true);
                    var code = Last(contains.Path);
                    var values = facts.Values(code);
                    bool any = values.Any(v => contains.Values.Any(c => Test(ConditionOperator.Equal, v, c) == ConditionTruth.Holds));
                    return (Of(any != negate), [ActualValue(subject.Name, subject.Attribute, code, values, facts)], false);
                }

            default:
                return (ConditionTruth.Unknown, [], true);
        }
    }

    private static (ConditionTruth, List<TermText>, bool) EvalPresence(string path, bool negate, ChartReferenceIndex index, IRuleFacts facts)
    {
        if (Subject(path, index) is not { } subject)
            return (ConditionTruth.Unknown, [], true);
        if (subject.Setting)
        {
            var id = path["contextParameters.".Length..];
            if (Bool(facts.Setting(id)) is not { } on)
                return (ConditionTruth.Unknown, [], true);
            return (Of(on != negate), [Format(on ? Strings.ChartReference_Condition_On : Strings.ChartReference_Condition_Off, subject.Name)], false);
        }

        var code = Last(path);
        var values = facts.Values(code);
        bool present = subject.Attribute is { ValueType: var type } && IsBoolean(type)
            ? values.Count > 0 && Bool(values[0]) == true
            : values.Count > 0;
        return (Of(present != negate), [ActualValue(subject.Name, subject.Attribute, code, values, facts)], false);
    }

    private static (ConditionTruth, List<TermText>, bool) EvalCompare(CompareNode compare, bool negate, ChartReferenceIndex index, IRuleFacts facts)
    {
        var op = negate ? Negate(compare.Op) : compare.Op;

        if (Last(compare.Path).Equals("PrimitiveType", StringComparison.OrdinalIgnoreCase))
        {
            var primitive = compare.Value.Split('.')[^1];
            if (facts.Primitive is not { } actual)
                return (ConditionTruth.Unknown, [], true);
            var truth = CompareText(op, actual, primitive);
            return (truth, [Compare(op, Strings.ChartReference_Condition_Geometry, Geometry(primitive))], false);
        }

        if (Subject(compare.Path, index) is not { } subject)
            return (ConditionTruth.Unknown, [], true);
        if (Operand(compare.Value, index, facts) is not { } rhs)
            return (ConditionTruth.Unknown, [], true);

        if (subject.Setting)
        {
            var id = compare.Path["contextParameters.".Length..];
            if (facts.Setting(id) is not { } value)
                return (ConditionTruth.Unknown, [], true);
            var truth = Test(op, value, rhs.Raw);
            if (truth == ConditionTruth.Unknown)
                return (truth, [], true);
            if (Bool(value) is { } on)
                return (truth, [Format(on ? Strings.ChartReference_Condition_On : Strings.ChartReference_Condition_Off, subject.Name)], false);
            return (truth, [WithValue(op, subject.Name, facts.SettingText(id) ?? value, rhs.Words)], false);
        }

        var code = Last(compare.Path);
        var values = facts.Values(code);
        // An index the reader dropped, as in colour[1]: the first value.
        string? first = values.Count == 0 ? null : values[0];
        var result = rhs.Raw is not null ? Test(op, first, rhs.Raw)
            : op == ConditionOperator.Equal ? Of(first is null)
            : op == ConditionOperator.NotEqual ? Of(first is not null)
            : ConditionTruth.Unknown;
        if (result == ConditionTruth.Unknown)
            return (result, [], true);

        if (rhs.Raw is null || first is null || subject.Attribute is { ListedValues.Count: > 0 } || Bool(first) is not null)
            return (result, [ActualValue(subject.Name, subject.Attribute, code, values, facts)], false);
        if (result == ConditionTruth.Holds && IsZero(rhs.Raw))
            return (result, [], false);
        return (result, [WithValue(op, subject.Name, facts.ValueText(code) ?? first, rhs.Words)], false);
    }

    // What a value compared with is: a number, a setting's value, nil.
    // A rule's own local is none of these.
    private static (string? Raw, TermText Words)? Operand(string token, ChartReferenceIndex index, IRuleFacts facts)
    {
        if (token == "nil")
            return (null, TermText.Empty);
        if (token == "scaledDecimalZero")
            return ("0", TermText.Plain(0.ToString(CultureInfo.CurrentCulture)));
        if (token is "true" or "false")
            return (token, TermText.Plain(token));
        if (token.StartsWith("contextParameters.", StringComparison.Ordinal))
        {
            var id = token["contextParameters.".Length..];
            if (!index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, id), out _) || facts.Setting(id) is not { } value)
                return null;
            var name = SettingName(index, id).LowerFirst();
            var words = facts.SettingText(id) is { } text
                ? Format(Strings.Pick_Why_SettingValueFormat, name, text)
                : Format(Strings.Pick_Why_SettingFormat, name);
            return (value, words);
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (token, TermText.Plain(number.ToString(CultureInfo.CurrentCulture)))
            : null;
    }

    // "Water level effect is always under water", "Surrounding depth is
    // 10 m", "Category of wreck is not given": what the value is, not what
    // it isn't.
    private static TermText ActualValue(TermText name, AttributeEntry? attribute, string code, IReadOnlyList<string> values, IRuleFacts facts)
    {
        if (values.Count == 0)
            return Format(Strings.ChartReference_Condition_NotGiven, name);
        if (attribute is { ListedValues.Count: > 0 })
        {
            // The bands in order, as the condition says them: "red, green, red".
            var labels = values.Select(v => ValueLabel(attribute, v)).ToList();
            var list = labels.Count == 1 ? WithArticle(attribute, labels[0]) : TermText.Join(", ", labels);
            return Format(Strings.ChartReference_Condition_Is, name, list);
        }

        if (attribute is { ValueType: var type } && IsBoolean(type) && Bool(values[0]) is { } yes)
            return YesNo(name, yes);
        return Format(Strings.ChartReference_Condition_Is, name, facts.ValueText(code) ?? values[0]);
    }

    // "Surrounding depth (10 m) is less than the safety contour (30 m)".
    private static TermText WithValue(ConditionOperator op, TermText name, string value, TermText rhs) =>
        Compare(op, Format(Strings.Pick_Why_ValueFormat, name, value), rhs);

    // Lua's comparisons, with nil equal to nothing.
    private static ConditionTruth Test(ConditionOperator op, string? value, string? other)
    {
        switch (op)
        {
            case ConditionOperator.IsTrue:
                return value is null ? ConditionTruth.Fails : Bool(value) is { } on ? Of(on) : ConditionTruth.Unknown;
            case ConditionOperator.IsFalse:
                return value is null ? ConditionTruth.Holds : Bool(value) is { } off ? Of(!off) : ConditionTruth.Unknown;
        }

        if (value is null)
            return op == ConditionOperator.NotEqual ? ConditionTruth.Holds : ConditionTruth.Fails;
        if (other is null)
            return ConditionTruth.Unknown;
        return CompareText(op, value, other.Trim('\'', '"'));
    }

    private static ConditionTruth CompareText(ConditionOperator op, string value, string other)
    {
        int order;
        if (Number(value) is { } a && Number(other) is { } b)
            order = a.CompareTo(b);
        else if (Bool(value) is { } x && Bool(other) is { } y)
            order = x.CompareTo(y);
        else if (op is ConditionOperator.Equal or ConditionOperator.NotEqual)
            order = string.Compare(value, other, StringComparison.OrdinalIgnoreCase);
        else
            return ConditionTruth.Unknown;

        return op switch
        {
            ConditionOperator.Equal => Of(order == 0),
            ConditionOperator.NotEqual => Of(order != 0),
            ConditionOperator.LessThan => Of(order < 0),
            ConditionOperator.LessThanOrEqual => Of(order <= 0),
            ConditionOperator.GreaterThan => Of(order > 0),
            ConditionOperator.GreaterThanOrEqual => Of(order >= 0),
            _ => ConditionTruth.Unknown,
        };
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static bool? Bool(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "true" or "on" => true,
        "false" or "off" => false,
        _ => null,
    };

    private static ConditionTruth Of(bool holds) => holds ? ConditionTruth.Holds : ConditionTruth.Fails;
}
